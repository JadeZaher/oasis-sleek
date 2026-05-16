using FluentAssertions;
using FluentValidation.TestHelper;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Validation;

namespace OASIS.WebAPI.Tests.Validation;

public class AvatarRegisterValidatorTests
{
    private readonly AvatarRegisterValidator _validator = new();

    [Fact]
    public void ValidModel_ShouldNotHaveErrors()
    {
        var model = new AvatarRegisterModel { Username = "validuser", Email = "test@example.com", Password = "Password123" };
        var result = _validator.TestValidate(model);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyUsername_ShouldHaveError()
    {
        var model = new AvatarRegisterModel { Username = "", Email = "test@example.com", Password = "Password123" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Username);
    }

    [Fact]
    public void ShortUsername_ShouldHaveError()
    {
        var model = new AvatarRegisterModel { Username = "ab", Email = "test@example.com", Password = "Password123" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Username);
    }

    [Fact]
    public void InvalidEmail_ShouldHaveError()
    {
        var model = new AvatarRegisterModel { Username = "validuser", Email = "not-email", Password = "Password123" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void WeakPassword_ShouldHaveErrors()
    {
        var model = new AvatarRegisterModel { Username = "validuser", Email = "test@example.com", Password = "weak" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void UsernameWithSpecialChars_ShouldHaveError()
    {
        var model = new AvatarRegisterModel { Username = "user@name!", Email = "test@example.com", Password = "Password123" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Username);
    }
}

public class AvatarLoginValidatorTests
{
    private readonly AvatarLoginValidator _validator = new();

    [Fact]
    public void ValidModel_ShouldNotHaveErrors()
    {
        var model = new AvatarLoginModel { Email = "test@example.com", Password = "pass" };
        var result = _validator.TestValidate(model);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyEmail_ShouldHaveError()
    {
        var model = new AvatarLoginModel { Email = "", Password = "pass" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void EmptyPassword_ShouldHaveError()
    {
        var model = new AvatarLoginModel { Email = "test@example.com", Password = "" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }
}

public class WalletCreateValidatorTests
{
    private readonly WalletCreateValidator _validator = new();

    [Fact]
    public void ValidModel_ShouldNotHaveErrors()
    {
        var model = new WalletCreateModel { ChainType = "Solana", Address = "abc123" };
        var result = _validator.TestValidate(model);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyAddress_ShouldHaveError()
    {
        var model = new WalletCreateModel { ChainType = "Solana", Address = "" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Address);
    }

    [Fact]
    public void EmptyChainType_ShouldHaveError()
    {
        var model = new WalletCreateModel { ChainType = "", Address = "abc" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.ChainType);
    }
}

public class HolonCreateValidatorTests
{
    private readonly HolonCreateValidator _validator = new();

    [Fact]
    public void ValidModel_ShouldNotHaveErrors()
    {
        var model = new HolonCreateModel { Name = "TestHolon", Description = "A test", ProviderName = "Test" };
        var result = _validator.TestValidate(model);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyName_ShouldHaveError()
    {
        var model = new HolonCreateModel { Name = "", Description = "A test", ProviderName = "Test" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void EmptyDescription_ShouldHaveError()
    {
        var model = new HolonCreateModel { Name = "Test", Description = "", ProviderName = "Test" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Description);
    }
}

public class NftMintValidatorTests
{
    private readonly NftMintValidator _validator = new();

    [Fact]
    public void ValidModel_ShouldNotHaveErrors()
    {
        var model = new NftMintRequest { Name = "MyNFT", Description = "A NFT", ChainId = "solana", WalletId = Guid.NewGuid() };
        var result = _validator.TestValidate(model);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void EmptyName_ShouldHaveError()
    {
        var model = new NftMintRequest { Name = "", Description = "A", ChainId = "sol", WalletId = Guid.NewGuid() };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void EmptyWalletId_ShouldHaveError()
    {
        var model = new NftMintRequest { Name = "NFT", Description = "D", ChainId = "sol", WalletId = Guid.Empty };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.WalletId);
    }
}

public class SearchRequestValidatorTests
{
    private readonly SearchRequestValidator _validator = new();

    [Fact]
    public void ValidModel_ShouldNotHaveErrors()
    {
        var model = new SearchRequest { Query = "test", Page = 1, PageSize = 20, SortBy = "CreatedDate" };
        var result = _validator.TestValidate(model);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void PageLessThanOne_ShouldHaveError()
    {
        var model = new SearchRequest { Query = "", Page = 0, PageSize = 20 };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.Page);
    }

    [Fact]
    public void PageSizeOutOfRange_ShouldHaveError()
    {
        var model = new SearchRequest { Query = "", Page = 1, PageSize = 200 };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void InvalidSortBy_ShouldHaveError()
    {
        var model = new SearchRequest { Query = "", Page = 1, PageSize = 20, SortBy = "InvalidField" };
        var result = _validator.TestValidate(model);
        result.ShouldHaveValidationErrorFor(x => x.SortBy);
    }
}

public class MappingProfileTests
{
    private readonly AutoMapper.IMapper _mapper;

    public MappingProfileTests()
    {
        var config = new AutoMapper.MapperConfiguration(cfg =>
        {
            cfg.AddProfile<Mapping.OASISMappingProfile>();
        });
        _mapper = config.CreateMapper();
    }

    [Fact]
    public void WalletCreateModel_To_Wallet_MapsCorrectly()
    {
        var model = new WalletCreateModel { ChainType = "Solana", Address = "addr1", Label = "Main" };
        var wallet = _mapper.Map<Wallet>(model);

        wallet.ChainType.Should().Be("Solana");
        wallet.Address.Should().Be("addr1");
        wallet.Label.Should().Be("Main");
    }

    [Fact]
    public void AvatarUpdateModel_To_Avatar_MapsOnlySetProperties()
    {
        var avatar = new Avatar { Username = "old", Email = "old@test.com" };
        var model = new AvatarUpdateModel { FirstName = "New" };
        _mapper.Map(model, avatar);

        avatar.FirstName.Should().Be("New");
        avatar.Username.Should().Be("old");
    }

    [Fact]
    public void HolonCreateModel_To_Holon_MapsCorrectly()
    {
        var model = new HolonCreateModel { Name = "TestHolon", Description = "Desc", ProviderName = "Test" };
        var holon = _mapper.Map<Holon>(model);

        holon.Name.Should().Be("TestHolon");
        holon.Description.Should().Be("Desc");
    }
}
