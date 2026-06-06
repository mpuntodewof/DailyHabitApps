# Security & Secrets

## TL;DR

**Never commit secrets.** Use `dotnet user-secrets` for development, environment variables for CI/production.

## Where secrets live

| Layer | Where | When loaded |
|-------|-------|-------------|
| `appsettings.json` | Tracked. Defaults and non-secret structure (issuer, audience, port, etc.) | Always |
| `appsettings.Development.json` | Tracked. Shared dev config (log levels, etc.) | Development only |
| `appsettings.Local.json` | **Gitignored.** Per-developer non-secret overrides | Development only |
| `dotnet user-secrets` | Per-user, outside the repo. **Stores actual secrets.** | Development only |
| Environment variables | Set by your shell or CI | Always; overrides everything above |

Configuration loading order (last wins): `appsettings.json` → `appsettings.{Env}.json` → `appsettings.Local.json` → user-secrets → env vars.

## Required secrets

| Key | Where to set it | Notes |
|-----|----------------|-------|
| `Jwt:Secret` | user-secrets (dev) / `JWT_SECRET` env var (prod) | ≥64 random bytes, base64 |
| `Jwt:Issuer`, `Jwt:Audience` | `appsettings.json` is fine | Not secret |
| `Smtp:Username` | user-secrets (dev) / `SMTP_USERNAME` env var (prod) | The Gmail address |
| `Smtp:Password` | user-secrets (dev) / `SMTP_PASSWORD` env var (prod) | Gmail **App Password** (not your account password). 2-Step Verification must be on. |

## First-time setup (developer)

```bash
cd server/AtomicHabits

# JWT signing key — generate one and store it
openssl rand -base64 64 | tr -d '\n' > /tmp/jwt && \
  dotnet user-secrets set "Jwt:Secret" "$(cat /tmp/jwt)" && \
  rm /tmp/jwt

# SMTP credentials
dotnet user-secrets set "Smtp:Username" "you@example.com"
dotnet user-secrets set "Smtp:Password" "your-app-password-no-spaces"

# Verify
dotnet user-secrets list
```

## If you've leaked a secret

1. **Rotate immediately.** The leaked value is dead the moment you do this.
   - Gmail App Password → https://myaccount.google.com/apppasswords (revoke + create new)
   - JWT signing key → generate a new one as above. (All existing access tokens become invalid; users must log in again.)
2. **Stop tracking the file** if it's source-controlled.
3. **Purge from git history** if the repo is public:
   ```bash
   # Install git-filter-repo: https://github.com/newren/git-filter-repo
   git filter-repo --replace-text <(echo "leaked_secret_value==>REDACTED")
   git push --force --all
   git push --force --tags
   ```
   This rewrites history and requires force-push. Coordinate with everyone who has the repo cloned.

## Known historical leaks (already rotated)

| Value | Where | Status |
|-------|-------|--------|
| Gmail App Password `ptbr ekhv xjdx pxwo` | `EmailService.cs` (Initial commit) | ✅ Code rewritten; rotate the App Password at Google before relying on this. |
| JWT_SECRET `qZRFgZrb…` | `launchSettings.json` (commit `bb2183f`) | ✅ Removed from working copy; replaced with a fresh secret in user-secrets. **Old value should be considered public.** |

## Pre-commit protection (recommended)

Install [`gitleaks`](https://github.com/gitleaks/gitleaks) and add it as a pre-commit hook to catch secrets before they reach a commit:

```bash
gitleaks protect --staged
```
