# Deploy-Steps & Stub-Remediation Registry

**Purpose.** This is the canonical, single-source list of every stub, placeholder,
deferred primitive, and operational pre-requisite that must be remediated before
OASIS can act as a **production custodial blockchain provider** that moves real
value. It exists because the ardanova-provider-port initiative deliberately ships
the *architecture* (signing seam, custody policy, KYC, fiat, multi-tenancy) ahead
of the *production-grade primitives* — and that gap must never be invisible.

**Reading this file.** Items are grouped by severity. A 🔴 BLOCKER must be closed
before that capability touches **mainnet / real value**. A 🟠 PRE-PROD must be
closed before a production launch even on testnet-as-prod. A 🟡 HARDENING is a
real gap that is acceptable for an internal/beta cut. Each item names its owning
track and the file:line evidence where the stub lives.

> Greenfield context: per project memory (`greenfield-prelaunch-no-compat`), there
> are no live customers or data yet. Nothing here is a migration; everything is a
> "before we flip it on" gate.

---

## 🔴 BLOCKERS — must close before mainnet / real value flow

### B1. Real Algorand keypair generation
- **Stub:** `Core/WalletKeyService.cs:116-129` (`DeriveEd25519PublicKey` = HMAC-SHA512,
  not Ed25519) and `:478-489` (`GenerateMnemonic` = word-index, not BIP39/Algorand-25).
  Addresses generated today are **unspendable**.
- **Owner:** `signing-core-keystone` (Phase 1).
- **Done when:** seed→address matches `Algorand2`-derived; mnemonic re-imports to the
  same address; round-trip test green.

### B2. Real server-side signing for Algorand
- **Stub:** `Providers/Blockchain/Algorand/AlgorandProvider.cs:148-164`
  (`Burn`/`Transfer` return hard errors); `MintAsync`/`CreateASAAsync` record but do
  not sign/submit. (ArdaNova's analogue `AlgorandService.cs:777-797 SignTransaction`
  is **also** a stub — do not port its crypto.)
- **Owner:** `signing-core-keystone` (Phases 2–3).
- **Done when:** build→sign→submit→confirm works against testnet with canonical
  msgpack; known-vector sign test matches `Algorand2`.

### B3. KMS/HSM-backed custody (replace config-secret key derivation)
- **Stub:** `Core/WalletKeyService.cs:15-20` derives the data-encryption key from
  `SHA-256(config "OASIS:WalletEncryptionKey")`. A config secret in appsettings/env is
  **not** production-grade custody for value-bearing keys.
- **Owner:** `custody-key-management`.
- **Done when:** the data key (or the per-user keys themselves) live in a KMS/HSM;
  no value-bearing private key is recoverable from app config alone. Cross-ref
  `bridge-unsafe-pre-launch` memory + `api-safety-hardening` track risk posture.

### B4. Idempotency / replay / atomicity for fiat-triggered allocation
- **Risk:** fiat settles on the tenant (ArdaNova) and triggers an OASIS wallet/asset
  allocation. Without idempotency a webhook replay double-allocates. The bridge
  already shipped this mistake once (`bridge-unsafe-pre-launch`).
- **Owner:** `fiat-stripe-bridge` (mirror the `clientIdempotencyKey` plumbing in
  `WalletManager.TopUpAsync`).
- **Done when:** duplicate idempotency-key returns the original result with no second
  on-chain side effect; test proves it.

### B5. Cross-tenant isolation enforcement
- **Risk:** a tenant principal must NEVER reach another tenant's avatars/wallets.
  The new `OwnerTenantId` guard is the only thing standing between tenants.
- **Owner:** `tenant-onboarding`.
- **Done when:** integration test proves cross-tenant access is rejected
  (404, "not found, not forbidden"); scope enforcement test green.

### B6. Mainnet enablement gate
- **Risk:** flipping to mainnet before B1–B5 close moves real value over unspendable-
  /unsigned-/unprotected paths.
- **Owner:** ops + `signing-core-keystone` (keep existing hard mainnet guards, e.g.
  the faucet `Mainnet` block, until B1–B5 are all `[x]`).
- **Done when:** a single documented checklist gates the mainnet config flip on
  B1–B5 + a security review sign-off.

---

## 🟠 PRE-PROD — close before any production launch

### P1. Private-key zeroing (string-immutability constraint)
- **Constraint:** `WalletKeyService.DecryptPrivateKey` returns a hex `string`
  (`Core/WalletKeyService.cs:45-48`); .NET strings can't be reliably zeroed. The
  decrypt→sign→zero contract needs a `byte[]` decrypt overload at the custody
  boundary (this is why `SigningKeyMaterial` uses `byte[]`, signing-core D2).
