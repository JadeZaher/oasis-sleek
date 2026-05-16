namespace OASIS.WebAPI.Core;

/// <summary>
/// Dispenses test ALGO from a configured faucet account to a recipient address.
/// Isolates Algorand2 SDK transaction building / signing / submission so the
/// wallet manager stays SDK-agnostic and unit-testable.
/// Only used on dev / test networks — callers must enforce the mainnet guard.
/// </summary>
public interface IAlgorandFaucet
{
    /// <summary>
    /// True when a faucet mnemonic is configured (Blockchain:Faucet:Algorand:Mnemonic).
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Send <paramref name="amountAlgo"/> ALGO from the configured faucet account
    /// to <paramref name="toAddress"/> on the active Algorand network.
    /// Returns the submitted transaction id.
    /// </summary>
    Task<string> DispenseAsync(string toAddress, decimal amountAlgo, CancellationToken ct = default);
}
