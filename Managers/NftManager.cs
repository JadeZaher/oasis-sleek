using OASIS.WebAPI.Core;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Managers;

public class NftManager : INftManager
{
    private readonly IHolonStore _holonStore;
    private readonly IBlockchainOperationStore _blockchainOperationStore;

    public NftManager(IHolonStore holonStore, IBlockchainOperationStore blockchainOperationStore)
    {
        _holonStore = holonStore;
        _blockchainOperationStore = blockchainOperationStore;
    }

    public async Task<OASISResult<INft>> GetAsync(Guid id, OASISRequest? request = null)
    {
        var result = await _holonStore.GetByIdAsync(id, default);
        if (result.IsError || result.Result == null) return new OASISResult<INft> { IsError = true, Message = result.Message };

        if (!string.Equals(result.Result.AssetType, "NFT", StringComparison.OrdinalIgnoreCase))
            return new OASISResult<INft> { IsError = true, Message = "Holon is not an NFT." };

        return new OASISResult<INft> { Result = (INft)result.Result, Message = "Success" };
    }

    public async Task<OASISResult<IEnumerable<INft>>> QueryAsync(NftQueryRequest query, OASISRequest? request = null)
    {
        var all = await _holonStore.QueryAsync(null, default);
        if (all.IsError || all.Result == null) return new OASISResult<IEnumerable<INft>> { IsError = true, Message = all.Message };

        var filtered = all.Result
            .Where(h => string.Equals(h.AssetType, "NFT", StringComparison.OrdinalIgnoreCase));

        if (query.OwnerAvatarId.HasValue)
            filtered = filtered.Where(h => h.AvatarId == query.OwnerAvatarId.Value);
        if (!string.IsNullOrEmpty(query.ChainId))
            filtered = filtered.Where(h => h.ChainId?.Equals(query.ChainId, StringComparison.OrdinalIgnoreCase) == true);
        if (!string.IsNullOrEmpty(query.TokenId))
            filtered = filtered.Where(h => h.TokenId?.Equals(query.TokenId, StringComparison.OrdinalIgnoreCase) == true);
        if (!string.IsNullOrEmpty(query.Name))
            filtered = filtered.Where(h => h.Name.Contains(query.Name, StringComparison.OrdinalIgnoreCase));

        return new OASISResult<IEnumerable<INft>> { Result = filtered.Cast<INft>().ToList(), Message = "Success" };
    }

    public async Task<OASISResult<IBlockchainOperation>> MintAsync(NftMintRequest request, Guid avatarId, OASISRequest? providerRequest = null)
    {
        // Build the Holon with AssetType = "NFT"
        var metadata = new Dictionary<string, string>(request.Metadata);
        if (!string.IsNullOrEmpty(request.ImageUri)) metadata["image"] = request.ImageUri;
        if (!string.IsNullOrEmpty(request.ExternalUri)) metadata["external_url"] = request.ExternalUri;

        var holon = new Holon
        {
            AvatarId = avatarId,
            Name = request.Name,
            Description = request.Description,
            AssetType = "NFT",
            ChainId = request.ChainId,
            TokenId = request.TokenId,
            Metadata = metadata,
            ProviderName = "PostgreSQL",
            IsActive = true
        };

        var saveResult = await _holonStore.UpsertAsync(holon, default);
        if (saveResult.IsError || saveResult.Result == null)
            return new OASISResult<IBlockchainOperation> { IsError = true, Message = saveResult.Message };

        // Build blockchain operation for mint
        var operation = new BlockchainOperation
        {
            AvatarId = avatarId,
            WalletId = request.WalletId,
            OperationType = "Mint",
            Status = OperationStatus.Pending,
            Parameters = new Dictionary<string, string>
            {
                ["holonId"] = holon.Id.ToString(),
                ["name"] = request.Name,
                ["chainId"] = request.ChainId
            }
        };

        return await _blockchainOperationStore.UpsertAsync(operation, default);
    }

