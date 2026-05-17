using OASIS.WebAPI.Core;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Interfaces.Managers;
using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Requests;
using OASIS.WebAPI.Models.Responses;
using OASIS.WebAPI.Providers.Blockchain.Base;

namespace OASIS.WebAPI.Managers;

public class WalletManager : IWalletManager
{
    private readonly ProviderContext _providerContext;
    private readonly IBlockchainProviderFactory _chainFactory;
    private readonly WalletKeyService _keyService;
    private readonly IConfiguration _config;
    private readonly IAlgorandFaucet _algorandFaucet;
    private readonly BlockchainConfigurationManager _blockchainConfig;

    public WalletManager(
        ProviderContext providerContext,
        IBlockchainProviderFactory chainFactory,
        WalletKeyService keyService,
        IConfiguration config,
        IAlgorandFaucet algorandFaucet)
    {
        _providerContext = providerContext;
        _chainFactory = chainFactory;
        _keyService = keyService;
        _config = config;
        _algorandFaucet = algorandFaucet;
        _blockchainConfig = new BlockchainConfigurationManager(config);
    }

    public async Task<OASISResult<IWallet>> GetAsync(Guid id, OASISRequest? request = null)
    {
        var activation = _providerContext.Activate(request);
        if (activation.IsError) return new OASISResult<IWallet> { IsError = true, Message = activation.Message };

        return await _providerContext.CurrentProvider.LoadWalletAsync(id);
    }

    public async Task<OASISResult<IEnumerable<IWallet>>> QueryAsync(WalletQueryRequest query, OASISRequest? request = null)
    {
        var activation = _providerContext.Activate(request);
        if (activation.IsError) return new OASISResult<IEnumerable<IWallet>> { IsError = true, Message = activation.Message };

        var all = await _providerContext.CurrentProvider.LoadAllWalletsAsync();
        if (all.IsError || all.Result == null) return all;

        var filtered = all.Result.AsEnumerable();

        if (query.AvatarId.HasValue)
            filtered = filtered.Where(w => w.AvatarId == query.AvatarId.Value);
        if (!string.IsNullOrEmpty(query.ChainType))
            filtered = filtered.Where(w => w.ChainType.Equals(query.ChainType, StringComparison.OrdinalIgnoreCase));
        if (query.IsDefault.HasValue)
            filtered = filtered.Where(w => w.IsDefault == query.IsDefault.Value);

        return new OASISResult<IEnumerable<IWallet>> { Result = filtered.ToList(), Message = "Success" };
    }

