# Launch Runbook — interest validation

Goal: find out if strangers want this **before** building billing/entitlement code.
Total effort: ~15 minutes of your time. Three steps you must do (they need your logins/voice).

---

## Step 1 — Email capture (2 min)
1. Go to https://formspree.io and sign up (free).
2. Create a new form. Copy its endpoint URL (looks like `https://formspree.io/f/abcd1234`).
3. Open `landing/index.html`, find `NOTIFY_ENDPOINT`, paste the URL between the quotes.
4. Save.

> Leave `STRIPE_PAYMENT_LINK` empty — you're measuring *interest* first. The "$29" buttons
> automatically fall back to the email form. Don't take money yet.

## Step 2 — Host it free (5 min)
- Easiest: drag the **`landing`** folder onto https://pages.cloudflare.com (or https://app.netlify.com/drop).
- You get a public HTTPS URL instantly. That's your link to share.

## Step 3 — Post it (8 min)
Use the copy below. Post to **r/getdisciplined** and **Indie Hackers** first. Don't spam —
post once, genuinely, reply to comments. Optionally Show HN later.

---

## What "success" looks like
- Watch Formspree for signups + watch click-through on the page.
- Rough read:
  - A handful of organic signups from one honest post = real interest -> build Phase 1.
  - Crickets across 2-3 honest posts = the cheapest possible "don't build it yet" lesson.

---

# COPY TO PASTE

## Reddit — r/getdisciplined
**Title:** I got tired of habit apps that nag me, so I built a calm one. Would love brutal feedback.

**Body:**
I've used a bunch of habit trackers and bounced off all of them — they either gamify everything
into anxiety or bury the one thing I care about (did I show up today?) under noise.

So I built the opposite: a quiet tracker built on the "1% better" idea. One-tap daily check-ins,
a year-view heatmap that fills in as you go, real streak/completion stats, and gentle per-habit
reminders only on the days you pick. No ads, no data selling, and you can export everything anytime.

I'm trying to figure out if anyone besides me actually wants this before I build it out, so I put
up a one-page explainer: [YOUR LINK]

Honest question for this sub: what made habit apps *stick* for you, vs. the ones you dropped?
Tear the idea apart if it deserves it.

---

## Indie Hackers
**Title:** Validating before building: a one-time-purchase habit tracker (no subscription)

**Body:**
I have a working habit-tracker (full-stack, auth/2FA/analytics already built as a side project)
and I'm deciding whether to monetize it. Instead of bolting on Stripe and hoping, I'm doing the
boring-but-correct thing first: a landing page + interest test before writing any billing code.

The bet: a *one-time* $29 purchase, not a subscription — because nobody wants a monthly bill for
a habit tracker, and the category has brutal churn.

Landing page: [YOUR LINK]

Two things I'd genuinely value feedback on:
1. Does "pay once, yours forever" read as a feature or as "this'll be abandoned"?
2. For a crowded category with no audience, is a landing-page test even the right first move,
   or would you validate differently?

---

## Show HN (optional, later)
**Title:** Show HN: A calm, one-time-purchase habit tracker (validating before I build billing)

**Body:**
A habit tracker built on the 1%-better idea — one-tap check-ins, a year heatmap, real stats,
gentle reminders. One-time $29, no subscription, full data export. I'm testing interest before
building the paid tier. Landing page: [YOUR LINK]. Feedback welcome, especially on the
one-time-vs-subscription call and the free/Pro split.
