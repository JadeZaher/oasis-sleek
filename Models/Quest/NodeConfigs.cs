using OASIS.WebAPI.Models;
using OASIS.WebAPI.Models.Requests;

namespace OASIS.WebAPI.Models.Quest;

// ═══════════════════════════════════════════════════════════════════
// Per-node-type config DTOs for quest node dispatch deserialization.
// Moved verbatim from the former QuestManager.cs:~845-908 (made public so
// the scoped handlers in Services/Quest/Handlers/* can deserialize them).
// ═══════════════════════════════════════════════════════════════════

public class IdConfig
{
    public Guid Id { get; set; }
}

public class HolonUpdateNodeConfig
{
    public Guid HolonId { get; set; }
    public HolonUpdateModel Model { get; set; } = new();
}

public class HolonInteractNodeConfig
{
    public Guid HolonId { get; set; }
    public HolonInteractionRequest Request { get; set; } = new();
}

public class HolonPropagateNodeConfig
{
    public Guid HolonId { get; set; }
    public HolonPropagateRequest Request { get; set; } = new();
}

public class HolonCloneNodeConfig
{
    public Guid HolonId { get; set; }
    public HolonCloneRequest Request { get; set; } = new();
}

public class HolonMoveNodeConfig
{
    public Guid HolonId { get; set; }
    public Guid NewParentId { get; set; }
}

public class NftTransferNodeConfig
{
    public Guid NftId { get; set; }
    public NftTransferRequest Request { get; set; } = new();
}

public class NftBurnNodeConfig
{
    public Guid NftId { get; set; }
    public Guid WalletId { get; set; }
}

public class WalletUpdateNodeConfig
{
    public Guid WalletId { get; set; }
    public WalletUpdateModel Model { get; set; } = new();
}

public class WalletSetDefaultNodeConfig
{
    public Guid WalletId { get; set; }
}

public class StarGenerateNodeConfig
{
    public Guid StarId { get; set; }
    public STARDappGenerationRequest Request { get; set; } = new();
}