- **Owner:** `custody-key-management`.

### P2. Key rotation / re-encryption
- **Gap:** no path to re-wrap stored keys under a new `OASIS:WalletEncryptionKey`.
- **Owner:** `custody-key-management`.

### P3. Platform account fee-funding & monitoring
- **Gap:** a custodial signer needs ALGO to pay fees. Provisioning the platform
  account, funding it, and alerting on low balance are ops deploy-steps.
- **Owner:** ops (surfaced by `signing-core-keystone`).

### P4. KYC provider secrets + real provider
- **Stub:** Veriff (or chosen provider) API secrets; the default `ManualKycProvider`
  is admin-review only. Real automated KYC needs provider credentials + webhook
  signing secret.
- **Owner:** `kyc-module`.

### P5. KYC gating actually wired on wallet-generate + mint
- **Gap:** the gate is reusable but the wiring onto `WalletManager.GenerateWalletAsync`
  and the mint path is a documented cross-track seam, not yet enforced everywhere.
- **Owner:** `kyc-module` (seam) + `signing-core-keystone`/wallet owners (enforcement).

### P6. Tenant onboarding runbook executed for the first tenant
- **Gap:** the mechanism exists; the first real tenant (ArdaNova) must be registered,
  issued a tenant-scoped API key, and its user→Avatar mapping populated.
- **Owner:** `tenant-onboarding`.

---

## 🟡 HARDENING — real gaps, acceptable for internal/beta

### H1. Solana + Ethereum real keygen & signing
- **Stub:** `Core/WalletKeyService.cs:133-142` (secp256k1 HMAC placeholder) and the
  Solana Ed25519 path; no real Solana/Ethereum signer yet. Algorand-first by design.
- **Owner:** future `signing-core-*` chain tracks.

### H2. Soulbound clawback-revoke primitive
- **Deferred:** mint sets platform as manager/freeze/clawback, but the revoke-by-
  clawback+destroy primitive is deferred (signing-core D4).
- **Owner:** follow-up to `signing-core-keystone`.

### H3. Simulated-data distinguishability audit
- **Guardrail:** `db-only-null-provider` marks simulated addresses/tx-hashes
  (`sim:` prefix). Audit that no code path can mistake simulated data for settled
  on-chain value when a tenant later switches Simulated→Live.
- **Owner:** `db-only-null-provider`.

### H4. Algorand address checksum validation
- **Pre-existing gap (PROVIDERS.md):** `ValidateAddressAsync` is regex-only, no
  SHA-512/256 checksum check. Real keygen (B1) makes a proper validator cheap to add.
- **Owner:** `signing-core-keystone` follow-up.

### H5. Brand-leak guard in CI
- **Guardrail:** every ported track has a "no `ArdaNova` strings" acceptance grep.
  Promote it to a CI check so the brand boundary can't regress.
- **Owner:** any track / CI ops.

---

## Status board

| ID | Severity | Item | Owning track | State |
|----|----------|------|--------------|-------|
| B1 | 🔴 | Real Algorand keygen | signing-core-keystone | open |
| B2 | 🔴 | Real Algorand signing | signing-core-keystone | open |
| B3 | 🔴 | KMS/HSM custody | custody-key-management | open |
| B4 | 🔴 | Fiat-allocation idempotency | fiat-stripe-bridge | open |
| B5 | 🔴 | Cross-tenant isolation | tenant-onboarding | open |
| B6 | 🔴 | Mainnet enablement gate | ops + signing-core-keystone | open |
| P1 | 🟠 | Key zeroing (byte[]) | custody-key-management | open |
| P2 | 🟠 | Key rotation | custody-key-management | open |
| P3 | 🟠 | Platform fee-funding | ops | open |
| P4 | 🟠 | KYC provider secrets | kyc-module | open |
| P5 | 🟠 | KYC gating wired | kyc-module + wallet owners | open |
| P6 | 🟠 | First-tenant onboarding | tenant-onboarding | open |
| H1 | 🟡 | Solana/Ethereum keygen+signing | future chain tracks | open |
| H2 | 🟡 | Soulbound clawback-revoke | signing-core follow-up | open |
| H3 | 🟡 | Simulated-data distinguishability | db-only-null-provider | open |
| H4 | 🟡 | Algorand checksum validation | signing-core follow-up | open |
| H5 | 🟡 | Brand-leak CI guard | CI ops | open |
