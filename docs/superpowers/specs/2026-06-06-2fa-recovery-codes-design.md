# 2FA Recovery Codes — Design

> Status: approved for implementation
> Date: 2026-06-06
> Roadmap item: §9 Near-term — "2FA recovery codes — fallback when an authenticator device is lost."

## 1. Goal

Add single-use recovery codes to the existing TOTP-based two-factor authentication so a
user who loses their authenticator device can still log in. Covers the full lifecycle:
**issue** codes at enrollment, **use** one to log in, and **regenerate** a fresh set from
Settings.

## 2. Context — existing 2FA

The current 2FA flow (unchanged by this work except where noted):

- Entity [`UserTwoFactor`](../../../server/AtomicHabits/Models/UserTwoFactor.cs) — Base32 TOTP secret, `IsEnabled`, audit timestamps. Unique on `UserId`.
- Service [`TwoFactorService`](../../../server/AtomicHabits/Services/TwoFactorService.cs) — `StartEnrollmentAsync`, `ConfirmEnrollmentAsync`, `DisableAsync`, `IsEnabledAsync`, `VerifyAsync` (TOTP, ±1 step window).
- Controller [`TwoFactorController`](../../../server/AtomicHabits/Controllers/TwoFactorController.cs) — `status`, `enable-init`, `enable-confirm`, `disable`.
- Login: [`AuthService.LoginAsync`](../../../server/AtomicHabits/Services/AuthService.cs) returns `{ requiresTwoFactor, twoFactorToken }` (5-min pending JWT) when 2FA is enabled. `VerifyTwoFactorAsync(pendingToken, code, ...)` validates the pending token, then calls `_twoFactor.VerifyAsync` (TOTP) and issues tokens via `IssueTokensAsync`.
- Frontend: [`TwoFactorDialog.jsx`](../../../client-ui/src/views/settings/TwoFactorDialog.jsx) (enable/disable), 2FA challenge form in [`AuthLogin.jsx`](../../../client-ui/src/views/authentication/auth/AuthLogin.jsx), `AuthContext.verifyTwoFactor(token, code)`.

Recovery codes plug into three points: **generation** in `ConfirmEnrollmentAsync`,
**login verification** in `VerifyTwoFactorAsync`, and a new **regenerate** path.

## 3. Data model

New entity, one row per code (so individual codes can be marked used):

```
TwoFactorRecoveryCode
├── Id         int PK
├── UserId     int FK → User        (indexed: IX_TwoFactorRecoveryCodes_UserId)
├── CodeHash   string  (max 128)    SHA-256 hex of the normalized code
├── IsUsed     bool   = false
├── UsedAt     DateTime?
└── CreatedAt  DateTime = UtcNow
```

- **Hashing:** SHA-256 of the normalized code (uppercase, dashes stripped). Consistent with
  the existing `RefreshToken.TokenHash` pattern. Codes are high-entropy + single-use, so
  SHA-256 is appropriate (BCrypt is reserved for low-entropy passwords).
- **Code format:** 10 codes per set. Each `XXXX-XXXX-XXXX` using Crockford base32 alphabet
  (`0-9A-Z` minus I/L/O/U), ~60 bits of entropy. Generated with `RandomNumberGenerator`.
- **Storage:** plaintext codes are returned to the client **exactly once** (at generation);
  the DB holds only hashes.

Changes:
- New `DbSet<TwoFactorRecoveryCode> TwoFactorRecoveryCodes` + non-unique index on `UserId` in
  [`AppDbContext`](../../../server/AtomicHabits/Data/AppDbContext.cs) `OnModelCreating`.
- New migration `AddTwoFactorRecoveryCodes`.

## 4. Backend

### Service — extend `ITwoFactorService`

- `Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(int userId, CancellationToken ct)`
  — deletes any existing rows for the user, generates 10 codes, stores their hashes, returns
  the 10 plaintext codes. Idempotent-replace semantics.
- `Task<bool> VerifyRecoveryCodeAsync(int userId, string code, CancellationToken ct)`
  — normalize + hash input, find an unused row with matching `CodeHash`, set `IsUsed = true`
  + `UsedAt`, `SaveChanges`, return success. Returns false if no match or already used.
- `Task<int> CountRemainingRecoveryCodesAsync(int userId, CancellationToken ct)`
  — count of `!IsUsed` rows for the user.

`ConfirmEnrollmentAsync` is modified to call `GenerateRecoveryCodesAsync` after setting
`IsEnabled = true`, and to return the codes in its result:
`Result = new { enabled = true, recoveryCodes = [...] }`.

