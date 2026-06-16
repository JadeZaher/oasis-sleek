using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using OASIS.WebAPI.Core;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Managers;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Tests.Managers;

/// <summary>
/// Unit coverage for the tenant provisioning manager. The load-bearing tests are
/// the cross-tenant ownership guard (acceptance c proven at the unit layer, since
/// the integration harness's per-test namespace isolation is a known open issue —
/// see project memory integration-test-namespace-isolation) and the
/// scope-delegation ceiling (acceptance d).
/// </summary>
public class TenantManagerTests
{
    private readonly Mock<IAvatarStore> _store;
    private readonly TenantManager _manager;

    public TenantManagerTests()
    {
        _store = new Mock<IAvatarStore>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "super-secret-key-for-testing-only!",
                ["Jwt:Issuer"] = "test",
                ["Jwt:Audience"] = "test"
            })
            .Build();

        _manager = new TenantManager(_store.Object, config);
    }

    // ── Provision ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ProvisionChild_New_SetsOwnerTenantFromParameter()
    {
        var tenantId = Guid.NewGuid();
        _store.Setup(s => s.GetByTenantAndExternalUserAsync(tenantId, "user-42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<IAvatar> { Result = null });
        _store.Setup(s => s.UpsertAsync(It.IsAny<IAvatar>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAvatar a, CancellationToken _) => new OASISResult<IAvatar> { Result = a });

        var result = await _manager.ProvisionChildAsync(tenantId, new ProvisionChildModel { ExternalUserId = "user-42" });

        result.IsError.Should().BeFalse();
        result.Result!.ExternalUserId.Should().Be("user-42");
        // The persisted child's OwnerTenantId comes from the PARAMETER.
        _store.Verify(s => s.UpsertAsync(
            It.Is<IAvatar>(a => a.OwnerTenantId == tenantId && a.ExternalUserId == "user-42"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProvisionChild_Idempotent_ReturnsExistingChildNoDuplicate()
    {
        var tenantId = Guid.NewGuid();
        var existing = new Avatar
        {
            Id = Guid.NewGuid(),
            OwnerTenantId = tenantId,
            ExternalUserId = "user-42",
            Username = "existing",
            Email = "e@x.local"
        };
        _store.Setup(s => s.GetByTenantAndExternalUserAsync(tenantId, "user-42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<IAvatar> { Result = existing });

        var result = await _manager.ProvisionChildAsync(tenantId, new ProvisionChildModel { ExternalUserId = "user-42" });

        result.IsError.Should().BeFalse();
        result.Result!.AvatarId.Should().Be(existing.Id);
        // No second avatar written.
        _store.Verify(s => s.UpsertAsync(It.IsAny<IAvatar>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProvisionChild_MissingExternalUserId_ReturnsError()
    {
        var result = await _manager.ProvisionChildAsync(Guid.NewGuid(), new ProvisionChildModel { ExternalUserId = "" });

        result.IsError.Should().BeTrue();
    }

    // ── Cross-tenant ownership guard (acceptance c — the load-bearing test) ───

    [Fact]
    public async Task IssueChildCredential_OwnChild_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        _store.Setup(s => s.GetByIdAsync(childId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<IAvatar>
            {
                Result = new Avatar { Id = childId, OwnerTenantId = tenantId, ExternalUserId = "u1" }
            });

        var result = await _manager.IssueChildCredentialAsync(
            tenantId, childId,
            requestedScopes: new[] { OasisScopes.WalletManage },
            tenantScopes: new[] { OasisScopes.TenantProvision, OasisScopes.WalletManage, OasisScopes.NftMint });

        result.IsError.Should().BeFalse();
        result.Result!.AvatarId.Should().Be(childId);
        result.Result.Token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task IssueChildCredential_OtherTenantsChild_ReturnsNotFound_Not403()
    {
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var childId = Guid.NewGuid();
        // Child belongs to T2; T1 asks for a credential.
        _store.Setup(s => s.GetByIdAsync(childId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<IAvatar>
            {
                Result = new Avatar { Id = childId, OwnerTenantId = t2 }
            });

        var result = await _manager.IssueChildCredentialAsync(
            t1, childId, Array.Empty<string>(), new[] { OasisScopes.WalletManage });

        result.IsError.Should().BeTrue();
        // 404, NOT 403 — a prober cannot distinguish "no such avatar" from
        // "exists but belongs to another tenant".
        result.Message.Should().StartWith(TenantAuthorizationError.NotFound);
        result.Message.Should().NotStartWith(TenantAuthorizationError.Forbidden);
    }

    [Fact]
    public async Task IssueChildCredential_UnownedAvatar_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        // OwnerTenantId == null (a legacy / self-registered avatar).
        _store.Setup(s => s.GetByIdAsync(childId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<IAvatar>
            {
                Result = new Avatar { Id = childId, OwnerTenantId = null }
            });

        var result = await _manager.IssueChildCredentialAsync(
            tenantId, childId, Array.Empty<string>(), new[] { OasisScopes.WalletManage });

        result.IsError.Should().BeTrue();
        result.Message.Should().StartWith(TenantAuthorizationError.NotFound);
    }

    // ── Scope-delegation ceiling (acceptance d) ───────────────────────────────

    [Fact]
    public async Task IssueChildCredential_CannotExceedTenantScopes()
    {
        var tenantId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        _store.Setup(s => s.GetByIdAsync(childId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<IAvatar>
            {
                Result = new Avatar { Id = childId, OwnerTenantId = tenantId }
            });

        // Tenant holds ONLY wallet:manage; child requests wallet:manage + nft:mint.
        var result = await _manager.IssueChildCredentialAsync(
            tenantId, childId,
            requestedScopes: new[] { OasisScopes.WalletManage, OasisScopes.NftMint },
            tenantScopes: new[] { OasisScopes.TenantProvision, OasisScopes.WalletManage });

        result.IsError.Should().BeFalse();
        // nft:mint is dropped (not held by tenant); tenant:provision never delegates.
        result.Result!.Scopes.Should().BeEquivalentTo(new[] { OasisScopes.WalletManage });
        result.Result.Scopes.Should().NotContain(OasisScopes.NftMint);
        result.Result.Scopes.Should().NotContain(OasisScopes.TenantProvision);
    }

    [Fact]
    public async Task IssueChildCredential_EmptyRequest_DelegatesFullTenantSetMinusProvision()
    {
        var tenantId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        _store.Setup(s => s.GetByIdAsync(childId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<IAvatar>
            {
                Result = new Avatar { Id = childId, OwnerTenantId = tenantId }
            });

        var result = await _manager.IssueChildCredentialAsync(
            tenantId, childId,
            requestedScopes: Array.Empty<string>(),
            tenantScopes: new[] { OasisScopes.TenantProvision, OasisScopes.WalletManage, OasisScopes.NftMint });

        result.IsError.Should().BeFalse();
        result.Result!.Scopes.Should().BeEquivalentTo(new[] { OasisScopes.WalletManage, OasisScopes.NftMint });
        result.Result.Scopes.Should().NotContain(OasisScopes.TenantProvision);
    }

    [Fact]
    public async Task IssueChildCredential_TokenSubjectIsChild_WithDelegatedScopeClaims()
    {
        var tenantId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        _store.Setup(s => s.GetByIdAsync(childId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<IAvatar>
            {
                Result = new Avatar { Id = childId, OwnerTenantId = tenantId }
            });

        var result = await _manager.IssueChildCredentialAsync(
            tenantId, childId,
            new[] { OasisScopes.WalletManage },
            new[] { OasisScopes.WalletManage });

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Result!.Token);
        jwt.Subject.Should().Be(childId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "scope" && c.Value == OasisScopes.WalletManage);
    }

    // ── List / Resolve scoping ────────────────────────────────────────────────

    [Fact]
    public async Task ResolveChild_NoMatch_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        _store.Setup(s => s.GetByTenantAndExternalUserAsync(tenantId, "ghost", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<IAvatar> { Result = null });

        var result = await _manager.ResolveChildAsync(tenantId, "ghost");

        result.IsError.Should().BeTrue();
        result.Message.Should().StartWith(TenantAuthorizationError.NotFound);
    }

    [Fact]
    public async Task ListChildren_FilterByExternalUserId_ReturnsOnlyMatch()
    {
        var tenantId = Guid.NewGuid();
        _store.Setup(s => s.ListByOwnerTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OASISResult<IEnumerable<IAvatar>>
            {
                Result = new IAvatar[]
                {
                    new Avatar { Id = Guid.NewGuid(), OwnerTenantId = tenantId, ExternalUserId = "a" },
                    new Avatar { Id = Guid.NewGuid(), OwnerTenantId = tenantId, ExternalUserId = "b" }
                }
            });

        var result = await _manager.ListChildrenAsync(tenantId, "b");

        result.Result.Should().ContainSingle(c => c.ExternalUserId == "b");
    }
}

/// <summary>
/// Coverage for the single reusable scope-check helper used by the TenantScope
/// policy and the credential-issuer.
/// </summary>
public class ClaimsPrincipalScopeTests
{
    private static ClaimsPrincipal PrincipalWith(params string[] scopes)
    {
        var claims = scopes.Select(s => new Claim("scope", s));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Fact]
    public void HasScope_True_WhenScopeClaimPresent()
    {
        var p = PrincipalWith(OasisScopes.TenantProvision, OasisScopes.WalletManage);
        p.HasScope(OasisScopes.TenantProvision).Should().BeTrue();
    }

    [Fact]
    public void HasScope_False_WhenAbsent()
    {
        var p = PrincipalWith(OasisScopes.WalletManage);
        p.HasScope(OasisScopes.TenantProvision).Should().BeFalse();
    }

    [Fact]
    public void HasScope_False_ForEmptyScopeArgument()
    {
        var p = PrincipalWith(OasisScopes.TenantProvision);
        p.HasScope("").Should().BeFalse();
    }

    [Fact]
    public void GetScopes_ReturnsDistinctTrimmedValues()
    {
        var p = PrincipalWith(OasisScopes.WalletManage, OasisScopes.WalletManage, OasisScopes.NftMint);
        p.GetScopes().Should().BeEquivalentTo(new[] { OasisScopes.WalletManage, OasisScopes.NftMint });
    }
}
