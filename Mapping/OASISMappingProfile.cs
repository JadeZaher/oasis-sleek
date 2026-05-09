using AutoMapper;
using OASIS.WebAPI.Core;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Mapping;

public class OASISMappingProfile : Profile
{
    public OASISMappingProfile()
    {
        // Avatar mappings
        CreateMap<AvatarRegisterModel, Avatar>();
        CreateMap<AvatarUpdateModel, Avatar>()
            .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));

        // Holon mappings
        CreateMap<HolonCreateModel, Holon>();
        CreateMap<HolonUpdateModel, Holon>()
            .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));

        // Wallet mappings
        CreateMap<WalletCreateModel, Wallet>();
        CreateMap<WalletUpdateModel, Wallet>()
            .ForAllMembers(opts => opts.Condition((src, dest, srcMember) => srcMember != null));

        // NFT / Search hit mappings
        CreateMap<IHolon, NftResult>()
            .ForMember(dest => dest.OwnerAvatarId, opt => opt.MapFrom(src => src.AvatarId));

        CreateMap<IHolon, SearchHit>()
            .ForMember(dest => dest.EntityType, opt => opt.MapFrom(_ => SearchableEntityType.Holon))
            .ForMember(dest => dest.Title, opt => opt.MapFrom(src => src.Name));

        CreateMap<IAvatar, SearchHit>()
            .ForMember(dest => dest.EntityType, opt => opt.MapFrom(_ => SearchableEntityType.Avatar))
            .ForMember(dest => dest.Title, opt => opt.MapFrom(src => src.Username));

        CreateMap<IWallet, SearchHit>()
            .ForMember(dest => dest.EntityType, opt => opt.MapFrom(_ => SearchableEntityType.Wallet))
            .ForMember(dest => dest.Title, opt => opt.MapFrom(src => src.Address));
    }
}
