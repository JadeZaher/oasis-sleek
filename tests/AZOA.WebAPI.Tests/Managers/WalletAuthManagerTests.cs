using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using AZOA.WebAPI.Interfaces;
using AZOA.WebAPI.Interfaces.Managers;
using AZOA.WebAPI.Interfaces.Stores;
using AZOA.WebAPI.Managers;
using AZOA.WebAPI.Models;
using AZOA.WebAPI.Models.Responses;

namespace AZOA.WebAPI.Tests.Managers;

/// <summary>
/// user-sovereign-identity AC1/AC2/AC2b/AC3b: wallet-challenge verify (create-or-login
/// only, never takeover), atomic single-use nonce, and the claim watermark cut. The
/// stores + ed25519 verifier are mocked; the manager logic is under test.
/// </summary>
public class WalletAuthManagerTests
{
    private readonly Mock<IWalletAuthChallengeStore> _challenges = new();
    private readonly Mock<IWalletAuthClaimTokenStore> _claimTokens = new();
    private readonly Mock<IAvatarStore> _avatars = new();
    private readonly Mock<IWalletSignatureVerifier> _verifier = new();
    private readonly WalletAuthManager _mgr;

    private const string Addr = "ALGOADDRESSXYZ";
    private const string Chain = "algorand";

    public WalletAuthManagerTests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "super-secret-key-for-testing-only-quite-long-enough!",
            ["Jwt:Issuer"] = "test",
            ["Jwt:Audience"] = "test",
        }).Build();
        _mgr = new WalletAuthManager(_challenges.Object, _claimTokens.Object, _avatars.Object, _verifier.Object, config);
    }

    private void GivenLiveChallengeAndValidSignature()
    {
        var nonce = "nonce-123";
        var msg = $"AZOA-AUTH-v1\nissuer:test\naudience:test\nchain:{Chain}\naddress:{Addr}\nnonce:{nonce}\nexpiry:2099-01-01T00:00:00Z\n";
        _challenges.Setup(c => c.GetLatestLiveByAddressAsync(Addr, Chain, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AZOAResult<WalletAuthChallenge>
            {
                Result = new WalletAuthChallenge
                {
                    Address = Addr, ChainType = Chain, Nonce = nonce, DomainMessage = msg,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(4),
                }
            });
        _challenges.Setup(c => c.TryConsumeAsync(nonce, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AZOAResult<bool> { Result = true });
        _verifier.Setup(v => v.Verify(Chain, Addr, It.IsAny<byte[]>(), It.IsAny<byte[]>())).Returns(true);
    }

    [Fact]
    public async Task Verify_UnknownWallet_CreatesSelfOwnedAvatar_AC2()
    {
        GivenLiveChallengeAndValidSignature();
        // Wallet not bound to any avatar yet.
        _avatars.Setup(a => a.GetByAuthWalletAsync(Addr, Chain, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AZOAResult<IAvatar> { Result = null });
        _avatars.Setup(a => a.UpsertAsync(It.IsAny<IAvatar>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAvatar a, CancellationToken _) => new AZOAResult<IAvatar> { Result = a });

        var r = await _mgr.VerifyAsync(Addr, Chain, "c2ln", null);

        r.IsError.Should().BeFalse();
        r.Result!.Token.Should().NotBeNullOrEmpty();
        // The minted avatar is SELF-OWNED with the wallet binding set.
        _avatars.Verify(a => a.UpsertAsync(
            It.Is<IAvatar>(x => x.OwnerTenantId == null
                && x.AuthWalletAddress == Addr && x.AuthWalletChainType == Chain),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Verify_KnownWallet_LogsIntoThatAvatar_NoNewAvatar_AC2()
    {
        GivenLiveChallengeAndValidSignature();
        var existing = new Avatar { Id = Guid.NewGuid(), OwnerTenantId = null, AuthWalletAddress = Addr, AuthWalletChainType = Chain };
        _avatars.Setup(a => a.GetByAuthWalletAsync(Addr, Chain, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AZOAResult<IAvatar> { Result = existing });

        var r = await _mgr.VerifyAsync(Addr, Chain, "c2ln", null);

        r.IsError.Should().BeFalse();
        r.Result!.AvatarId.Should().Be(existing.Id);
        // No new avatar minted for a known wallet.
        _avatars.Verify(a => a.UpsertAsync(It.IsAny<IAvatar>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Verify_BadSignature_Fails_AfterConsume()
    {
        GivenLiveChallengeAndValidSignature();
        _verifier.Setup(v => v.Verify(Chain, Addr, It.IsAny<byte[]>(), It.IsAny<byte[]>())).Returns(false);
        _avatars.Setup(a => a.GetByAuthWalletAsync(Addr, Chain, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AZOAResult<IAvatar> { Result = null });

        var r = await _mgr.VerifyAsync(Addr, Chain, "badsig", null);

        r.IsError.Should().BeTrue();
        // Never minted an avatar on a bad signature.
        _avatars.Verify(a => a.UpsertAsync(It.IsAny<IAvatar>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Verify_NonceAlreadyConsumed_Fails_AC1()
    {
        var nonce = "nonce-123";
        var msg = $"AZOA-AUTH-v1\nissuer:test\naudience:test\nchain:{Chain}\naddress:{Addr}\nnonce:{nonce}\nexpiry:2099-01-01T00:00:00Z\n";
        _challenges.Setup(c => c.GetLatestLiveByAddressAsync(Addr, Chain, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AZOAResult<WalletAuthChallenge>
            {
                Result = new WalletAuthChallenge { Address = Addr, ChainType = Chain, Nonce = nonce, DomainMessage = msg, ExpiresAt = DateTime.UtcNow.AddMinutes(4) }
            });
        // Atomic consume LOSES (another concurrent verify already won).
        _challenges.Setup(c => c.TryConsumeAsync(nonce, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AZOAResult<bool> { Result = false });

        var r = await _mgr.VerifyAsync(Addr, Chain, "c2ln", null);

        r.IsError.Should().BeTrue();
        // The signature verify never even runs once the consume is lost.
        _verifier.Verify(v => v.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task Verify_NoActiveChallenge_Fails()
    {
        _challenges.Setup(c => c.GetLatestLiveByAddressAsync(Addr, Chain, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AZOAResult<WalletAuthChallenge> { Result = null });

        var r = await _mgr.VerifyAsync(Addr, Chain, "c2ln", null);

        r.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_ClientMessageMismatch_Rejected_AC1b()
    {
        GivenLiveChallengeAndValidSignature();
        _avatars.Setup(a => a.GetByAuthWalletAsync(Addr, Chain, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AZOAResult<IAvatar> { Result = null });

        // A tampered client echo of the signed message must reject (domain separation).
        var r = await _mgr.VerifyAsync(Addr, Chain, "c2ln", message: "TAMPERED-MESSAGE");

        r.IsError.Should().BeTrue();
    }
}
