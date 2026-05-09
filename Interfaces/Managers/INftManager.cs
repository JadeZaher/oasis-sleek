using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Interfaces.Managers;

public interface INftManager
{
    Task<OASISResult<INft>> GetAsync(Guid id, OASISRequest? request = null);
    Task<OASISResult<IEnumerable<INft>>> QueryAsync(NftQueryRequest query, OASISRequest? request = null);
    Task<OASISResult<IBlockchainOperation>> MintAsync(NftMintRequest request, Guid avatarId, OASISRequest? providerRequest = null);
    Task<OASISResult<IBlockchainOperation>> TransferAsync(Guid nftId, NftTransferRequest request, Guid avatarId, OASISRequest? providerRequest = null);
    Task<OASISResult<IBlockchainOperation>> BurnAsync(Guid nftId, Guid walletId, Guid avatarId, OASISRequest? providerRequest = null);
    Task<OASISResult<NftMetadata>> GetMetadataAsync(Guid id, OASISRequest? request = null);
}
