# Momentum — Architecture & Roadmap

> Single reference document covering the current system design, existing feature inventory, fixes to apply to existing features, and the future feature roadmap.
> Last reviewed: 2026-04-29

---

## Table of Contents

1. [System Overview](#1-system-overview)
2. [High-Level Architecture](#2-high-level-architecture)
3. [Backend Layering](#3-backend-layering)
4. [Frontend Layering](#4-frontend-layering)
5. [Domain Model](#5-domain-model)
6. [Request Flow Example](#6-request-flow-example)
7. [Existing Feature Inventory](#7-existing-feature-inventory)
8. [Fixes for Existing Features](#8-fixes-for-existing-features)
9. [Future Features Roadmap](#9-future-features-roadmap)
10. [Platform / DevOps Roadmap](#10-platform--devops-roadmap)
11. [Suggested Execution Order](#11-suggested-execution-order)

---

## 1. System Overview

**Momentum** is a full-stack habit-tracking & analytics platform.

| Layer    | Stack                                                                 |
| -------- | --------------------------------------------------------------------- |
| Frontend | React 19, MUI 7, ApexCharts, Vite, React Router 7, axios, jwt-decode  |
| Backend  | ASP.NET Core 8 Web API, EF Core 9, BCrypt, JWT Bearer, Swagger        |
| Database | SQL Server (`AtomicHabitsDb`)                                         |
| Auth     | JWT (1 h access) + opaque refresh token (SHA-256 hashed, DB-stored)   |
| Email    | `IEmailSender` → `EmailService` (SMTP) for password reset             |

Repository layout:

```
Daily Habit Tracker Apps/
├── client-ui/   # React SPA (Vite)
└── server/      # ASP.NET Core 8 solution (AtomicHabits.sln)
```

---

## 2. High-Level Architecture

```
+--------------------+       HTTPS/JSON        +-----------------------+      EF Core      +-----------------+
|   React SPA        | <--------------------> |  ASP.NET Core 8 API   | <---------------> |  SQL Server DB  |
|  (Vite + MUI)      |   JWT Bearer + Refresh |  AtomicHabits.csproj  |       (LINQ)      |  AtomicHabitsDb |
+--------------------+                        +-----------------------+                   +-----------------+
        |                                              |    |
        | jwt-decode / cookies                         |    +--> SMTP (IEmailSender → EmailService)
        | axios interceptors (retry on 401)            |
        +--------------- CORS "AtomicUI" --------------+
```

Key cross-cutting facts:

- All API responses use a uniform `ApiResponse` envelope: `IsSuccess`, `StatusCode`, `Result`, `ErrorMessages`.
- JWT is configured from environment variables (`JWT_SECRET`, `JWT_ISSUER`, `JWT_AUDIENCE`) — see [Program.cs:68-82](server/AtomicHabits/Program.cs#L68-L82).
- CORS is limited to a hard-coded list of localhost origins ([Program.cs:154-168](server/AtomicHabits/Program.cs#L154-L168)).
- DI is registered in [Program.cs:28-51](server/AtomicHabits/Program.cs#L28-L51).

---

## 3. Backend Layering

Controller → Service → Repository → EF Core `AppDbContext`.

```
Controllers/                       Services/                       Repositories/
- AuthController.cs            ->  - AuthService.cs           ->   - UserRepositories.cs
- HabitController.cs               - TokenService.cs               - HabitRepositories.cs
- HabitTrackingController.cs       - HabitService.cs               - HabitTrackingRepositories.cs
- DashboardController.cs           - HabitTrackingService.cs       - StreakRepositories.cs
- WeatherForecastController (X)    - DashboardService.cs           - DashboardRepositories.cs
                                   - EmailService.cs

Data/                       Models/                            Models/DTO/
- AppDbContext.cs           - User / Role / UserRole           - AuthenticationDTO.cs
- AppDbContextFactory.cs    - Permission / Module              - HabitDTO.cs
- ConnectionFactory.cs      - RolePermission                   - HabitTrackingDTO.cs
- DbSeeder.cs               - Habit / HabitTracking            - HabitDistributionDTO.cs
                            - HabitReminder / Streak           - HabitReminderDTO.cs
                            - RefreshToken / JwtKeys           - HabitSummaryDto.cs
                            - ApiResponse                      - CardOverviewsDTO.cs
                                                               - ForgotPasswordDTO.cs
```

`AppDbContext` DbSets: `Users, Roles, UserRoles, Permissions, Modules, RolePermissions, JwtKeys, Habits, HabitTrackings, HabitReminders, Streaks, RefreshTokens` ([AppDbContext.cs:8-21](server/AtomicHabits/Data/AppDbContext.cs#L8-L21)).

---

## 4. Frontend Layering

```
client-ui/src/
├── api/
│   ├── axiosInstance.js        # baseURL=VITE_API_URL, interceptors (Bearer + 401 refresh queue)
│   └── demoApi.js
├── context/                    # global state via React Context
│   ├── AuthContext.jsx         # login/register/logout/restoreSession
│   ├── HabitContext.jsx        # CRUD habits
│   ├── HabitTrackingContext.jsx# tracking, stats, distributions
│   └── SnackbarContext.jsx     # toast notifications
├── routes/Router.jsx           # createBrowserRouter, lazy + Suspense, ProtectedRoute / PublicRoute
├── layouts/                    # FullLayout, BlankLayout, header, sidebar, footer
├── views/
│   ├── authentication/         # Login, Register, ForgotPassword, Error
│   ├── dashboard/              # TopCards, HabitCompletionRate, HabitHeatmapCalendar, …
│   ├── habit/                  # Habit + Calendar, HabitDialogForm, HabitTrackingDialog, …
│   ├── stats/Stats.jsx
│   └── settings/Settings.jsx
├── utils/                      # cookieUtils, tokenUtils (jwt-decode, refresh queue)
└── App.jsx                     # Provider tree: Theme → Snackbar → Auth → Habit → HabitTracking → Router
```

Token handling lives in [tokenUtils.js](client-ui/src/utils/tokenUtils.js) and [axiosInstance.js](client-ui/src/api/axiosInstance.js); the auth context restores session on mount via `restoreSession` ([AuthContext.jsx:24-46](client-ui/src/context/AuthContext.jsx#L24-L46)).

---

## 5. Domain Model

```
User 1 ── * UserRole * ── 1 Role * ── * RolePermission * ── 1 Permission * ── 1 Module
User 1 ── * Habit 1 ── * HabitTracking
                 ├── 1 Streak
                 └── * HabitReminder
User 1 ── * RefreshToken
```

- `Habit`: name, color, frequency (`Daily/Weekly/Monthly`), goal (`GoalValue`, `GoalUnit`, `GoalFrequency`), `IsArchived`, audit fields.
- `HabitTracking`: per-day log with `IsCompleted`, `TimeSpentMinutes`, optional `Notes`.
- `Streak`: current/best streak, completion rate, totals.
- `HabitReminder`: `ReminderTime`, `DaysOfWeek` (e.g. "Mon,Wed,Fri"), `IsEnabled`.
- `RefreshToken`: hashed token, `ExpiresAt`, `IsRevoked`, FK → User.

---

## 6. Request Flow Example

**`POST /api/HabitTracking/{habitId}/submit-daily`**

1. UI calls `submitDailyHabit` from `HabitTrackingContext`. `axiosInstance` attaches `Bearer <accessToken>` from cookie.
2. Controller [`HabitTrackingController.SubmitDailyHabit`](server/AtomicHabits/Controllers/HabitTrackingController.cs#L82) extracts the raw token and delegates to the service.
3. `HabitTrackingService.PostDailyHabit` ([HabitTrackingService.cs:286-367](server/AtomicHabits/Services/HabitTrackingService.cs#L286-L367)):
   - resolves user via `IAuthService.GetCurrentUserFromJwt`
   - looks up the habit (must belong to user)
   - rejects duplicate same-day tracking
   - creates `HabitTracking` and upserts `Streak`
4. EF Core persists to SQL Server.
5. If 401, axios interceptor calls `/Auth/refresh-token` and replays the original request.

---

## 7. Existing Feature Inventory

| Domain                          | Endpoint(s)                                                            | Status                                          |
| ------------------------------- | ---------------------------------------------------------------------- | ----------------------------------------------- |
| Register / Login                | `POST /api/Auth/register`, `/login`                                    | ✅ Working, BCrypt                              |
| Forgot / Reset Password         | `POST /api/Auth/forgot-password`, `/reset-password`                    | ✅ Working, email link                          |
| Refresh / Revoke / Logout       | `/refresh-token`, `/revoke-refresh-token`, `/logout`                   | ✅ Fixed (#4, #6)                               |
| Habit CRUD                      | `GET/POST/PUT/DELETE /api/Habit/...`                                   | ✅ Working                                      |
| Habit summary                   | `GET /api/Habit/habits-summary/{userId}`                               | ⚠️ Math assumes daily-only                      |
| Habit tracking dates            | `GET /api/HabitTracking/habit-tracking-dates`                          | ✅ Working                                      |
| Habit stats                     | `GET /api/HabitTracking/get-habit-stats/{habitId}/{userId}`            | ✅ Working                                      |
| Daily progress submission       | `POST /api/HabitTracking/{habitId}/submit-daily`                       | ✅ Working                                      |
| Manual progress submission      | `POST /api/HabitTracking/submit-habit-progress`                        | ✅ Working                                      |
| Distribution charts             | `GET /api/HabitTracking/get-weekly|monthly|yearly`                     | ✅ Working                                      |
| Dashboard card overviews        | `GET /api/Dashboard/habit-card-overviews`                              | ✅ Working                                      |
| RBAC tables                     | `Role`, `Permission`, `Module`, `RolePermission`                       | ✅ Seeded + enforced via `[Permission("…")]`   |
| Habit reminders                 | `HabitReminder` entity + scheduler + UI                                | ✅ CRUD endpoints + dispatcher + per-habit dialog |
| Two-Factor Auth                 | TOTP enrol + login challenge + Settings UI                             | ✅ Full flow: status / enable (QR) / confirm / disable |
| Habit tags                      | `Tag` + `HabitTag` join + chips + filter                               | ✅ CRUD + attach/detach + chips on cards + filter dropdown |
| Pagination & filtering          | `GET /api/Habit/search` + UI                                           | ✅ `?page&pageSize&search&tagId&includeArchived` + MUI Pagination |
| Admin role assignment           | `Roles.Manage` permission + UI                                         | ✅ assign/revoke chips, prevents self-Admin removal |
| Settings page                   | Toggles + theme + tag CRUD                                             | ✅ Persisted via `UserPreferences`; dark mode wired to MUI theme; tag CRUD section |
| Dashboard heatmap               | `HabitHeatmapCalendar` component                                       | ✅ Wired to `/api/Dashboard/heatmap`            |
| Habit archiving                 | `Habit.IsArchived` flag                                                | ✅ `POST /api/Habit/archive/{id}` + `restore/{id}` + `?includeArchived` |

Legend: ✅ working, ⚠️ working with known bugs, 🟡 partially built / schema-only.

---

## 8. Fixes for Existing Features

### 🔒 Security

| #  | Done | Issue                                                                                                                                                                  | Action                                                                                                            |
| -- | :--: | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| 1  | ✅   | Controllers re-parse the raw `Authorization` header ([HabitController.cs:30-31](server/AtomicHabits/Controllers/HabitController.cs#L30-L31))                            | Use `User.FindFirstValue(ClaimTypes.NameIdentifier)`; trust JWT middleware. **(Done — `ClaimsPrincipalExtensions.GetUserId()` + applied across Habit/HabitTracking/Dashboard controllers)** |
| 2  | ✅   | `GET /Habit/get-habits/{userId}` trusts the route param — IDOR risk                                                                                                    | Always derive `userId` from JWT; reject if path `userId` ≠ token user (or remove the route param entirely). **(Done — JWT `sub` now overrides any client-supplied userId in path/query/body)** |
| 3  | ✅   | JWT secret read only from env vars ([TokenService.cs:29-31](server/AtomicHabits/Services/TokenService.cs#L29-L31)); NRE if missing                                      | Bind `IOptions<JwtOptions>` from `appsettings.json` + env override; validate on startup. **(Done — `Config/JwtOptions.cs`, `ValidateOnStart`)** |
| 4  | ✅   | **Refresh-token rotation bug**: `Hash(newRefresh.ToString())` hashes a `Task<string>` ([AuthService.cs:289-294](server/AtomicHabits/Services/AuthService.cs#L289-L294)) | `await _tokenService.GenerateRefreshToken()` before hashing; rotated tokens are currently unusable. **(Done — awaited, hashed correctly)** |
| 5  | ✅   | Refresh-token expiry inconsistent (7 d issue vs 1 d rotate)                                                                                                            | Pick one TTL; add to `JwtOptions`. **(Done — both paths now 7 days; pending #3 to move TTL into options)** |
| 6  | ✅   | `RevokeRefreshTokenAsync` clears legacy `User.RefreshToken*` fields, never sets `IsRevoked=true` on `RefreshTokens` rows                                               | Update `RefreshTokens` rows to `IsRevoked = true` for the user; remove dead `User.RefreshToken*` columns. **(Done — leftover resolved: removed `User.RefreshToken`, `RefreshTokenExpiry`, `IsRevoked` properties + columns dropped via migration `DropLegacyRefreshTokenColumnsAndUniqueUserIndexes`.)** |
| 7  | ✅   | Reset-password URL hard-coded to `http://localhost:3000/ResetPassword` ([AuthService.cs:165](server/AtomicHabits/Services/AuthService.cs#L165))                         | Move base URL to config; UI runs on Vite (5173). **(Done — `App:WebBaseUrl` + `App:ResetPasswordPath`)** |
| 8  | ✅   | CORS allow-list is dev-only ([Program.cs:158-163](server/AtomicHabits/Program.cs#L158-L163))                                                                            | Read origins from configuration per environment. **(Done — `Cors:AllowedOrigins[]`)** |
| 9  | ✅   | Tokens stored in non-HttpOnly cookies via `tokenUtils.storeTokens` — XSS-readable                                                                                      | Move refresh token to server-set `HttpOnly; Secure; SameSite=Strict` cookie; keep access token in memory. **(Done — see Changelog 2026-04-28 part 2)** |
| 10 | ✅   | Dead code: `WeatherForecastController.cs`, `WeatherForecast.cs`, large commented-out `RefreshTokenAsync` block, unused `Paket.Core` package                            | Delete. **(Done — files removed, package dropped, commented blocks cleaned, also removed unused `Models.JwtOptions`)** |

### 🐛 Correctness

| #  | Done | Issue                                                                                                                                                                                                              | Action                                                                                          |
| -- | :--: | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------- |
| 11 | ✅   | Streak math duplicated across `DashboardService`, `HabitTrackingService.GetHabitStats`, and `StreakRepositories.UpsertStreakAfterTracking`                                                                          | Centralize into a single `StreakCalculator`. **(Done — `Utils/StreakCalculator.cs`; `DashboardService` and `HabitTrackingService.GetHabitStats` now call it. `StreakRepositories.UpsertStreakAfterTracking` left as-is — separate persistence concern, candidate for later refactor)** |
| 12 | ✅   | Week-of-year off by one when today is Sunday (`DayOfWeek=0`) ([HabitService.cs:158](server/AtomicHabits/Services/HabitService.cs#L158))                                                                             | Use `ISOWeek` or culture-aware helper. **(Done — `StartOfIsoWeek(today)` returns Monday correctly for any day)** |
| 13 | ✅   | `HabitContext.deleteHabit` calls `resToken.userId` ([HabitContext.jsx:90](client-ui/src/context/HabitContext.jsx#L90)) but JWT claim is `sub` — refresh-after-delete silently fails                                | Use `resToken.sub`. **(Done)**                                                                  |
| 14 | ☐    | Dashboard heatmap data hard-coded ([Dashboard.jsx:18-26](client-ui/src/views/dashboard/Dashboard.jsx#L18-L26))                                                                                                       | Add a real heatmap endpoint and wire it.                                                        |
| 15 | ☐    | Settings page is fully local state ([Settings.jsx](client-ui/src/views/settings/Settings.jsx)) — toggles do nothing server-side                                                                                     | Persist via a `UserPreferences` table or hide until backend exists.                             |
| 16 | ✅   | `GetHabits` returns 404 when user has zero habits ([HabitService.cs:50-56](server/AtomicHabits/Services/HabitService.cs#L50-L56))                                                                                   | Empty list is a valid `200`. **(Done — returns `200` with empty array)**                         |
| 17 | ✅   | `HabitSummary` divides by `* habits.Count` assuming every habit is daily                                                                                                                                            | Compute expected sessions per habit using its `GoalFrequency`. **(Done — `ExpectedSessions(habit, daysElapsed, periodLength)` helper sums per-habit expectations across daily/weekly/monthly/yearly)** |
| 18 | ✅   | `createHabit` reducer bug ([HabitContext.jsx:46-47](client-ui/src/context/HabitContext.jsx#L46-L47)): `habit.id === habit.id` is always true                                                                        | Fix shadowed variable; replace by id correctly. **(Done — appends new habit instead of overwriting)** |

### ⚙️ Architecture / Code Quality

| #  | Done | Issue                                                                                                       | Action                                                                                |
| -- | :--: | ----------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| 19 | ✅   | README claims TypeScript + Controller-Service-UseCase; reality is JSX, no UseCase layer                     | Update README, or introduce TS/UseCase layer. **(Done — README rewritten to match reality: JSX, Controller→Service→Repository, real folder tree, current auth model, configuration & getting-started sections, link to roadmap.)** |
| 20 | ✅   | README references `Mappers/`, `Validators/`, `Utils/` folders that don't exist                              | Add or remove from docs. **(Done — `Utils/`, `Validators/`, `Config/`, `Middleware/` all now exist; `Mappers/` deferred until needed)** |
| 21 | ✅   | `AppDbContext` lives in the default namespace ([AppDbContext.cs:4](server/AtomicHabits/Data/AppDbContext.cs#L4)) | Move to `AtomicHabits.Data`. **(Done — type now in `AtomicHabits.Data` namespace; consumers updated)** |
| 22 | ✅   | `HabitController` injects raw `AppDbContext` ([HabitController.cs:13](server/AtomicHabits/Controllers/HabitController.cs#L13)) and never uses it | Remove. **(Done)**                                                                    |
| 23 | ✅   | No EF model configuration / indexes                                                                         | Add `OnModelCreating` indexes: `HabitTracking(HabitId, TrackingDate)`, `RefreshTokens(TokenHash)`, unique `Users(Email)`. **(Done — leftover resolved: `Users(Email)` and `Users(Username)` now `IsUnique()`. Migration `DropLegacyRefreshTokenColumnsAndUniqueUserIndexes` includes a SQL guard that aborts with a clear error message if duplicates exist before creating the unique indexes.)** |
| 24 | ✅   | Repeated try/catch + `ApiResponse` boilerplate in every service method                                      | Add global exception middleware. **(Done — `Middleware/GlobalExceptionMiddleware.cs` returns standard `ApiResponse` 500. Existing services kept their try/catch for now to avoid churn; new code can rely on the middleware.)** |
| 25 | ✅   | Manual validation in every method                                                                           | Introduce FluentValidation (or DataAnnotations + a filter). **(Done — `FluentValidation.AspNetCore` 11.3.0 added; `Validators/AuthValidators.cs` and `Validators/HabitValidators.cs` cover Register/Login/Habit/HabitTracking; auto-validation wired in `Program.cs`)** |

---

## 9. Future Features Roadmap

### 🎯 Near-term (next iteration)

- **Push notifications** — Web Push / VAPID for daily reminders (currently email-only).
- **Reminder UX polish** — timezone-aware times (currently UTC), tag-aware filtering of reminders.
- ~~**2FA recovery codes** — fallback when an authenticator device is lost.~~ ✅ Shipped 2026-06-07 (see Changelog).
- **Tag rename / edit** — currently the Tag UI supports create/delete; renaming uses the existing PUT endpoint but the UI is read/delete only.
- **Continue the TS migration** — port `axiosInstance`, `RequirePermission`, contexts (`AuthContext`, `HabitContext`, …) and views as work touches them. The plumbing is ready (`tsconfig.json`, types installed, `npm run typecheck`).
- **Code splitting** — `react-apexcharts` is pushing a 580 kB chunk; defer with dynamic import per route.

### 🌱 Mid-term

- **PWA / installable app** — service worker + offline-first habit logging that syncs when online.
- **Push notifications** — Web Push / VAPID for daily reminders.
- **Goal-history / progress photos / notes timeline** per habit.
- **Social features** — friends, accountability partners, shared challenges, leaderboards.
- **Habit templates & onboarding wizard** — based on Atomic Habits' "habit stacking" (cue → routine → reward fields).
- **Achievements / badges** — awarded by streak length, consistency, milestones.
- **Data import/export** — CSV/JSON; account deletion (GDPR).
- **Dark-mode persistence** and **i18n** — Settings hints at language but only en exists.

---

## 10. Platform / DevOps Roadmap

- **Real CI/CD** — README mentions Jenkins + Docker but no `Dockerfile` / `Jenkinsfile` is present. Add multi-stage Dockerfiles for both projects, a pipeline (build → test → migrate → deploy), and a `docker-compose.yml` for local SQL Server + API + UI.
- **Automated tests** — ⏳ *backend in progress* (2026-06-07): `server/AtomicHabits.Tests` (xUnit + EF InMemory + SQLite + `WebApplicationFactory` + Moq), **36 tests**. Covers `StreakCalculator`, `TwoFactorService` recovery codes, `HabitService` summary math, **`HabitTrackingService`** (duplicate-day rejection, create path) + **`StreakRepositories`** upsert + **distribution bucketing** (weekly/monthly), three regression guards (2FA pending-token, registration commit, **persisted-streak-never-advances**), register/login HTTP integration, and habit IDOR. **Still uncovered:** `DashboardService` heatmap buckets, the reminder dispatcher, and the **entire frontend** (no Vitest/RTL yet). Plans: `docs/superpowers/plans/2026-06-07-backend-test-suite.md`, `…-habit-tracking-test-slice.md`.
- **Observability** — structured logging (Serilog + Seq/ELK), OpenTelemetry traces, health-check endpoint.
- **Centralized config / secrets** — Azure Key Vault or AWS Secrets Manager; remove `Trusted_Connection=True` localhost default in [appsettings.json](server/AtomicHabits/appsettings.json#L3).
- **API versioning** — `/api/v1/...` before public release.
- **Migrate frontend to TypeScript** — match README and prevent runtime bugs (e.g. fix #13).

---

## 11. Suggested Execution Order

A pragmatic sequence to land the fixes and roadmap with the least churn.

**Phase 1 — Stop-the-bleeding (security & correctness) — COMPLETE ✅**
1. ✅ Fix #4 refresh-token rotation bug.
2. ✅ Fix #6 logout/revoke (mark `RefreshTokens.IsRevoked`).
3. ✅ Fix #1 + #2 — derive `userId` from JWT; remove path-based user IDs.
4. ✅ Fix #9 — move refresh token to HttpOnly cookie.
5. ✅ Fix #13, #16, #18 (small UI/API bugs).

**Phase 2 — Hygiene & consistency — COMPLETE ✅**
6. ✅ Fixes #11, #12, #17 — unify streak/summary math.
7. ✅ Fixes #20, #21, #23, #24, #25 — folders, namespace, EF indexes, global exception middleware, validation.
8. ✅ Fix #10 — delete dead code.
9. ✅ Fixes #7, #8, #3, #5 — config/CORS/JWT options cleanup.

**Phase 3 — Wire up half-built features — COMPLETE ✅** (tags deferred)
10. ✅ Persist Settings (#15) → `UserPreferences` table.
11. ✅ Heatmap endpoint (#14).
12. ✅ RBAC enforcement.
13. ✅ Habit archiving (tags deferred — bigger change, deserves its own batch).

**Phase 4 — New capabilities — COMPLETE ✅**
14. ✅ Reminder CRUD + background scheduler (email channel).
15. ✅ 2FA (TOTP) enrol/disable + login challenge.
16. ✅ Habit tags (entity + CRUD + attach/detach + filter).
17. ✅ Pagination & filtering on habits (`GET /api/Habit/search`).
18. ✅ Small UI affordances: 2FA login challenge, archive menu item, read-only Admin users page.

**Phase 5 — UI polish round — COMPLETE ✅**
19. ✅ Tag chips on habit cards + tag filter dropdown.
20. ✅ Search + tag filter + archive toggle + pagination on the habit page.
21. ✅ 2FA Settings UI — QR code, enable/confirm/disable.
22. ✅ Reminder UI — list, add, toggle, delete reminders per habit.
23. ✅ Admin role assignment — chip-based grant/revoke UI + backend `Roles.Manage` endpoints.

**Phase 6 — Permissions, theming, TS kickoff — COMPLETE ✅**
24. ✅ `GET /api/Auth/me` returns roles + permissions; `AuthContext` exposes `hasPermission` / `hasRole`.
25. ✅ Sidebar gates `Admin · Users` behind `Users.Read`. Reusable `<RequirePermission>` for any future gate.
26. ✅ Tag CRUD UI in Settings — create with color picker, delete via chip.
27. ✅ Dark mode in `prefs.darkMode` is applied through `buildAppTheme(mode)` in `App.jsx`.
28. ✅ TypeScript scaffolding: `tsconfig.json`, type defs installed, `tokenUtils.ts` and `cookieUtils.ts` ported. `npm run typecheck` passes.

**Phase 4 — New capabilities**
14. Reminders + scheduler + notification preferences.
15. 2FA (TOTP).
16. PWA + Web Push.
17. Social, achievements, templates, import/export.

**Phase 5 — Platform**
18. Dockerfiles, compose, CI/CD pipeline.
19. Test suites (backend + frontend).
20. Observability + secrets management.
21. API versioning + TypeScript migration.

---

*This document is the canonical reference for fixing existing features and planning future work. Keep it updated as items are shipped.*

---

## Changelog

### 2026-04-28 — Phase 1 partial landing

Backend:
- **#4** Refresh-token rotation bug fixed in [AuthService.RefreshTokenAsync](server/AtomicHabits/Services/AuthService.cs) — properly awaits `GenerateRefreshToken()` and hashes the resulting string.
- **#5** Refresh-token TTL unified to 7 days across issue and rotate paths.
- **#6** `RevokeRefreshTokenAsync` now sets `IsRevoked = true` on every active refresh-token row for the user (logout actually logs out now).
- **#1 + #2** Added [`Utils/ClaimsPrincipalExtensions.cs`](server/AtomicHabits/Utils/ClaimsPrincipalExtensions.cs) with `GetUserId()`. Applied across:
  - [HabitController](server/AtomicHabits/Controllers/HabitController.cs) — all endpoints
  - [HabitTrackingController](server/AtomicHabits/Controllers/HabitTrackingController.cs) — all endpoints (overrides DTO/path `userId` with JWT `sub`)
  - [DashboardController](server/AtomicHabits/Controllers/DashboardController.cs)
- **#16** [HabitService.GetHabits](server/AtomicHabits/Services/HabitService.cs) returns `200` with empty list instead of `404`.
- **#22** Removed unused `AppDbContext` injection from [HabitController](server/AtomicHabits/Controllers/HabitController.cs).

Frontend:
- **#13** [HabitContext.deleteHabit](client-ui/src/context/HabitContext.jsx) now reads `resToken.sub` (JWT standard claim) instead of the non-existent `resToken.userId`.
- **#18** [HabitContext.createHabit](client-ui/src/context/HabitContext.jsx) reducer no longer overwrites all habits — appends the new one.

Build status: **0 errors**, 84 pre-existing warnings (none introduced).

### 2026-04-28 — Phase 1 part 2: HttpOnly refresh-token cookie (#9)

**End-to-end auth model rewrite.** Refresh tokens no longer touch JavaScript.

Backend:
- [AuthService.cs](server/AtomicHabits/Services/AuthService.cs) — added `WriteRefreshTokenCookie` / `ClearRefreshTokenCookie` helpers (cookie attrs: `HttpOnly; Secure; SameSite=Strict; Path=/api/Auth; Expires=+7d`).
  - `IssueTokensAsync(user, ctx)` — writes the refresh token to a cookie and returns only the access token in the body.
  - `RefreshTokenAsync(ctx, dto, ct)` — reads the refresh token from the cookie (falls back to body for backward compat); rotates and writes a new cookie.
  - `RevokeRefreshTokenAsync(userId, ctx, ct)` — clears the cookie in addition to revoking DB rows.
- [AuthController.cs](server/AtomicHabits/Controllers/AuthController.cs) — passes `HttpContext` into `register/login/refresh/revoke/logout`. `revoke-refresh-token` and `logout` now use `User.GetUserId()`.

Frontend:
- [client-ui/src/utils/tokenUtils.js](client-ui/src/utils/tokenUtils.js) — replaced cookie-based `storeTokens` with an in-memory `_accessToken` module variable. New API: `getAccessToken / setAccessToken / clearAccessToken`. `refreshAccessToken()` posts no body — the cookie travels automatically.
- [client-ui/src/api/axiosInstance.js](client-ui/src/api/axiosInstance.js) — request interceptor reads from memory, not cookies. `withCredentials: true` already present.
- [client-ui/src/context/AuthContext.jsx](client-ui/src/context/AuthContext.jsx) — `login` / `register` store the access token in memory. `restoreSession` calls `refreshAccessToken()` (cookie alone proves identity on reload). `logout` calls `POST /Auth/logout` then clears memory.
- [client-ui/src/context/HabitContext.jsx](client-ui/src/context/HabitContext.jsx), [HabitTrackingContext.jsx](client-ui/src/context/HabitTrackingContext.jsx) — replaced `getCookie('accessToken') && !isTokenExpired(...)` gates with `getAccessToken()` checks.

Cookie scope: `Path=/api/Auth` — sent only to auth endpoints, not to every API call.

Migration note: existing logged-in users will be logged out once on next deploy (their old non-HttpOnly cookies still work for one refresh, after which the server overwrites with the new HttpOnly cookie). [`cookieUtils.js`](client-ui/src/utils/cookieUtils.js) is now unused but kept in place in case it's useful for non-auth cookies later.

Build status: backend **0 errors**, frontend **vite build ✓**.

### 2026-04-28 — Phase 2: Hygiene & consistency

**Configuration & dead code:**
- **#10** Deleted `WeatherForecastController.cs`, `WeatherForecast.cs`, the long commented-out `RefreshTokenAsync`/`StoreRefreshTokenAsync`/`HashToken` blocks in `AuthService`, `using Paket;` and `using Azure;` in service files, and the unused `Models.JwtOptions` (collided with the new `Config.JwtOptions`). Dropped `Paket.Core` package.
- **#21** [`Data/AppDbContext.cs`](server/AtomicHabits/Data/AppDbContext.cs) moved into `namespace AtomicHabits.Data`. Added `using AtomicHabits.Data;` to repos/services and migration designer files.
- **#3 + #5** [`Config/JwtOptions.cs`](server/AtomicHabits/Config/JwtOptions.cs) bound from `Jwt` section with env-var overlay and `ValidateOnStart`. [`Config/AppOptions.cs`](server/AtomicHabits/Config/AppOptions.cs) holds `WebBaseUrl`/`ResetPasswordPath` and `CorsOptions.AllowedOrigins`. [`TokenService`](server/AtomicHabits/Services/TokenService.cs) and [`AuthService`](server/AtomicHabits/Services/AuthService.cs) consume `IOptions<JwtOptions>`/`IOptions<AppOptions>`. Refresh-token TTL now `_jwt.RefreshTokenDays` (default 7).
- **#7** Reset-password URL now built from `App:WebBaseUrl + App:ResetPasswordPath` (default `http://localhost:5173/auth/reset-password`).
- **#8** [`Program.cs`](server/AtomicHabits/Program.cs) reads CORS allow-list from `Cors:AllowedOrigins` array.

**Math correctness:**
- **#11** [`Utils/StreakCalculator.cs`](server/AtomicHabits/Utils/StreakCalculator.cs) — single source of truth for current/longest streak. Used by `DashboardService` and `HabitTrackingService.GetHabitStats`. (`StreakRepositories.UpsertStreakAfterTracking` left untouched — it's a persistence routine; can adopt the calculator later.)
- **#12** [`HabitService.HabitSummary`](server/AtomicHabits/Services/HabitService.cs) uses `StartOfIsoWeek(today)` (Monday-anchored) instead of the off-by-one `today.AddDays(-(int)today.DayOfWeek + 1)`.
- **#17** Same method now sums per-habit `ExpectedSessions(habit, daysElapsed, periodLength)` based on `GoalFrequency` (daily/weekly/monthly/yearly) instead of multiplying habit count by elapsed days. Today rate uses only daily habits as the denominator.

**Persistence:**
- **#23** [`AppDbContext.OnModelCreating`](server/AtomicHabits/Data/AppDbContext.cs) adds:
  - unique `IX_RefreshTokens_TokenHash`
  - `IX_RefreshTokens_UserId_IsRevoked`
  - `IX_HabitTrackings_HabitId_TrackingDate`
  - `IX_HabitTrackings_UserId_TrackingDate`
  - `IX_Habits_UserId_IsArchived`
  - `IX_Users_Email`, `IX_Users_Username` (non-unique on purpose — tighten to unique after a duplicate audit)
  - unique `IX_UserRoles_UserId_RoleId`
  - Migration `AddEntityIndexes` generated. Apply with `dotnet ef database update`.

**Cross-cutting:**
- **#24** [`Middleware/GlobalExceptionMiddleware.cs`](server/AtomicHabits/Middleware/GlobalExceptionMiddleware.cs) returns a standard `ApiResponse` 500 with the exception message in development only. Wired first in the pipeline. Existing services kept their try/catch for now (intentional — avoid churn); new endpoints can rely on the middleware.
- **#25** Added `FluentValidation.AspNetCore` 11.3.0. [`Validators/AuthValidators.cs`](server/AtomicHabits/Validators/AuthValidators.cs) covers `RegisterDto`, `LoginDto`. [`Validators/HabitValidators.cs`](server/AtomicHabits/Validators/HabitValidators.cs) covers `HabitDTO`, `HabitTrackingDTO`. Auto-validation registered via `AddFluentValidationAutoValidation()`.

Build status: backend **0 errors** (84 pre-existing warnings, none introduced).

**Open follow-ups deferred to Phase 3+:**
- *(none — all Phase 1 + Phase 2 leftovers resolved on 2026-04-28 — see next changelog entry)*

### 2026-04-28 — Phase 2 cleanup: all leftovers resolved

- **#19** [`README.md`](README.md) rewritten to match reality: JSX (not TypeScript), Controller→Service→Repository (no UseCase layer), accurate folder tree (Config/, Middleware/, Utils/, Validators/), current auth model (in-memory access token + HttpOnly refresh cookie), configuration block, getting-started instructions, and a link back to this roadmap.
- **#6 leftover** Removed dead `RefreshToken`, `RefreshTokenExpiry`, `IsRevoked` properties from [`Models/RBAC.cs#User`](server/AtomicHabits/Models/RBAC.cs). Dropped the unused `using Microsoft.Identity.Client;` while there. New migration `DropLegacyRefreshTokenColumnsAndUniqueUserIndexes` drops the legacy columns from the `Users` table.
- **#23 leftover** Promoted `Users(Email)` and `Users(Username)` indexes to `IsUnique()` in [`AppDbContext.OnModelCreating`](server/AtomicHabits/Data/AppDbContext.cs). Same migration creates the unique indexes. To avoid a cryptic SQL failure on dirty data, the migration begins with a `THROW`-based guard that aborts with a clear message if duplicates exist:
  > *"Migration aborted: duplicate values in Users.Email prevent the unique index. Resolve duplicates and rerun."*

  This means the migration is idempotent and safe — apply with `dotnet ef database update`. If it aborts, query `SELECT Email, COUNT(*) FROM Users GROUP BY Email HAVING COUNT(*) > 1`, clean up, and rerun.

Build status: backend **0 errors**.

### 2026-04-28 — Phase 3: Wire up half-built features

**Settings persistence (#15) — Batch G**
- New entity [`Models/UserPreferences.cs`](server/AtomicHabits/Models/UserPreferences.cs) with `Notifications`, `DarkMode`, `EmailUpdates`, `DeviceSync`, `PrimaryColor`, `FontFamily`, `BorderRadius`, `Spacing`. Unique index on `UserId`.
- [`Models/DTO/UserPreferencesDto.cs`](server/AtomicHabits/Models/DTO/UserPreferencesDto.cs), [`Services/UserPreferencesService.cs`](server/AtomicHabits/Services/UserPreferencesService.cs) (upsert), [`Controllers/UserPreferencesController.cs`](server/AtomicHabits/Controllers/UserPreferencesController.cs) — `GET /api/UserPreferences`, `PUT /api/UserPreferences`. JWT-derived userId.
- Frontend [`context/UserPreferencesContext.jsx`](client-ui/src/context/UserPreferencesContext.jsx) loads on mount and exposes `prefs` + `savePrefs`. Provider wired in [`App.jsx`](client-ui/src/App.jsx).
- [`Settings.jsx`](client-ui/src/views/settings/Settings.jsx) now reads/writes via the context; toggling a switch persists to the server. 2FA switch is disabled with a "coming soon" hint until #J/#K (TOTP) lands.

**Heatmap endpoint (#14) — Batch H**
- [`DashboardRepositories.GetDailyCompletionCounts`](server/AtomicHabits/Repositories/DashboardRepositories.cs) groups completed trackings by date.
- [`DashboardService.GetHeatmap(userId, days, ct)`](server/AtomicHabits/Services/DashboardService.cs) returns one cell per calendar day for the trailing window, with `count` and an `intensity` bucket (0–4) computed relative to the user's own max.
- `GET /api/Dashboard/heatmap?days=90` (clamped 1–366, default 90).
- [`Dashboard.jsx`](client-ui/src/views/dashboard/Dashboard.jsx) calls the endpoint on mount and adapts `cells` → `{ dayOfMonth → intensity }` for the existing `HabitHeatmapCalendar`. Hardcoded sample data deleted.

**Habit archiving — Batch I**
- [`HabitRepositories.SetArchivedAsync`](server/AtomicHabits/Repositories/HabitRepositories.cs) flips `IsArchived` (ownership-checked). `GetHabitByUserId(userId, includeArchived=false)` filters by default.
- [`HabitService.SetArchivedAsync`](server/AtomicHabits/Services/HabitService.cs).
- [`HabitController`](server/AtomicHabits/Controllers/HabitController.cs): `POST /api/Habit/archive/{habitId}`, `POST /api/Habit/restore/{habitId}`. `GET /api/Habit/get-habits/{userId}?includeArchived=true` to list everything.
- Frontend: [`HabitContext.jsx`](client-ui/src/context/HabitContext.jsx) gains `archiveHabit(id)` / `restoreHabit(id)` (UI buttons left for a follow-up).

**RBAC enforcement — Batch J**
- [`Data/DbSeeder.cs`](server/AtomicHabits/Data/DbSeeder.cs) seeds modules and permissions (`Users.Read`, `Users.Manage`, `Roles.Read`, `Roles.Manage`, `Habits.Read`, `Habits.Write`) and grants them to `Admin` (all) and `User` (`Habits.*` only). Idempotent.
- New [`Authorization/PermissionAuthorization.cs`](server/AtomicHabits/Authorization/PermissionAuthorization.cs):
  - `[Permission("Code")]` attribute (decorates controllers/actions).
  - `PermissionPolicyProvider` builds `perm:CODE` policies on demand — no need to register every permission individually.
  - `PermissionAuthorizationHandler` joins `UserRoles → RolePermissions → Permissions` to check the JWT user has the requested permission. Logs denials.
- `Program.cs` registers the provider (singleton) and handler (scoped).
- Sample admin endpoint [`AdminController.ListUsers`](server/AtomicHabits/Controllers/AdminController.cs) protected by `[Permission("Users.Read")]` — returns user list with their roles.

**Persistence:**
- New migration `AddUserPreferences` creates the `UserPreferences` table + unique index. Apply with `dotnet ef database update`.
- Permission catalog seeds at startup via `DbSeeder.SeedRolesAsync` (now also seeds modules, permissions, and Admin/User role-permission mappings).

Build status: backend **0 errors**, frontend **vite build ✓**.

**To activate Phase 3 on a running deployment:**
1. `dotnet ef database update` (creates `UserPreferences`).
2. Restart the API — `DbSeeder` auto-seeds the permission catalog and Admin/User role grants on first run.
3. Manually promote your own user to Admin (one-time): `INSERT INTO UserRoles (UserId, RoleId) SELECT @userId, Id FROM Roles WHERE Name = 'Admin';`

### 2026-04-28 — Phase 4: Reminders, 2FA, Tags, Pagination, UI affordances

**Habit Reminders + scheduler — Batch K**
- [`Models/HabitReminder.cs`](server/AtomicHabits/Models/HabitReminder.cs) gained `LastFiredOn` (DateOnly) — used to dedupe within the day.
- [`Repositories/HabitReminderRepositories.cs`](server/AtomicHabits/Repositories/HabitReminderRepositories.cs) — CRUD + `GetDueAsync` (5-min backlog) + `MarkFiredAsync`.
- [`Services/HabitReminderService.cs`](server/AtomicHabits/Services/HabitReminderService.cs) — owner-checked CRUD.
- [`Controllers/HabitReminderController.cs`](server/AtomicHabits/Controllers/HabitReminderController.cs) — `GET / POST /api/HabitReminder`, `PUT/DELETE /api/HabitReminder/{id}`.
- [`Scheduling/ReminderDispatcherService.cs`](server/AtomicHabits/Scheduling/ReminderDispatcherService.cs) — `BackgroundService` ticks every minute; honors `DaysOfWeek` (`Mon,Wed,Fri…`) — empty/blank = all days; sends via existing `IEmailSender`; on failure leaves `LastFiredOn` untouched so the next tick retries.
- Index `IX_HabitReminders_IsEnabled_LastFiredOn` to keep the due-query cheap.

**2FA (TOTP) — Batch L**
- New entity [`Models/UserTwoFactor.cs`](server/AtomicHabits/Models/UserTwoFactor.cs) — Base32 secret, `IsEnabled`, audit timestamps. Unique on `UserId`.
- [`Services/TwoFactorService.cs`](server/AtomicHabits/Services/TwoFactorService.cs) using `Otp.NET 1.4.0`. Methods: `StartEnrollmentAsync` (generates secret + `otpauth://` URI for QR), `ConfirmEnrollmentAsync` (verifies first code, sets `IsEnabled=true`, mirrors `User.TwoFactorEnabled`), `DisableAsync`, `IsEnabledAsync`, `VerifyAsync`. ±1 step verification window for clock skew.
- [`Controllers/TwoFactorController.cs`](server/AtomicHabits/Controllers/TwoFactorController.cs) — `enable-init`, `enable-confirm`, `disable`.
- Login flow: [`AuthService.LoginAsync`](server/AtomicHabits/Services/AuthService.cs) detects 2FA-enabled users and returns `{ requiresTwoFactor: true, twoFactorToken }` instead of access tokens. The `twoFactorToken` is a 5-min JWT signed with the same key + a `twofa_pending` claim ([TokenService.GenerateTwoFactorPendingToken](server/AtomicHabits/Services/TokenService.cs)).
- New `POST /api/Auth/verify-2fa` accepts the pending token + TOTP code, then issues real tokens via the existing `IssueTokensAsync`.
- Frontend [`AuthContext.login`](client-ui/src/context/AuthContext.jsx) returns `{ requiresTwoFactor, twoFactorToken } | { user }`. New `verifyTwoFactor(token, code)` finishes the flow. [`AuthLogin.jsx`](client-ui/src/views/authentication/auth/AuthLogin.jsx) renders a code-entry form when challenged.

**Habit Tags — Batch M**
- New entities [`Tag`](server/AtomicHabits/Models/Tag.cs) and `HabitTag` (composite PK `(HabitId, TagId)`, cascade delete on both sides).
- Habit nav `Habit.HabitTags` added.
- Indexes: unique `IX_Tags_UserId_Name`, `IX_HabitTags_TagId`.
- [`Services/TagService.cs`](server/AtomicHabits/Services/TagService.cs) — list/create/update/delete tags; attach/detach to a habit; list tags of a habit. All ownership-checked.
- [`Controllers/TagController.cs`](server/AtomicHabits/Controllers/TagController.cs) — `GET/POST /api/Tag`, `PUT/DELETE /api/Tag/{tagId}`, `GET /api/Tag/habit/{habitId}`, `POST/DELETE /api/Tag/habit/{habitId}/{tagId}`.

**Pagination & filtering — Batch N**
- [`HabitRepositories.SearchAsync`](server/AtomicHabits/Repositories/HabitRepositories.cs): `search` (`LIKE` over name + description), `tagId` filter, `includeArchived`, `page`/`pageSize` (1–100, defaults 1/20). Returns `(items, total)`.
- New endpoint `GET /api/Habit/search?search=&tagId=&includeArchived=&page=&pageSize=`. Existing `get-habits/{userId}` left untouched for back-compat.

**UI affordances — Batch O**
- [`AuthLogin.jsx`](client-ui/src/views/authentication/auth/AuthLogin.jsx) renders a 6-digit code form when login returns `requiresTwoFactor`.
- New [`views/admin/AdminUsers.jsx`](client-ui/src/views/admin/AdminUsers.jsx) — read-only table of users with their roles, calls `GET /api/Admin/users` (gated by `[Permission("Users.Read")]` on the backend; non-admins see a friendly error). Route: `/admin/users`.
- [`HabitMenuButton.jsx`](client-ui/src/views/habit/components/HabitMenuButton.jsx) gains optional `onArchive`/`onRestore` + `isArchived` props, rendering an Archive or Restore menu item. [`Habit.jsx`](client-ui/src/views/habit/Habit.jsx) wires both to the new `archiveHabit`/`restoreHabit` context methods.

**Persistence:**
- Migration `AddRemindersTwoFactorAndTags` creates `UserTwoFactors`, `Tags`, `HabitTags`, adds `LastFiredOn` to `HabitReminders` plus the new indexes. Apply with `dotnet ef database update`.

Build status: backend **0 errors**, frontend **vite build ✓**.

**To activate Phase 4 on a running deployment:**
1. `dotnet ef database update` (creates 2FA, tags, and the reminder index).
2. Restart the API — the reminder scheduler starts as a hosted service.
3. SMTP must be configured for reminders to actually deliver (emails currently rely on the existing `EmailService`).

### 2026-04-28 — Phase 5: UI polish round

Frontend-heavy round — no schema or new capabilities, just exposing what's already in the backend.

**Tag chips + filter — Batch P**
- Backend tweak: [`HabitRepositories`](server/AtomicHabits/Repositories/HabitRepositories.cs) now `.Include(h => h.HabitTags).ThenInclude(ht => ht.Tag)` on both list paths so habit responses carry their tags. Added `[JsonIgnore]` on `Tag.User`, `Tag.HabitTags`, and `HabitTag.Habit` ([Models/Tag.cs](server/AtomicHabits/Models/Tag.cs)) to break the JSON cycle.
- New [`context/TagContext.jsx`](client-ui/src/context/TagContext.jsx) — `tags` list + `createTag` / `deleteTag` / `attachTag` / `detachTag`. Provider mounted in [`App.jsx`](client-ui/src/App.jsx).
- [`Habit.jsx`](client-ui/src/views/habit/Habit.jsx) renders `Chip`s under each habit using `habit.habitTags[].tag`.

**Pagination + filter UI — Batch Q**
- [`HabitContext`](client-ui/src/context/HabitContext.jsx) gains `searchHabits({ search, tagId, includeArchived, page, pageSize })` plus a `pagination` slot. Backed by the existing `/api/Habit/search`.
- [`Habit.jsx`](client-ui/src/views/habit/Habit.jsx) toolbar: search box, tag dropdown, "Show archived" toggle, total count, MUI `Pagination` at the bottom (page size 20). All local-state knobs feed back into `searchHabits` via a single `useEffect`.

**2FA Settings UI — Batch R**
- Added `qrcode.react@4.2.0`.
- New [`GET /api/TwoFactor/status`](server/AtomicHabits/Controllers/TwoFactorController.cs) returns `{ enabled }`.
- New [`views/settings/TwoFactorDialog.jsx`](client-ui/src/views/settings/TwoFactorDialog.jsx) — single dialog handles both enable (renders QR from the `otpauthUri` plus the manual secret) and disable (asks for current code).
- [`Settings.jsx`](client-ui/src/views/settings/Settings.jsx) replaces the disabled "coming soon" 2FA switch with a real one driven by `/TwoFactor/status`. Toggling opens the dialog in the appropriate mode and refreshes status on confirm.

**Reminder UI — Batch S**
- New [`views/habit/components/HabitRemindersDialog.jsx`](client-ui/src/views/habit/components/HabitRemindersDialog.jsx) — lists reminders for a single habit, lets the user add/toggle/delete, picks days-of-week with checkboxes (`Mon..Sun`).
- [`HabitMenuButton.jsx`](client-ui/src/views/habit/components/HabitMenuButton.jsx) gains an optional `onReminders` prop → "Reminders" menu item. Wired in [`Habit.jsx`](client-ui/src/views/habit/Habit.jsx).

**Admin role assignment — Batch T**
- Backend: [`AdminController`](server/AtomicHabits/Controllers/AdminController.cs) gains `GET /roles` (`Roles.Read`), `POST /users/{userId}/roles/{roleName}` and `DELETE …` (`Roles.Manage`). Includes a self-protection guard: an Admin cannot remove their own Admin role.
- Frontend: [`views/admin/AdminUsers.jsx`](client-ui/src/views/admin/AdminUsers.jsx) now shows role chips with delete icons + a `+` icon that opens a menu of assignable roles. Falls back to a read-only view if the user lacks `Roles.Read`.

Build status: backend **0 errors**, frontend **vite build ✓**.

**No migration needed** for Phase 5 — purely additive controllers/UI.

**Deferred to next round:**
- Tag CRUD UI (creating/renaming tags with color picker)
- Sidebar gating (hide `/admin/users` link based on permissions)
- Reminder timezone awareness, 2FA recovery codes, push notifications

### 2026-04-29 — Phase 6: Permissions, theming, TypeScript kickoff

Frontend-heavy round, again no migration needed.

**Permissions claim + sidebar gating — Batch U**
- New `GET /api/Auth/me` ([AuthController.cs](server/AtomicHabits/Controllers/AuthController.cs)) returns `{ id, username, email, avatarUrl, roles, permissions }`. The permissions list is computed from `UserRoles → RolePermissions → Permissions`.
- [`AuthContext`](client-ui/src/context/AuthContext.jsx) calls `/Auth/me` after login, after 2FA verify, and during `restoreSession`. Exposes `permissions`, `roles`, `hasPermission(code)`, `hasRole(name)`.
- New [`components/RequirePermission.jsx`](client-ui/src/components/RequirePermission.jsx) — reusable wrapper to hide UI from users without a permission code or role. UI-only; backend remains authoritative.
- [`SidebarItems.jsx`](client-ui/src/layouts/sidebar/SidebarItems.jsx) now adds an **Admin · Users** entry that filters by `permission: "Users.Read"`.

**Tag CRUD UI — Batch V**
- New [`views/settings/TagManagement.jsx`](client-ui/src/views/settings/TagManagement.jsx) — list of tags as colored chips, create form (name + `react-color` picker), delete via chip. Mounted as a section in the Settings page.

**Dark mode wiring — Batch W**
- [`theme/DefaultColors.js`](client-ui/src/theme/DefaultColors.js) gains `basedarkTheme` (slate background, light text) and `buildAppTheme(mode)`.
- [`App.jsx`](client-ui/src/App.jsx) splits into outer providers (theme, snackbar, auth, prefs) and inner `ThemedRoutes` that reads `prefs.darkMode` from `UserPreferencesContext` and picks the theme accordingly. Toggle in Settings now actually changes the visual theme without a reload.

**TypeScript kickoff — Batch X**
- Added `typescript@^6`, `@types/react`, `@types/react-dom`, `@types/lodash` as devDeps.
- New [`tsconfig.json`](client-ui/tsconfig.json) with `allowJs: true`, `noEmit: true`, `bundler` resolution, `react-jsx`. `strict` deferred — incremental migration mode.
- New `npm run typecheck` script (`tsc --noEmit`).
- Ported leaf utilities to TypeScript: [`utils/cookieUtils.ts`](client-ui/src/utils/cookieUtils.ts) (typed `CookieOptions`) and [`utils/tokenUtils.ts`](client-ui/src/utils/tokenUtils.ts) (`AppJwtPayload`, return types). The original `.js` files are removed; consumers needed no changes thanks to identical exports.
- `npm run typecheck` passes; `npx vite build` passes.

Build status: backend **0 errors**, frontend **vite build ✓**, **typecheck ✓**.

**No EF migration needed** — Phase 6 is purely additive controllers + UI + tooling.

### 2026-04-29 — Phase 7: High-leverage hardening (F + W + P + E)

After production-debugging the forgot-password incident (SMTP misconfig → Network Error → schema drift → FK cycle), four targeted improvements to make those classes of problem visible or impossible:

**Item F — Pending-migrations startup warning**
- [`Program.cs`](server/AtomicHabits/Program.cs) now calls `dbContext.Database.GetPendingMigrationsAsync()` at startup. If any are pending, logs a `Warning` listing them. Otherwise logs a one-line `Information` confirmation. Failure to reach the DB is caught + logged so the API still starts.
- This would have caught the `Invalid object name 'UserTwoFactors'` 500 before any user request hit it.

**Item W — Leaked-secret cleanup + secret-handling strategy**
- Audited git history. Two values are present in public history and must be considered compromised:
  - Gmail App Password from initial-commit `EmailService.cs` — rotate at https://myaccount.google.com/apppasswords.
  - JWT signing key from commit `bb2183f` (`launchSettings.json`) — replaced with a fresh random base64 key stored in user-secrets.
- [`launchSettings.json`](server/AtomicHabits/Properties/launchSettings.json) wiped of `JWT_SECRET`, `SMTP_USERNAME`, `SMTP_PASSWORD`. Now contains only `JWT_ISSUER` / `JWT_AUDIENCE` plus a leading `_comment` directing developers to user-secrets.
- [`Program.cs`](server/AtomicHabits/Program.cs) loads an optional `appsettings.Local.json` last (gitignored), giving developers a non-secret-aware override path.
- [`.gitignore`](.gitignore) expanded — covers `bin/`, `obj/`, `node_modules/`, `dist/`, `appsettings.Local.json`, `.env`, `.env.local`, `secrets.json`. `appsettings.Development.json` stays tracked (shared dev config like log levels).
- New [`SECURITY.md`](SECURITY.md) documents the layered config strategy, what to do if you leak a secret, and the historical leaks already discovered.
- **Manual follow-ups for the developer (not done by code):** revoke the leaked Gmail App Password, optionally rewrite git history with `git filter-repo` to scrub the leaked values from public clones.

**Item P — Docker + Compose**
- [`server/AtomicHabits/Dockerfile`](server/AtomicHabits/Dockerfile) — multi-stage SDK→ASP.NET runtime, runs as non-root user `atomichabits`, exposes 8080.
- [`client-ui/Dockerfile`](client-ui/Dockerfile) — multi-stage Node→nginx production build with [SPA-aware nginx config](client-ui/nginx.conf) (`try_files $uri /index.html`) and aggressive caching for `/assets/`. `VITE_API_URL` baked at build time via `--build-arg`.
- [`client-ui/Dockerfile.dev`](client-ui/Dockerfile.dev) — Vite hot-reload variant with `--host 0.0.0.0` for bind-mount development.
- [`docker-compose.yml`](docker-compose.yml) at repo root — three services (`db`, `api`, `web`), shared bridge network, named volume `db_data` for SQL Server persistence, healthcheck on `db` so `api` waits for SQL Server to be reachable. SQL Server SA password, JWT secret, SMTP credentials all sourced from `.env` (gitignored).
- [`.env.example`](.env.example) tracked as a template; `.env` is in `.gitignore`.
- `docker compose --env-file .env.example config` validates without error.
- Solves: WSL ↔ Windows SQL Server connectivity headaches, "what's running on port X?" confusion, env-var setup ceremony in PowerShell vs bash.

**Item E — Eye toggles on Login + Register**
- New reusable [`PasswordField`](client-ui/src/components/forms/PasswordField.jsx) component — wraps `CustomTextField`, adds a Visibility/VisibilityOff `IconButton` in the input's `endAdornment`. Independent state per field instance, `tabIndex={-1}` so it doesn't disrupt keyboard navigation, `onMouseDown` preventDefault so toggling doesn't steal focus, `aria-label` updates with state.
- Applied in [`AuthLogin.jsx`](client-ui/src/views/authentication/auth/AuthLogin.jsx) and [`AuthRegister.jsx`](client-ui/src/views/authentication/auth/AuthRegister.jsx). The component is a drop-in replacement for `<CustomTextField type="password" ... />`.
- `ResetPassword` already has eye toggles from earlier; we'll consolidate that to use this component in a follow-up TS-migration pass.

Build status: backend **0 errors**, frontend **vite build ✓**, **typecheck ✓**, `docker compose config` ✓.

**No EF migration needed** — Phase 7 is purely tooling, config, and UI.

**Manual follow-ups for the developer:**
1. Revoke the leaked Gmail App Password at https://myaccount.google.com/apppasswords.
2. Optional: `git filter-repo` the historical leaks if the public-repo exposure is a concern.
3. First run with Docker:
   ```bash
   cp .env.example .env   # then edit .env with real values
   docker compose up --build
   docker compose exec api dotnet AtomicHabits.dll  # or just rely on the entrypoint
   # apply migrations once DB is up:
   docker compose exec api sh -c "cd /app && ASPNETCORE_ENVIRONMENT=Development dotnet ef database update"
   ```
   (Migration tooling isn't bundled into the runtime image yet — use the local `dotnet ef` against the compose-exposed DB on port 1433 if you'd rather.)

### 2026-06-07 — 2FA recovery codes (§9 near-term)

Single-use recovery codes as a fallback when an authenticator device is lost. Full lifecycle: issue at enrollment, log in with a code, regenerate from Settings. Design + ADR: [docs/superpowers/specs/2026-06-06-2fa-recovery-codes-design.md](docs/superpowers/specs/2026-06-06-2fa-recovery-codes-design.md), [docs/superpowers/adr/0001-2fa-recovery-codes.md](docs/superpowers/adr/0001-2fa-recovery-codes.md).

Backend:
- New entity [`Models/TwoFactorRecoveryCode.cs`](server/AtomicHabits/Models/TwoFactorRecoveryCode.cs) — one row per code, SHA-256 hash of the normalized code, `IsUsed`/`UsedAt`. Registered in [`AppDbContext`](server/AtomicHabits/Data/AppDbContext.cs) with `IX_TwoFactorRecoveryCodes_UserId`. Migration `AddTwoFactorRecoveryCodes` (applied).
- [`TwoFactorService`](server/AtomicHabits/Services/TwoFactorService.cs): `GenerateRecoveryCodesAsync` (10 codes, Crockford-base32 `XXXX-XXXX-XXXX`, replaces any prior set), `VerifyRecoveryCodeAsync` (race-safe single-use via atomic `ExecuteUpdateAsync ... WHERE !IsUsed`), `CountRemainingRecoveryCodesAsync`. Codes are generated on `ConfirmEnrollmentAsync` and deleted on `DisableAsync`.
- [`TwoFactorController`](server/AtomicHabits/Controllers/TwoFactorController.cs): `POST /api/TwoFactor/recovery-codes/regenerate` (TOTP-gated) and `GET /api/TwoFactor/recovery-codes/count`. `enable-confirm` now returns the codes once.
- Login: `VerifyTwoFactorDto` gains `IsRecoveryCode`; [`AuthService.VerifyTwoFactorAsync`](server/AtomicHabits/Services/AuthService.cs) branches to `VerifyRecoveryCodeAsync` vs TOTP on the explicit flag (no format auto-detection). Recovery path stays gated behind the validated 5-minute pending token.

Frontend:
- New [`RecoveryCodesPanel.jsx`](client-ui/src/views/settings/RecoveryCodesPanel.jsx) — one-time code display with copy/download and an acknowledgement-gated Done.
- [`TwoFactorDialog.jsx`](client-ui/src/views/settings/TwoFactorDialog.jsx) shows codes after enable and gains a `regenerate` mode.
- [`AuthLogin.jsx`](client-ui/src/views/authentication/auth/AuthLogin.jsx) 2FA challenge gains a "use a recovery code instead" toggle; [`AuthContext.verifyTwoFactor`](client-ui/src/context/AuthContext.jsx) forwards the flag.
- [`Settings.jsx`](client-ui/src/views/settings/Settings.jsx) shows "Recovery codes: N remaining" + a Regenerate button when 2FA is enabled.

Verification: backend `dotnet build` **0 errors**, frontend `npm run typecheck` ✓, `vite build` ✓, migration applied to the local DB. **Manual end-to-end (enable → capture → recovery-login → consume → regenerate) has NOT yet been run against the running app** — recommended before relying on it in production (no automated test harness exists in this repo).

Deferred (non-blocking): per-attempt rate-limiting on recovery codes, audit logging, consolidating the double-save in `ConfirmEnrollmentAsync` — see the plan's follow-ups.

### 2026-06-07 — Backend test suite (Platform/DevOps: automated tests)

First automated tests for the backend. New `server/AtomicHabits.Tests` xUnit project (EF InMemory + SQLite + `WebApplicationFactory` + Moq), **24 tests, all green**. Plan: [docs/superpowers/plans/2026-06-07-backend-test-suite.md](docs/superpowers/plans/2026-06-07-backend-test-suite.md).

Coverage:
- **Unit** — [`StreakCalculator`](server/AtomicHabits/Utils/StreakCalculator.cs) (6 cases); [`TwoFactorService`](server/AtomicHabits/Services/TwoFactorService.cs) recovery-code lifecycle (SQLite-backed, since EF InMemory doesn't support `ExecuteUpdateAsync`); [`HabitService.HabitSummary`](server/AtomicHabits/Services/HabitService.cs) per-frequency math.
- **Regression guards** — the two bugs found in last session's manual e2e: 2FA pending-token validation (`sub`→`NameIdentifier`, commit `f561677`) and registration transaction-commit (`bbaffa0`). Both fail if the fix is reverted.
- **Integration (HTTP)** — register→login over a real `WebApplicationFactory` pipeline with SQLite; wrong-password rejection.
- **Security (IDOR)** — user B cannot read user A's habits via a route id (JWT-derived ownership holds).

Two further production fixes surfaced *by writing the tests* (each its own commit):
- `dda4a18` — `ValidateTwoFactorPendingToken` had no `ClockSkew`, so the default 5 min let a 5-min pending token live ~10 min; aligned to `TimeSpan.Zero`.
- `978091b` — `IsDailyHabit`/`ExpectedSessions` used `Contains("day")`, which misses the literal `"daily"` (the model's **default** `GoalFrequency`), silently excluding default habits from the today rate. Fixed via `IsDailyFrequency` + a guard test.

Still uncovered (next): `HabitTrackingService` (duplicate-day, streak upsert), `DashboardService` heatmap buckets, the reminder dispatcher, and the **frontend** (no Vitest/RTL yet).

Run the suite: `dotnet test server/AtomicHabits.sln`.

### 2026-06-07 — HabitTracking test slice (+ streak-reset bug fix)

Extended the backend suite from 24 → **36 tests** over the daily-write path. Plan: [docs/superpowers/plans/2026-06-07-habit-tracking-test-slice.md](docs/superpowers/plans/2026-06-07-habit-tracking-test-slice.md).

- **`StreakRepositories.UpsertStreakAfterTracking`** — 4 tests (first completion, consecutive→2, missed-day breaks current keeps best, completion-rate).
- **`HabitTrackingService`** — duplicate-day rejection (`PostHabitProgress` + `PostDailyHabit` → 409), create-path, habit-not-found 404.
- **Distribution** — weekly day-of-month bucketing (int[4]) and monthly month-index (int[12]), incl. empty-set cases.

**Bug found & fixed by these tests** (commit `2c0718d`): `UpsertStreakAfterTracking` looked up the "last completed tracking" *after* `CreateTracking` had already saved the current day's row, so it always matched today (not yesterday) and reset `CurrentStreak` to 1 on every entry. The **persisted** `Streak.CurrentStreak`/`BestStreak` therefore never advanced past 1 — even though `GetHabitStats` (which recomputes via `StreakCalculator`) displayed the correct value. Fixed by excluding the current day's row (`ht.TrackingDate < date`) from the lookup; guarded by the consecutive-days test.

Still uncovered: `DashboardService` heatmap buckets, the reminder dispatcher, and the frontend.

