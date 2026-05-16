using Algorand;
using Algorand.Algod;
using Algorand.Algod.Model;
using Algorand.Algod.Model.Transactions;
using OASIS.WebAPI.Providers.Blockchain.Base;

namespace OASIS.WebAPI.Core;

/// <summary>
/// Dispenses test ALGO via the Algorand2 SDK (build → sign → submit).
/// The faucet account is loaded from a 25-word mnemonic in configuration
/// (Blockchain:Faucet:Algorand:Mnemonic). Used for dev / test networks only.
/// </summary>
public class AlgorandFaucet : IAlgorandFaucet
{
    private readonly IConfiguration _config;
    private readonly ILogger<AlgorandFaucet> _logger;
    private readonly BlockchainConfigurationManager _configManager;

    private const string FaucetChain = "Algorand";

    public AlgorandFaucet(IConfiguration config, ILogger<AlgorandFaucet> logger)
    {
        _config = config;
        _logger = logger;
        _configManager = new BlockchainConfigurationManager(config);
    }

    private string? Mnemonic => _config.GetValue<string>("Blockchain:Faucet:Algorand:Mnemonic");

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Mnemonic);

    public async Task<string> DispenseAsync(string toAddress, decimal amountAlgo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toAddress))
            throw new ArgumentException("Recipient address is required.", nameof(toAddress));
        if (amountAlgo <= 0)
            throw new ArgumentException("Amount must be positive.", nameof(amountAlgo));

        var mnemonic = Mnemonic;
        if (string.IsNullOrWhiteSpace(mnemonic))
            throw new InvalidOperationException(
                "Algorand faucet is not configured (set Blockchain:Faucet:Algorand:Mnemonic).");

        var network = _configManager.GetDefaultNetwork(FaucetChain);
        var networkConfig = _configManager.GetNetworkConfig(FaucetChain, network);

        var faucetAccount = new Account(mnemonic.Trim());

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(networkConfig.NodeUrl),
            Timeout = TimeSpan.FromMilliseconds(networkConfig.TimeoutMs ?? 30000)
        };
        if (!string.IsNullOrWhiteSpace(networkConfig.ApiToken))
            httpClient.DefaultRequestHeaders.Add("X-Algo-API-Token", networkConfig.ApiToken);

        var algod = new DefaultApi(httpClient);

        var txParams = await algod.TransactionParamsAsync(ct);

        var microAlgos = Algorand.Utils.Utils.AlgosToMicroalgos((double)amountAlgo);

        var payment = PaymentTransaction.GetPaymentTransactionFromNetworkTransactionParameters(
            faucetAccount.Address,
            new Address(toAddress),
            microAlgos,
            "OASIS faucet top-up",
            txParams);

        var signedTx = payment.Sign(faucetAccount);
        var response = await Algorand.Utils.Utils.SubmitTransaction(algod, signedTx);

        _logger.LogInformation(
            "Algorand faucet dispensed {Amount} ALGO to {To} on {Network} (tx {TxId})",
            amountAlgo, toAddress, network, response.Txid);

        return response.Txid;
    }
}
