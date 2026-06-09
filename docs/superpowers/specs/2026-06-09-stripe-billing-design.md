# Stripe Billing (Plan 7) — Design

> Design spec for replacing the manual `set-plan` upgrade bridge with real Stripe
> subscription billing. Brainstormed 2026-06-09. This is the last piece that turns
> the manual Pro toggle into an actual paywall. Refines §5 (billing subsection) of
> `2026-06-07-momentum-performance-os-v1-design.md`.

## Context & decisions

The app already has the entitlement axis (`User.PlanTier`/`SubscriptionStatus`/
`CurrentPeriodEnd`, the `[RequiresActiveSubscription]` gate, `<RequirePro>`), driven
today by a manual `POST /api/Subscription/set-plan` dev bridge. Stripe replaces that
bridge as the real driver of those fields — the gate and the gated features
(Insights, Weekly Report) need NO change.

Decisions locked during brainstorming:
1. **Hosted Stripe Checkout** (redirect) for subscribing — not embedded Elements.
   Minimal PCI scope; Stripe hosts the card form, SCA, receipts.
2. **Stripe Customer Portal** (hosted) for managing/canceling — no in-app cancel UI.
3. **Core-lifecycle webhooks:** `checkout.session.completed`,
   `customer.subscription.updated`, `customer.subscription.deleted`,
   `invoice.payment_failed`. (Activate / sync / cancel / failed-payment→PastDue.)
4. **Built test-first behind an `IStripeGateway` abstraction** so logic is unit-tested
   with NO Stripe keys; live verification uses Stripe **test-mode** keys + the Stripe CLI.
