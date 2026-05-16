using System.Security.Cryptography;

namespace OASIS.WebAPI.Core;

/// <summary>
/// Generates deterministic, traceable operation IDs for blockchain operations
/// that require client-side signing. These are NOT transaction hashes — they
/// are references the client can use to look up the operation later.
///
/// Format: op_{chain}_{operationType}_{first12OfHash}
/// </summary>
public static class OperationIdGenerator
{
    /// <summary>
    /// Generate a deterministic operation ID from operation metadata.
    /// </summary>
    public static string Generate(string chain, string operationType, string walletAddress)
    {
        var input = $"{chain.ToLowerInvariant()}|{operationType}|{walletAddress}|{DateTime.UtcNow.Ticks}";
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        var hashHex = Convert.ToHexString(hash)[..12].ToLowerInvariant();

        return $"op_{chain.ToLowerInvariant()}_{operationType.ToLowerInvariant()}_{hashHex}";
    }

    /// <summary>
    /// Generate an operation ID with additional parameter data for uniqueness.
    /// </summary>
    public static string Generate(string chain, string operationType, string walletAddress, params object[] parameters)
    {
        var paramStr = string.Join("|", parameters.Select(p => p?.ToString() ?? ""));
        var input = $"{chain}|{operationType}|{walletAddress}|{paramStr}|{DateTime.UtcNow.Ticks}";
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        var hashHex = Convert.ToHexString(hash)[..12].ToLowerInvariant();

        return $"op_{chain.ToLowerInvariant()}_{operationType.ToLowerInvariant()}_{hashHex}";
    }
}
