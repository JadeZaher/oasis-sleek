using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OASIS.WebAPI.Models;

/// <summary>
/// Append-only ledger of Wormhole VAAs that have already been consumed
/// (redeemed) on the target chain. Provides replay protection: a VAA's
/// <see cref="Digest"/> may be inserted at most once.
///
/// Replay-protection contract (for Wave 2 redeem flow):
/// BEFORE submitting a VAA to the target chain, attempt to INSERT a row keyed
/// by the VAA digest. The UNIQUE constraint on <see cref="Digest"/> (configured
/// in <c>OASISDbContext</c>) means a replayed VAA's insert fails with a
/// <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> — treat that
/// as "already consumed, reject the replay". Use the same
/// catch-DbUpdateException-then-reread pattern as the idempotency store.
/// </summary>
[Table("ConsumedVaas")]
public class ConsumedVaaRecord
{
    /// <summary>
    /// The unique VAA digest/hash (keccak256 of the VAA body, hex). Primary
    /// key + unique constraint — this is the replay-protection key.
    /// </summary>
    [Key]
    [MaxLength(128)]
    public string Digest { get; set; } = string.Empty;

    /// <summary>Wormhole emitter chain ID of the originating message.</summary>
    public int EmitterChainId { get; set; }

    /// <summary>Wormhole emitter address (hex, 32-byte Wormhole format).</summary>
    [MaxLength(128)]
    public string EmitterAddress { get; set; } = string.Empty;

    /// <summary>Wormhole sequence number of the originating message.</summary>
    public long Sequence { get; set; }

    /// <summary>
    /// The bridge transaction this VAA was consumed for (audit linkage).
    /// </summary>
    [MaxLength(64)]
    public string? BridgeTransactionId { get; set; }

    public DateTime ConsumedAt { get; set; } = DateTime.UtcNow;
}
