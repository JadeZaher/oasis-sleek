using System.Text.Json;
using OASIS.WebAPI.Core;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Interfaces.Stores;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;

namespace OASIS.WebAPI.Managers;

public class STARManager : ISTARManager
{
    private readonly ISTARStore _starStore;

    public STARManager(ISTARStore starStore)
    {
        _starStore = starStore;
    }

    public async Task<OASISResult<ISTARODK>> GetAsync(Guid id, OASISRequest? request = null)
    {
        return await _starStore.GetByIdAsync(id, default);
    }

    public async Task<OASISResult<IEnumerable<ISTARODK>>> GetAllAsync(OASISRequest? request = null)
    {
        return await _starStore.GetAllAsync(default);
    }

    public async Task<OASISResult<ISTARODK>> CreateOrUpdateAsync(STARODKCreateModel model, OASISRequest? request = null)
    {
        var existing = await _starStore.GetAllAsync(default);
        var match = existing.Result?.FirstOrDefault(s => s.Name.Equals(model.Name, StringComparison.OrdinalIgnoreCase));

        var odk = match as STARODK ?? new STARODK();
        odk.Name = model.Name;
        odk.Description = model.Description;
        odk.PublicKey = model.PublicKey;
        odk.AvatarId = model.AvatarId;
        odk.ModifiedDate = DateTime.UtcNow;

        return await _starStore.UpsertAsync(odk, default);
    }

    public async Task<OASISResult<bool>> DeleteAsync(Guid id, OASISRequest? request = null)
    {
        return await _starStore.DeleteAsync(id, default);
    }

    public async Task<OASISResult<ISTARODK>> GenerateAsync(Guid id, STARDappGenerationRequest request, OASISRequest? providerRequest = null)
    {
        var existing = await _starStore.GetByIdAsync(id, default);
        if (existing.IsError || existing.Result == null) return existing;

        var odk = (STARODK)existing.Result;
        odk.TargetChain = request.TargetChain;
        odk.BoundHolonIds = request.BoundHolonIds;
        odk.GeneratedCode = GenerateDappCode(odk, request);
        odk.ModifiedDate = DateTime.UtcNow;

        return await _starStore.UpsertAsync(odk, default);
    }

    public async Task<OASISResult<ISTARODK>> DeployAsync(Guid id, OASISRequest? providerRequest = null)
    {
        var existing = await _starStore.GetByIdAsync(id, default);
        if (existing.IsError || existing.Result == null) return existing;

        var odk = (STARODK)existing.Result;
        if (string.IsNullOrEmpty(odk.GeneratedCode))
            return new OASISResult<ISTARODK> { IsError = true, Message = "Dapp must be generated before deployment." };

        odk.DeploymentConfig = JsonSerializer.Serialize(new
        {
            DeployedAt = DateTime.UtcNow,
            Chain = odk.TargetChain,
            Holons = odk.BoundHolonIds,
            TxHash = $"0x{Guid.NewGuid():N}"
        });
        odk.ModifiedDate = DateTime.UtcNow;

        return await _starStore.UpsertAsync(odk, default);
    }

    private static string GenerateDappCode(ISTARODK odk, STARDappGenerationRequest request)
    {
        var config = new
        {
            Name = odk.Name,
            Description = odk.Description,
            TargetChain = request.TargetChain,
            BoundHolons = request.BoundHolonIds,
            UserConfig = request.Config,
            GeneratedAt = DateTime.UtcNow
        };
        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    }
}