    public async Task<OASISResult<IWallet>> CreateAsync(WalletCreateModel model, Guid avatarId, OASISRequest? request = null)
    {
        var activation = _providerContext.Activate(request);
        if (activation.IsError) return new OASISResult<IWallet> { IsError = true, Message = activation.Message };

        // Address uniqueness per chain
        var all = await _providerContext.CurrentProvider.LoadAllWalletsAsync();
        var existing = all.Result?.FirstOrDefault(w =>
            w.Address.Equals(model.Address, StringComparison.OrdinalIgnoreCase) &&
            w.ChainType.Equals(model.ChainType, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
            return new OASISResult<IWallet> { IsError = true, Message = "Wallet address already exists for this chain." };

        var wallet = new Wallet
        {
            AvatarId = avatarId,
            ChainType = model.ChainType,
            Address = model.Address,
            PublicKey = model.PublicKey,
            Label = model.Label,
            IsDefault = model.IsDefault,
            WalletType = model.WalletType
        };

        if (model.IsDefault)
        {
            await UnsetPreviousDefaultAsync(avatarId, model.ChainType, wallet.Id);
        }

        return await _providerContext.CurrentProvider.SaveWalletAsync(wallet);
    }

    public async Task<OASISResult<IWallet>> UpdateAsync(Guid id, WalletUpdateModel model, OASISRequest? request = null)
    {
        var activation = _providerContext.Activate(request);
        if (activation.IsError) return new OASISResult<IWallet> { IsError = true, Message = activation.Message };

        var existing = await _providerContext.CurrentProvider.LoadWalletAsync(id);
        if (existing.IsError || existing.Result == null) return existing;

        var wallet = (Wallet)existing.Result;
        if (model.Label != null) wallet.Label = model.Label;

        if (model.IsDefault.HasValue && model.IsDefault.Value && !wallet.IsDefault)
        {
            await UnsetPreviousDefaultAsync(wallet.AvatarId, wallet.ChainType, wallet.Id);
            wallet.IsDefault = true;
        }
        else if (model.IsDefault.HasValue)
        {
            wallet.IsDefault = model.IsDefault.Value;
        }

        return await _providerContext.CurrentProvider.SaveWalletAsync(wallet);
    }

    public async Task<OASISResult<bool>> DeleteAsync(Guid id, OASISRequest? request = null)
    {
        var activation = _providerContext.Activate(request);
        if (activation.IsError) return new OASISResult<bool> { IsError = true, Message = activation.Message };

        return await _providerContext.CurrentProvider.DeleteWalletAsync(id);
    }

    public async Task<OASISResult<bool>> SetDefaultAsync(Guid avatarId, Guid walletId, OASISRequest? request = null)
    {
        var activation = _providerContext.Activate(request);
        if (activation.IsError) return new OASISResult<bool> { IsError = true, Message = activation.Message };

        var walletResult = await _providerContext.CurrentProvider.LoadWalletAsync(walletId);
        if (walletResult.IsError || walletResult.Result == null)
            return new OASISResult<bool> { IsError = true, Message = "Wallet not found." };

        var wallet = walletResult.Result;
        if (wallet.AvatarId != avatarId)
            return new OASISResult<bool> { IsError = true, Message = "Wallet not owned by avatar." };

        await UnsetPreviousDefaultAsync(avatarId, wallet.ChainType, walletId);

        wallet.IsDefault = true;
        var saveResult = await _providerContext.CurrentProvider.SaveWalletAsync(wallet);
        if (saveResult.IsError)
            return new OASISResult<bool> { IsError = true, Message = saveResult.Message };

        return new OASISResult<bool> { Result = true, Message = "Default wallet set." };
    }

    public async Task<OASISResult<PortfolioResult>> GetPortfolioAsync(Guid walletId, OASISRequest? request = null)
    {
        var activation = _providerContext.Activate(request);
        if (activation.IsError) return new OASISResult<PortfolioResult> { IsError = true, Message = activation.Message };

        var walletResult = await _providerContext.CurrentProvider.LoadWalletAsync(walletId);
        if (walletResult.IsError || walletResult.Result == null)
            return new OASISResult<PortfolioResult> { IsError = true, Message = "Wallet not found." };

        var wallet = walletResult.Result;

        // Stub: linked NFT Holons for this avatar
        var allHolons = await _providerContext.CurrentProvider.LoadAllHolonsAsync();
        var nfts = allHolons.Result?
            .Where(h => h.AvatarId == wallet.AvatarId && h.AssetType == "NFT")
            .Select(h => new NftHolding
            {
                HolonId = h.Id,
                Name = h.Name,
                TokenId = h.TokenId,
                ImageUri = h.Metadata.TryGetValue("image", out var img) ? img : null
            })
            .ToList() ?? new List<NftHolding>();

        var symbol = wallet.ChainType.ToUpperInvariant() switch
        {
            "ALGORAND" or "ALGO" => "ALGO",
            "SOLANA" or "SOL" => "SOL",
            "ETHEREUM" or "ETH" => "ETH",
            _ => wallet.ChainType.ToUpperInvariant()
        };

        decimal balance = 0;
        try
        {
            var chainProvider = _chainFactory.GetProvider(wallet.ChainType, ChainNetwork.Devnet);
            if (chainProvider != null)
            {
                var balanceResult = await chainProvider.GetBalanceAsync(wallet.Address);
                if (!balanceResult.IsError && balanceResult.Result != null)
                    decimal.TryParse(balanceResult.Result, out balance);
            }
        }
        catch { /* Fall back to 0 if blockchain unavailable */ }

        var portfolio = new PortfolioResult
        {
            WalletId = wallet.Id,
            ChainType = wallet.ChainType,
            Address = wallet.Address,
            Balance = balance,
            Symbol = symbol,
            Nfts = nfts,
            ComputedAt = DateTime.UtcNow
        };

        return new OASISResult<PortfolioResult> { Result = portfolio, Message = "Portfolio computed." };
    }

    // ─── New: Generate a wallet on-platform ───

    public async Task<OASISResult<IWallet>> GenerateWalletAsync(WalletGenerateRequest model, Guid avatarId, OASISRequest? request = null)
    {
        var activation = _providerContext.Activate(request);
        if (activation.IsError) return new OASISResult<IWallet> { IsError = true, Message = activation.Message };

        try
        {
            var (publicKey, privateKeyHex, address, seedPhrase) = _keyService.GenerateKeypair(model.ChainType);

            // Check uniqueness
            var all = await _providerContext.CurrentProvider.LoadAllWalletsAsync();
            var existing = all.Result?.FirstOrDefault(w =>
                w.Address.Equals(address, StringComparison.OrdinalIgnoreCase) &&
                w.ChainType.Equals(model.ChainType, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                return new OASISResult<IWallet> { IsError = true, Message = "Generated address collision — please retry." };

            var wallet = new Wallet
            {
                AvatarId = avatarId,
                ChainType = model.ChainType,
                Address = address,
                PublicKey = publicKey,
                Label = model.Label,
                IsDefault = model.IsDefault,
                WalletType = WalletType.Platform,
                EncryptedPrivateKey = _keyService.EncryptPrivateKey(privateKeyHex),
                EncryptedSeedPhrase = seedPhrase != null ? _keyService.EncryptSeedPhrase(seedPhrase) : null
            };

            if (model.IsDefault)
                await UnsetPreviousDefaultAsync(avatarId, model.ChainType, wallet.Id);

            return await _providerContext.CurrentProvider.SaveWalletAsync(wallet);
        }
        catch (NotSupportedException ex)
        {
            return new OASISResult<IWallet> { IsError = true, Message = ex.Message };
        }
    }

    // ─── New: Connect an external wallet (MetaMask, Ghost, etc.) ───

    public async Task<OASISResult<IWallet>> ConnectWalletAsync(WalletConnectRequest model, Guid avatarId, OASISRequest? request = null)
    {
        var activation = _providerContext.Activate(request);
        if (activation.IsError) return new OASISResult<IWallet> { IsError = true, Message = activation.Message };

        if (string.IsNullOrWhiteSpace(model.Address))
            return new OASISResult<IWallet> { IsError = true, Message = "Address is required." };

        // Optional: Verify ownership via signed message
        if (!string.IsNullOrEmpty(model.SignedMessage) && !string.IsNullOrEmpty(model.OriginalMessage))
        {
            // In production, verify the signature using chain-specific recovery
            // For now, trust the address if they provide it (lightweight verification)
        }

        // Check uniqueness
        var all = await _providerContext.CurrentProvider.LoadAllWalletsAsync();
        var existing = all.Result?.FirstOrDefault(w =>
            w.Address.Equals(model.Address, StringComparison.OrdinalIgnoreCase) &&
            w.ChainType.Equals(model.ChainType, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            // If the wallet belongs to this avatar, return it
            if (existing.AvatarId == avatarId)
                return new OASISResult<IWallet> { Result = existing, Message = "Wallet already connected." };

            return new OASISResult<IWallet> { IsError = true, Message = "Address already registered by another avatar." };
        }

        var wallet = new Wallet
        {
            AvatarId = avatarId,
            ChainType = model.ChainType,
            Address = model.Address,
            PublicKey = model.PublicKey,
            Label = model.Label,
            IsDefault = model.IsDefault,
            WalletType = WalletType.External
        };

        if (model.IsDefault)
            await UnsetPreviousDefaultAsync(avatarId, model.ChainType, wallet.Id);

        return await _providerContext.CurrentProvider.SaveWalletAsync(wallet);
    }

    // ─── New: Export wallet private key ───

    public async Task<OASISResult<WalletExportResult>> ExportWalletAsync(Guid walletId, Guid avatarId, OASISRequest? request = null)
    {
        var activation = _providerContext.Activate(request);
        if (activation.IsError) return new OASISResult<WalletExportResult> { IsError = true, Message = activation.Message };

        var walletResult = await _providerContext.CurrentProvider.LoadWalletAsync(walletId);
        if (walletResult.IsError || walletResult.Result == null)
            return new OASISResult<WalletExportResult> { IsError = true, Message = "Wallet not found." };

        var wallet = walletResult.Result;

        if (wallet.AvatarId != avatarId)
            return new OASISResult<WalletExportResult> { IsError = true, Message = "Wallet not owned by this avatar." };

        if (wallet.WalletType != WalletType.Platform)
            return new OASISResult<WalletExportResult> { IsError = true, Message = "Only platform-generated wallets can be exported. External wallets are managed by their respective browser wallet." };

        if (string.IsNullOrEmpty(wallet.EncryptedPrivateKey))
            return new OASISResult<WalletExportResult> { IsError = true, Message = "No private key stored for this wallet." };

        try
        {
            var privateKey = _keyService.DecryptPrivateKey(wallet.EncryptedPrivateKey);
            var seedPhrase = wallet.EncryptedSeedPhrase != null
                ? _keyService.DecryptSeedPhrase(wallet.EncryptedSeedPhrase)
                : null;

            return new OASISResult<WalletExportResult>
            {
                Result = new WalletExportResult
                {
                    WalletId = wallet.Id,
                    ChainType = wallet.ChainType,
                    Address = wallet.Address,
                    PublicKey = wallet.PublicKey,
                    PrivateKey = privateKey,
                    SeedPhrase = seedPhrase
                },
                Message = "Export successful. Handle with extreme care."
            };
        }
        catch (Exception ex)
        {
            return new OASISResult<WalletExportResult> { IsError = true, Message = $"Decryption failed: {ex.Message}" };
        }
    }

    // ─── New: Top-up a wallet via faucet (dev / test networks only) ───

    public async Task<OASISResult<object>> TopUpAsync(Guid walletId, decimal? amount, Guid avatarId, OASISRequest? request = null, string? clientIdempotencyKey = null)
    {
        var activation = _providerContext.Activate(request);
        if (activation.IsError) return new OASISResult<object> { IsError = true, Message = activation.Message };

        var walletResult = await _providerContext.CurrentProvider.LoadWalletAsync(walletId);
        if (walletResult.IsError || walletResult.Result == null)
            return new OASISResult<object> { IsError = true, Message = "Wallet not found." };

        var wallet = walletResult.Result;

        // Ownership check — mirror ExportWalletAsync.
        if (wallet.AvatarId != avatarId)
            return new OASISResult<object> { IsError = true, Message = "Wallet not owned by this avatar." };

        // HARD GUARD: never dispense on mainnet.
        var network = _blockchainConfig.GetDefaultNetwork(wallet.ChainType);
        if (network == ChainNetwork.Mainnet)
            return new OASISResult<object> { IsError = true, Message = "Top-up (faucet) is disabled on mainnet." };

        var defaultAmount = _config.GetValue<decimal?>("Blockchain:Faucet:DefaultAmount") ?? 5m;
        var dispenseAmount = amount.GetValueOrDefault(defaultAmount);
        if (dispenseAmount <= 0)
            return new OASISResult<object> { IsError = true, Message = "Amount must be a positive value." };

        switch (wallet.ChainType.ToLowerInvariant())
        {
            case "algorand":
            case "algo":
                if (!_algorandFaucet.IsConfigured)
                    return new OASISResult<object>
                    {
                        IsError = true,
                        Message = "Algorand faucet is not configured (set Blockchain:Faucet:Algorand:Mnemonic)."
                    };

                try
                {
                    // Client-supplied Idempotency-Key (if any) wins; otherwise the
                    // faucet derives a deterministic content key from
                    // (chain, recipient, amount) — absence is still dedup-safe,
                    // no random per-request key is ever generated.
                    var txHash = string.IsNullOrWhiteSpace(clientIdempotencyKey)
                        ? await _algorandFaucet.DispenseAsync(wallet.Address, dispenseAmount, ct: default)
                        : await _algorandFaucet.DispenseAsync(wallet.Address, dispenseAmount, clientIdempotencyKey, ct: default);
                    return new OASISResult<object>
                    {
                        Result = new
                        {
                            txHash,
                            amount = dispenseAmount,
                            chain = wallet.ChainType,
                            network = network.ToString()
                        },
                        Message = $"Dispensed {dispenseAmount} test ALGO to {wallet.Address} on {network}."
                    };
                }
                catch (Exception ex)
                {
                    return new OASISResult<object> { IsError = true, Message = $"Algorand faucet failed: {ex.Message}", Exception = ex };
                }

            case "solana":
            case "sol":
                // Solana devnet/testnet top-up is performed client-side via RPC airdrop
                // (the frontend handles Solana). Keep the method shape consistent.
                return new OASISResult<object>
                {
                    IsError = false,
                    Result = new
                    {
                        txHash = (string?)null,
                        amount = dispenseAmount,
                        chain = wallet.ChainType,
                        network = network.ToString()
                    },
                    Message = "Solana devnet/testnet top-up is performed client-side via RPC airdrop (requestAirdrop)."
                };

            default:
                return new OASISResult<object>
                {
                    IsError = true,
                    Message = $"Top-up not supported for chain {wallet.ChainType}."
                };
        }
    }

    private async Task UnsetPreviousDefaultAsync(Guid avatarId, string chainType, Guid exceptWalletId)
    {
        var all = await _providerContext.CurrentProvider.LoadAllWalletsAsync();
        var previous = all.Result?.FirstOrDefault(w =>
            w.AvatarId == avatarId &&
            w.ChainType.Equals(chainType, StringComparison.OrdinalIgnoreCase) &&
            w.IsDefault &&
            w.Id != exceptWalletId);

        if (previous != null)
        {
            previous.IsDefault = false;
            await _providerContext.CurrentProvider.SaveWalletAsync(previous);
        }
    }
}
