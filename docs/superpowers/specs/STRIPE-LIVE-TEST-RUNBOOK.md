# Stripe Billing — Live Test Runbook

> The Stripe integration (Plan 7) is fully built and unit-tested (106 tests, no keys
> needed). This runbook is the ONE part that needs your real Stripe **test-mode**
> keys: verifying the end-to-end flow against actual Stripe. Test mode = no real
> money, free, instant. Follow top to bottom.

## What you're verifying
Free user → Upgrade → Stripe Checkout (test card) → webhook flips them to Pro →
Pro cards unlock → Manage subscription → cancel → webhook downgrades them.

---

## 1. Stripe test-mode setup (one-time, ~10 min)

1. Create a free Stripe account at https://stripe.com (or log in). **Stay in TEST MODE** — the dashboard has a "Test mode" toggle (top-right); keep it ON. Test-mode keys start with `sk_test_…`.
2. **Create a Product + Price:**
   - Dashboard → Product catalog → **Add product**.
   - Name: "Momentum Pro". Add a **recurring** price (e.g. $5.00 / month).
   - Save, then copy the **Price ID** (looks like `price_1A2b3C…`).
3. **Get your secret key:** Developers → API keys → copy the **Secret key** (`sk_test_…`).
4. **Install the Stripe CLI** (for forwarding webhooks to localhost): https://stripe.com/docs/stripe-cli — then `stripe login`.

## 2. Configure the app (user-secrets — never commit these)

From `server/AtomicHabits/`:
```
dotnet user-secrets set "Stripe:SecretKey" "sk_test_YOUR_KEY"
dotnet user-secrets set "Stripe:PriceId"  "price_YOUR_PRICE_ID"
```
(`appsettings.json` already has empty `Stripe:SecretKey`/`WebhookSecret`/`PriceId` placeholders + the localhost URLs. User-secrets override them. If user-secrets isn't initialized: `dotnet user-secrets init` first.)

The `WebhookSecret` comes from the CLI in the next step.

## 3. Start everything (three terminals)

**Terminal A — API:** from `server/AtomicHabits/` → `dotnet run` (listens on http://localhost:5198).

**Terminal B — Stripe CLI webhook forwarder:**
```
stripe listen --forward-to localhost:5198/api/Billing/webhook
```
It prints a webhook signing secret: `whsec_…`. **Copy it**, then set it (Terminal, from `server/AtomicHabits/`):
```
dotnet user-secrets set "Stripe:WebhookSecret" "whsec_YOUR_SECRET"
```
…and **restart the API** (Terminal A) so it picks up the secret. Keep `stripe listen` running.

**Terminal C — client:** from `client-ui/` → `npm run dev` → open the Vite URL.

## 4. The end-to-end test

1. **Log in** (or register a fresh account — new accounts are Free by default).
2. **Confirm Free state:** on the Dashboard, the **Insights** and **This Week** cards show the locked "Upgrade to Pro" panel. ✅ (entitlement gate working)
3. **Upgrade:** click **"Upgrade to Pro"** → you're redirected to Stripe's hosted Checkout page.
4. **Pay with a test card:** card number `4242 4242 4242 4242`, any future expiry (e.g. `12/34`), any 3-digit CVC, any ZIP. Submit.
5. **Watch the webhook fire:** Terminal B (`stripe listen`) logs `checkout.session.completed` → forwarded → `200`. The API log shows the user synced to Pro.
6. **Back in the app:** you're redirected to `/dashboard?checkout=success`; the page calls `refreshMe()`. The **Insights** and **This Week** cards should now show their Pro content. ✅ (webhook → entitlement → UI flip)
7. **Manage / cancel:** click **"Manage subscription"** (on the This Week card) → redirected to Stripe's Customer Portal → **Cancel plan**.
8. **Watch downgrade:** Terminal B logs `customer.subscription.updated`/`deleted` → `200`. Back in the app, refresh the Dashboard → the Pro cards are locked again. ✅ (cancel → downgrade)

## 5. Optional checks
- **Per-user isolation:** with account A upgraded to Pro, log in as a different account B (still Free) → confirm B sees the locked cards. (Entitlement is per-user.)
- **Backend gate is real:** while Free, in browser devtools, a direct call to `/api/Insight` or `/api/Report/weekly` returns **403** (not just a UI hide).
- **Failed payment:** Stripe test card `4000 0000 0000 0341` (attaches a card that fails on the *next* charge) — simulates a renewal failure → `invoice.payment_failed` → user goes PastDue → loses Pro access. (Advanced; optional.)

## What "pass" looks like
Free → locked · Upgrade → Stripe Checkout → test card → Pro cards unlock · Manage → cancel → downgrade. If that chain works, Stripe billing is verified end-to-end.

## Notes
- **All in test mode** — no real charges. Test cards: https://stripe.com/docs/testing
- The manual `POST /api/Subscription/set-plan` still works in **Development** as a shortcut to flip Pro without Checkout (returns 404 in production). Stripe is the real path.
- `stripe listen` only needs to run during local testing. In a real deploy, you'd register the webhook endpoint URL in the Stripe dashboard and use that endpoint's signing secret instead.
- If the cards don't flip after checkout: check Terminal B shows `200` (not 400 — a 400 means the `WebhookSecret` doesn't match; re-copy it from `stripe listen` and restart the API).
