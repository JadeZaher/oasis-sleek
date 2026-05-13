# EF Storage Provider Setup Report

## Overview
This document summarizes the current EF Storage (PostgreSQL) provider setup in the OASIS project.

## Key Files
- **Interface**: `Interfaces/IOASISStorageProvider.cs` + `IOASISStorageProviderNFTExtensions.cs`
- **EF Implementation**: `Providers/EfStorageProvider.cs`
- **In-Memory Implementation**: `Providers/InMemoryStorageProvider.cs`
- **DbContext**: `Data/OASISDbContext.cs`
- **Registration**: `Program.cs` (lines 150-153)

## Current Configuration

### Database Connection String
```json
// appsettings.json
"ConnectionStrings": {
  "OASISDatabase": "Host=localhost;Port=5441;Database=oasis;Username=oasis;Password=oasis123"
}
```

### Provider Registration (Program.cs)
```csharp
// InMemoryStorageProvider (singleton) — fast in-process reads, shared across requests
var inMemoryProvider = new InMemoryStorageProvider();
builder.Services.AddSingleton<IOASISStorageProvider>(inMemoryProvider);

// EfStorageProvider (scoped) — standard lifetime matching OASISDbContext, handles PostgreSQL persistence
builder.Services.AddScoped<IOASISStorageProvider, EfStorageProvider>();
```

### DbContext Migration Files
- `Migrations/20260510171418_InitialCreate.cs` (and Designer)
- `Migrations/20260511144859_AddApiKeys.cs` (and Designer)
- `Migrations/20260513015302_AddWalletType.cs` (and Designer)

## Entity Sets (OASISDbContext)
The EF Core context manages these entities:

### Core Entities
- **Avatar** - User accounts with email/username uniqueness constraints
- **Wallet** - Blockchain wallets per avatar/address uniqueness
- **Holon** - Universal data nodes with tree structure (parent/child relationships)
- **BlockchainOperation** - On-chain operations history
- **STARODK** - dApp generator configurations
- **AvatarNFT** - On-chain NFT identity bindings
- **ApiKey** - API authentication keys for avatars

### Quest System Entities
- **Quest** / **QuestNode** / **QuestEdge** / **QuestDependency**
- **QuestTemplate** + related template nodes/edges

## Provider Pattern
The storage layer follows this architecture:

```
Controller → Manager → ProviderContext → IOASISStorageProvider (EF/InMemory)
```

### Decorator Pattern
All EF providers are wrapped with `HealthRecordingProviderDecorator` for monitoring.

```csharp
builder.Services.AddSingleton<IProviderHealthMonitor, ProviderHealthMonitor>();
// ... 
var decorated = providers.Select(p => new HealthRecordingProviderDecorator(p, healthMonitor));
```

### Multi-Provider Support
The system supports multiple storage providers:
1. **EfStorageProvider** (scoped) - PostgreSQL persistence
2. **InMemoryStorageProvider** (singleton) - In-process caching
3. Future: Redis, Azure CosmosDB, etc.

## Storage Provider Interface Methods
Both implementations must support all CRUD operations for each entity, plus:
- `OASISResult<T>` pattern for consistent error handling
- Optional query parameters for filtering (e.g., `HolonQueryRequest`)
- NFT extension methods via `IOASISStorageProviderNFTExtensions`

## Testing
- Unit tests in `OASIS.WebAPI.Tests/Providers/EfStorageProviderTests.cs`
- Integration tests use EF InMemory database
- Mock-based manager tests isolate storage from business logic

## Related Documentation
- `PROVIDERS.md` - Complete API surface and provider architecture
- `conductor/tracks/core-api/spec.md` - Storage provider interface definition
- `.pi/memory.md` - Implementation history and decisions