### Controller — `TwoFactorController`

- `enable-confirm` — response now carries `recoveryCodes[]` (unchanged route/verb).
- `POST /api/TwoFactor/recovery-codes/regenerate` — body `{ code }` (a **current TOTP code**
  is required; reuse `VerifyAsync` to authorize). On success returns `{ recoveryCodes: [...] }`.
- `GET /api/TwoFactor/recovery-codes/count` — returns `{ remaining }`.

### Login path — `AuthService.VerifyTwoFactorAsync`

The `verify-2fa` DTO ([`VerifyTwoFactorDto`](../../../server/AtomicHabits/Models/DTO/AuthenticationDTO.cs)) gains an
explicit optional `bool IsRecoveryCode = false`. The UI sets this flag — **no auto-detection**.

`VerifyTwoFactorAsync(pendingToken, code, isRecoveryCode, ctx, ct)`:
1. Validate pending token → userId (unchanged).
2. If `isRecoveryCode`: `ok = await _twoFactor.VerifyRecoveryCodeAsync(userId, code, ct)`.
   Else: `ok = await _twoFactor.VerifyAsync(userId, code, ct)` (existing TOTP path).
3. On `ok`, issue tokens via `IssueTokensAsync` (unchanged). On failure, return the existing
   "Invalid code" 400.

`AuthController.VerifyTwoFactor` passes `dto.IsRecoveryCode` through.

## 5. Frontend

### Enrollment — `TwoFactorDialog.jsx` (enable mode)

Enable becomes a 3-step flow: **QR → confirm 6-digit code → show recovery codes**.
After `enable-confirm` succeeds, switch to a "Save your recovery codes" panel:
- Render the 10 codes in a monospace grid.
- **Copy** (clipboard) and **Download .txt** buttons.
- An "I have saved these codes" checkbox that gates the final **Done** button.
- `onChanged?.()` fires only after Done (so Settings refreshes status + count).

### Recovery login — `AuthLogin.jsx` (2FA challenge)

- Add a "Use a recovery code instead" link under the 6-digit input. Toggling swaps to a
  recovery-code text input (format hint `XXXX-XXXX-XXXX`) and a "Use an authenticator code
  instead" link to swap back.
- Submit calls `verifyTwoFactor(token, code, isRecoveryCode)` with the flag set per mode.

### Regenerate — Settings

When 2FA is enabled, show **"Recovery codes: N remaining"** (from
`GET /recovery-codes/count`) plus a **Regenerate** button. Regenerate prompts for a current
TOTP code, calls `/recovery-codes/regenerate`, then reuses the same "Save your recovery
codes" panel to display the fresh set. Wired in [`Settings.jsx`](../../../client-ui/src/views/settings/Settings.jsx) /
`TwoFactorDialog.jsx`.

### Context — `AuthContext.jsx`

`verifyTwoFactor(token, code, isRecoveryCode = false)` gains the third arg and forwards it as
`isRecoveryCode` in the `verify-2fa` body.

## 6. Edge cases & rules

- **Single-use:** a code is marked `IsUsed` the moment it verifies; re-submitting it fails.
- **Regeneration invalidates the old set:** `GenerateRecoveryCodesAsync` deletes prior rows.
- **Disable 2FA:** `DisableAsync` should also delete the user's recovery codes (they're
  meaningless without 2FA, and a re-enroll generates a fresh set).
- **Running low:** the Settings count surfaces remaining codes; no hard enforcement beyond
  display in this round.
- **Authorization to regenerate:** requires a valid current TOTP code (same bar as `disable`).

## 7. Testing / verification

The repo has no automated test harness yet, so verification is:
- Backend `dotnet build` clean; `dotnet ef migrations add AddTwoFactorRecoveryCodes` generates
  and `dotnet ef database update` applies cleanly.
- Frontend `npx vite build` and `npm run typecheck` pass.
- Manual end-to-end: enable 2FA → capture the 10 codes → log out → log in with password →
  on the 2FA challenge choose "use a recovery code" → enter one → confirm login succeeds →
  confirm the same code is rejected on a second attempt → in Settings see remaining = 9 →
  Regenerate (with a TOTP code) → confirm old codes now rejected and count resets to 10.

Recorded as a manual-verification feature in the roadmap changelog.

## 8. Out of scope (this round)

- Emailing recovery codes / printable PDF.
- "Low codes remaining" nags or auto-regeneration.
- Rate-limiting recovery-code attempts (the existing flow has no per-attempt throttle; a
  separate hardening item).