5. **`set-plan` bridge demoted to Development-only** (kept for dev/test; disabled in
   production so it can't be a prod backdoor to free Pro).

### Non-goals (v1)
- Embedded/custom card UI (Elements).
- Dunning emails / in-app payment-failed notifications (PastDue just revokes access
  via the existing gate; email is a later add, same SMTP caveat as the report email).
- Proration UI, multiple plans/tiers, annual vs monthly choice (one Pro price).
- Invoices/receipts UI (the Customer Portal covers this).
- Tax/VAT handling beyond what Stripe Checkout does automatically.

---

## Section 1 — Schema + config

### Schema (one additive migration)
Add two nullable fields to `User` (`Models/RBAC.cs`) so Stripe events map back to a user:
- `StripeCustomerId` (string?) — set on first checkout; stable per user.
- `StripeSubscriptionId` (string?) — the active subscription id; for portal/cancel
  lookups and webhook matching.

The existing `PlanTier`/`SubscriptionStatus`/`CurrentPeriodEnd` remain the app's
source of truth (the gate reads them); Stripe keeps them in sync. Migration:
`AddStripeIdsToUser` — two nullable columns, additive, no data backfill.

### Config (`Config/StripeOptions.cs`, `IOptions<>` pattern like `JwtOptions`)
```
SecretKey       : string   // sk_test_… (user-secrets / env, never committed)
WebhookSecret   : string   // whsec_…  (Stripe CLI / dashboard endpoint secret)
PriceId         : string   // the Pro recurring price id (price_…)
SuccessUrl      : string   // e.g. {WebBaseUrl}/dashboard?checkout=success
CancelUrl       : string   // e.g. {WebBaseUrl}/dashboard?checkout=cancel
PortalReturnUrl : string   // e.g. {WebBaseUrl}/dashboard
```
- Bound from a `Stripe` config section + env/user-secrets overlay; documented in
  `SECURITY.md` alongside the JWT/SMTP secret guidance. NOT committed.
- Add the `Stripe.net` NuGet package to `AtomicHabits.csproj`.
- `StripeConfiguration.ApiKey` set from `SecretKey` at startup (Program.cs).

### `set-plan` demotion
Keep `SubscriptionController.set-plan` but guard it to Development only — e.g. wrap the
body in an environment check (`IWebHostEnvironment.IsDevelopment()`) returning 404/403
in non-Development, OR conditionally map the controller only in Development. Pick one;
the spec requires it is NOT usable in production.

---

## Section 2 — `BillingController` (three endpoints) + webhook sync

All Stripe SDK calls go through `IStripeGateway` (see §3). `BillingService` orchestrates;
controller is thin. `ApiResponse` envelope + owner-scoping conventions like other controllers.

### a) `POST /api/Billing/create-checkout-session` `[Authorize]`
- Resolve user via `User.GetUserId()`.
- If `user.StripeCustomerId` is null: create a Stripe Customer (email = user.Email),
  store the id on the user.
- Create a Checkout Session: `mode=subscription`, `customer=StripeCustomerId`,
  `line_items=[{ price: PriceId, quantity: 1 }]`, `success_url`/`cancel_url` from config.
- Return `{ url }`; frontend redirects the browser there. Replaces the manual upgrade.

### b) `POST /api/Billing/create-portal-session` `[Authorize]`
- Requires `user.StripeCustomerId` (404/400 if absent — not yet a customer).
- Create a Billing Portal session for that customer with `return_url = PortalReturnUrl`.
- Return `{ url }`; frontend redirects. User cancels/updates card/views invoices there.

### c) `POST /api/Billing/webhook` `[AllowAnonymous]`
- Read the raw request body + `Stripe-Signature` header; verify against
  `WebhookSecret` (reject with 400 on failure — security-critical; forged events
  must not mutate state).
- Dispatch on `event.Type` and sync entitlement. **User lookup key:** match by
  `StripeCustomerId` for ALL events (every Stripe subscription/invoice/checkout event
  carries the `customer` id). Do NOT rely on `StripeSubscriptionId` for lookup — on
  `checkout.session.completed` it isn't stored yet. (`StripeSubscriptionId` is stored
  *from* that event and used only for portal/cancel API calls, not webhook matching.)
  If no user matches the customer id, log + return 200 (ignore — not our customer):
  - **`checkout.session.completed`** → store `StripeSubscriptionId` from the session;
    set `PlanTier=Pro`, `SubscriptionStatus=Active`, `CurrentPeriodEnd` from the subscription.
  - **`customer.subscription.updated`** → map Stripe status → our enum
    (`active`→Active, `past_due`/`unpaid`→PastDue, `canceled`→Canceled); update
    `CurrentPeriodEnd`. PlanTier stays Pro while Active/PastDue, Free when Canceled.
  - **`customer.subscription.deleted`** → `PlanTier=Free`, `SubscriptionStatus=Canceled`,
    clear `StripeSubscriptionId`, `CurrentPeriodEnd=null`.
  - **`invoice.payment_failed`** → `SubscriptionStatus=PastDue` (the gate revokes Pro
    access since only Active grants it).
- **Idempotency:** every handler sets state to a deterministic value (no increments),
  so Stripe's retries/duplicate deliveries are safe. Unknown event types → 200 ignore.
- Return 200 on success so Stripe stops retrying.

The `[RequiresActiveSubscription]` gate is unchanged — it reads `PlanTier`/
`SubscriptionStatus`, now driven by these webhooks. Insights + Weekly Report become
real-paywalled with zero change to them.

---

## Section 3 — Testability, frontend wiring, live verification

### IStripeGateway abstraction (enables key-free unit tests)
```
interface IStripeGateway
{
    Task<string> EnsureCustomerAsync(string? existingCustomerId, string email, CancellationToken ct);
    Task<string> CreateCheckoutSessionUrlAsync(string customerId, string priceId, string successUrl, string cancelUrl, CancellationToken ct);
    Task<string> CreatePortalSessionUrlAsync(string customerId, string returnUrl, CancellationToken ct);
    // Webhook signature verification + event construction is in a separate seam
    // (see below) so handler logic is testable without a real signature.
}
```
- Real `StripeGateway` wraps `Stripe.net` (`CustomerService`, `SessionService`,
  Billing Portal `SessionService`). `BillingService` depends on the interface.