    public async Task<OASISResult<IBlockchainOperation>> TransferAsync(Guid nftId, NftTransferRequest request, Guid avatarId, OASISRequest? providerRequest = null)
    {
        // Load and verify ownership
        var holonResult = await _holonStore.GetByIdAsync(nftId, default);
        if (holonResult.IsError || holonResult.Result == null)
            return new OASISResult<IBlockchainOperation> { IsError = true, Message = "NFT not found." };

        var holon = holonResult.Result;
        if (!string.Equals(holon.AssetType, "NFT", StringComparison.OrdinalIgnoreCase))
            return new OASISResult<IBlockchainOperation> { IsError = true, Message = "Holon is not an NFT." };

        if (holon.AvatarId != avatarId)
            return new OASISResult<IBlockchainOperation> { IsError = true, Message = "You do not own this NFT." };

        // Transfer ownership
        holon.AvatarId = request.TargetAvatarId;
        holon.ModifiedDate = DateTime.UtcNow;

        var saveResult = await _holonStore.UpsertAsync(holon, default);
        if (saveResult.IsError)
            return new OASISResult<IBlockchainOperation> { IsError = true, Message = saveResult.Message };

        // Build blockchain operation for transfer
        var operation = new BlockchainOperation
        {
            AvatarId = avatarId,
            WalletId = request.WalletId,
            OperationType = "Transfer",
            Status = OperationStatus.Pending,
            Parameters = new Dictionary<string, string>
            {
                ["holonId"] = nftId.ToString(),
                ["fromAvatarId"] = avatarId.ToString(),
                ["toAvatarId"] = request.TargetAvatarId.ToString(),
                ["memo"] = request.Memo ?? string.Empty
            }
        };

        return await _blockchainOperationStore.UpsertAsync(operation, default);
    }

    public async Task<OASISResult<IBlockchainOperation>> BurnAsync(Guid nftId, Guid walletId, Guid avatarId, OASISRequest? providerRequest = null)
    {
        // Load and verify ownership
        var holonResult = await _holonStore.GetByIdAsync(nftId, default);
        if (holonResult.IsError || holonResult.Result == null)
            return new OASISResult<IBlockchainOperation> { IsError = true, Message = "NFT not found." };

        var holon = holonResult.Result;
        if (!string.Equals(holon.AssetType, "NFT", StringComparison.OrdinalIgnoreCase))
            return new OASISResult<IBlockchainOperation> { IsError = true, Message = "Holon is not an NFT." };

        if (holon.AvatarId != avatarId)
            return new OASISResult<IBlockchainOperation> { IsError = true, Message = "You do not own this NFT." };

        // Burn: deactivate the holon
        holon.IsActive = false;
        holon.ModifiedDate = DateTime.UtcNow;

        var saveResult = await _holonStore.UpsertAsync(holon, default);
        if (saveResult.IsError)
            return new OASISResult<IBlockchainOperation> { IsError = true, Message = saveResult.Message };

        // Build blockchain operation for burn
        var operation = new BlockchainOperation
        {
            AvatarId = avatarId,
            WalletId = walletId,
            OperationType = "Burn",
            Status = OperationStatus.Pending,
            Parameters = new Dictionary<string, string>
            {
                ["holonId"] = nftId.ToString()
            }
        };

        return await _blockchainOperationStore.UpsertAsync(operation, default);
    }

    public async Task<OASISResult<NftMetadata>> GetMetadataAsync(Guid id, OASISRequest? request = null)
    {
        var result = await _holonStore.GetByIdAsync(id, default);
        if (result.IsError || result.Result == null)
            return new OASISResult<NftMetadata> { IsError = true, Message = "NFT not found." };

        var holon = result.Result;
        if (!string.Equals(holon.AssetType, "NFT", StringComparison.OrdinalIgnoreCase))
            return new OASISResult<NftMetadata> { IsError = true, Message = "Holon is not an NFT." };

        var metadata = new NftMetadata
        {
            Name = holon.Name,
            Description = holon.Description
        };

        if (holon.Metadata != null)
        {
            if (holon.Metadata.TryGetValue("image", out var image))
                metadata.Image = image;
            if (holon.Metadata.TryGetValue("external_url", out var externalUrl))
                metadata.ExternalUrl = externalUrl;
            if (holon.Metadata.TryGetValue("animation_url", out var animUrl))
                metadata.AnimationUrl = animUrl;

            // Parse attributes from metadata
            if (holon.Metadata.TryGetValue("attributes", out var attrsJson))
            {
                try
                {
                    var attrs = System.Text.Json.JsonSerializer.Deserialize<List<NftAttribute>>(attrsJson);
                    if (attrs != null) metadata.Attributes = attrs;
                }
                catch
                {
                    // If parsing fails, skip attributes
                }
            }
        }

        return new OASISResult<NftMetadata> { Result = metadata, Message = "Success" };
    }
}
