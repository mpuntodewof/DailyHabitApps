# ADR 0001: 2FA Recovery Codes — storage, single-use, and lifecycle

**Status:** Accepted
**Date:** 2026-06-06

## Context

TOTP 2FA is implemented but offers no fallback if a user loses their authenticator. We add recovery codes. Several security-relevant choices need recording.

## Decision

- **One row per code**, not a JSON blob — makes single-use atomic and the remaining-count query trivial.
- **Store SHA-256 hashes, never plaintext.** Codes are returned to the client exactly once (at enrollment / regeneration).
- **Hash with SHA-256, not BCrypt.** Codes are random ~60-bit, single-use values; a slow hash protects low-entropy passwords and adds no value here. Matches the existing `RefreshToken.TokenHash` pattern.
- **Single-use is enforced with a guarded atomic UPDATE** (`ExecuteUpdateAsync ... WHERE !IsUsed`), so concurrent submissions of the same code cannot both succeed.
- **Regeneration replaces the whole set;** **disabling 2FA deletes all codes.**
- **Login discriminates TOTP vs recovery via an explicit `IsRecoveryCode` flag** sent by the UI — not format auto-detection — because the UI already knows the mode and explicitness avoids ambiguity.

## Consequences

**Positive:** minimal state, atomic single-use, no plaintext at rest, consistent with existing token hashing.
**Negative / accepted risk:** no per-attempt rate-limiting (mitigated by entropy + the 5-min pending-token gate); SHA-256 hashing logic is duplicated-in-spirit with refresh-token hashing (not shared until a third call site justifies it).

## Alternatives considered

- BCrypt hashing — rejected (no benefit for high-entropy single-use codes).
- Single blob of codes — rejected (single-use marking and counting become awkward).
- Auto-detect TOTP-vs-recovery by format — rejected (ambiguous; UI already knows).