- **Webhook:** the signature-verify/parse step is isolated (a small
  `IStripeEventReader.Read(rawBody, signature)` returning a parsed event, with the
  real impl calling `EventUtility.ConstructEvent`). The **handler** (`HandleEventAsync(event)`)
  is pure logic over a parsed event + the DbContext — fully unit-testable with sample
  event objects, no keys, no signature.

### Tests
- **Webhook handler (highest value):** for each of the 4 events, build a sample parsed
  event and assert the user's `PlanTier`/`SubscriptionStatus`/`CurrentPeriodEnd`/
  `StripeSubscriptionId` sync correctly; assert user-lookup-by-customer-id; assert
  idempotency (same event applied twice → same end state); assert an unknown event type
  is ignored (no throw).
- **Signature rejection:** a bad/missing signature → reader rejects → 400, no mutation.
- **BillingService with a fake `IStripeGateway`:** create-checkout persists a new
  `StripeCustomerId` when absent and reuses it when present; create-portal requires a
  customer id (error path when absent). Owner-scoped.
- **set-plan demotion:** (if feasible to test) non-Development → not usable.

### Frontend wiring (minimal)
- Shared helper `startCheckout()`: `POST /api/Billing/create-checkout-session` →
  `window.location.href = res.data.result.url`. Both `DashboardInsights` and
  `DashboardWeeklyReport` "Upgrade to Pro" buttons call it (replacing the `set-plan` POST).
- "Manage subscription" button (shown when `isPro`, e.g. on the Settings page and/or
  the Pro cards): `POST /api/Billing/create-portal-session` → redirect to the url.
- On return from Stripe (`success_url` → `/dashboard?checkout=success`): call
  `refreshMe()` so `isPro` updates. Note the webhook is the source of truth and may
  land a beat after redirect; v1 simply refreshes on return (acceptable — if the
  webhook hasn't landed yet, a manual refresh or the next `/Auth/me` catches up). Keep
  it simple; no polling loop in v1.

### Live verification (manual; the only step needing keys)
Runbook (documented in the plan):
1. Create a free Stripe account; in **test mode**, create a Product + recurring Price;
   copy the `price_…` id and the `sk_test_…` secret key.
2. Put `SecretKey`/`PriceId` in user-secrets; set the URLs in config.
3. `stripe login` + `stripe listen --forward-to localhost:5198/api/Billing/webhook` —
   copy the `whsec_…` it prints into `WebhookSecret`.
4. Run the app, click "Upgrade to Pro", complete checkout with test card
   `4242 4242 4242 4242` (any future expiry/CVC), confirm the webhook flips the user to
   Pro and the gated cards unlock. Test cancel via the Portal; confirm downgrade.

---

## Affected files (anticipated — plan confirms exact paths)
- Backend create: `Config/StripeOptions.cs`, `Services/IStripeGateway.cs` +
  `StripeGateway.cs`, `Services/IStripeEventReader.cs` + impl, `Services/BillingService.cs`,
  `Controllers/BillingController.cs`, DTOs as needed, migration `AddStripeIdsToUser`.
- Backend modify: `Models/RBAC.cs` (two fields), `Program.cs` (DI + StripeConfiguration +
  config binding), `Controllers/SubscriptionController.cs` (Development-only guard),
  `AtomicHabits.csproj` (Stripe.net), `SECURITY.md` (secret docs).
- Frontend modify: `DashboardInsights.jsx` + `DashboardWeeklyReport.jsx` (upgrade →
  startCheckout), a shared checkout helper, a "Manage subscription" button where `isPro`.
- Tests: webhook-handler tests, BillingService tests (fake gateway), signature-reject test.

## Subsequent / optional
- Dunning emails on PastDue (needs SMTP).
- The deferred Weekly Report Sunday auto-email (separate plan).
