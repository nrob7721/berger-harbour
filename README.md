# Berger Houseboats — house boat booking system

Online booking and deposit payment for Berger Houseboats' 14 house boats, embedded in the WordPress site, plus an
internal admin app for bookings, fleet, customers and settings. The full specification is in [`prompt.md`](prompt.md);
deployment is in [`docs/setup-checklist.md`](docs/setup-checklist.md).

```
WordPress boat page ── "Book Now" ──► bookings.example.com.au (Cloudflare Pages, /api/* → Pages Function proxy)
                                                    │
                                     Cloud Run public-api ◄── Stripe webhooks
                                                    │
                                                Firestore ◄── Cloud Run admin-api ◄── Cloud Scheduler (OIDC, hourly)
                                                                    ▲
                     berger-admin.pages.dev (Cloudflare Pages behind Cloudflare Access, /api/* proxy)
```

## Repository

| Path | What |
|---|---|
| `src/BergerHarbour.Domain` | Aggregates (`Boat`, `Booking`, `BookingDraft`, `Customer`, `BoatUnavailability`, `Season`, `BlockedPeriod`, `AddonDefinition`, `EmailTemplate`, `BusinessSettings`), value objects (incl. E.164 `PhoneNumber`), domain services (`BookingAvailabilityService`, `PricingService`, `PaymentScheduleService`, `AddonCapacityService`, `StayPeriodFactory`) and repository interfaces next to each aggregate. |
| `src/BergerHarbour.Application` | Use cases, the booking validation strategies (`OnlineBookingValidationStrategy`, `InternalBookingValidationStrategy`), notifications and the hourly job, seed, and ports (`IBoatScheduleLock`, `IPaymentGateway`, `IEmailSender`, `IClock`, `ITurnstileVerifier`, `IBookingReferenceGenerator`). |
| `src/BergerHarbour.Infrastructure` | Firestore repositories and the per-boat lock (`Firestore/`), Stripe, Gmail SMTP, Turnstile, Cloudflare Access JWT and Google OIDC validation, shared web hosting conventions. |
| `src/BergerHarbour.PublicApi` / `AdminApi` | ASP.NET Core controllers. `dotnet BergerHarbour.AdminApi.dll seed` runs the idempotent seed. |
| `tests/` | Domain and application unit tests; integration tests against the Firestore emulator. |
| `web/` | npm workspace: `shared` (generated API types, date/period logic, phone input, tokens), `booking` and `admin` Vite apps (TypeScript, no UI framework) with their Pages Functions. |
| `wordpress/book-now-block.html` | The HTML block pasted into each boat page. |
| `firestore.indexes.json`, `.github/workflows/` | Composite indexes; CI (PR), deploy to dev (`main`) and promotion to production (GitHub Release). |

## Local development

Requirements: .NET 10 SDK, Node 22+, Java 21+ and `npm install -g firebase-tools`.

```bash
./scripts/dev.sh
```

starts the Firestore emulator, both APIs (admin-api seeds on startup), and both Vite dev servers:

- Booking app: <http://localhost:5173/?boat=pacific-blue>
- Pay page: <http://localhost:5173/pay/?token=…>
- Admin app: <http://localhost:5174> (Cloudflare Access is bypassed only when `ASPNETCORE_ENVIRONMENT=Local`)
- Emails are written as HTML files to `.dev-emails/` instead of being sent.
- Turnstile uses Cloudflare's always-pass test keys.
- Payments: set `STRIPE_SECRET_KEY`/`STRIPE_WEBHOOK_SECRET` (test mode) for public-api and
  `VITE_STRIPE_PUBLISHABLE_KEY` for the booking app, then `stripe listen --forward-to localhost:5080/api/stripe/webhook`.
- Shorten notification timing for manual testing with e.g. `Notifications__BalanceDueBeforeHire=00:30:00` or
  `Booking__AbandonedDraftAfter=00:45:00` (all offsets are `TimeSpan` strings, see `prompt.md` §10.2). Trigger the job with
  `curl -X POST localhost:5081/api/jobs/process-notifications` (OIDC is bypassed in Local).

## Tests

```bash
dotnet test tests/BergerHarbour.Domain.Tests
dotnet test tests/BergerHarbour.Application.Tests
firebase emulators:exec --only firestore --project demo-berger-harbour \
  "dotnet test tests/BergerHarbour.Infrastructure.IntegrationTests"
cd web && npm test && npm run typecheck && npm run lint
```

## API types

Building the APIs writes `web/shared/openapi/{public,admin}-api.json`; `npm run gen:api` (part of `npm run build`)
turns them into `web/shared/src/api/*.d.ts` with `openapi-typescript`. Both are committed; CI fails if they are stale.

## Environments and deployment

| `ASPNETCORE_ENVIRONMENT` | Where | Settings file |
|---|---|---|
| `Local` | a developer machine (`scripts/dev.sh`, `launchSettings.json`, integration tests), against the emulator | `appsettings.Local.json` |
| `Development` | the deployed dev GCP project, `berger-booking-dev.pages.dev` / `berger-admin-dev.pages.dev` | `appsettings.Development.json` |
| `Production` | the prod GCP project, the booking domain / `berger-admin.pages.dev` | `appsettings.Production.json` |

Only `Local` bypasses Cloudflare Access, the edge proxy secret and the Scheduler OIDC check, seeds on startup, and
writes emails to disk. The services refuse to start if `DEV_ADMIN_EMAIL` or `DEV_JOBS_BYPASS` is set anywhere else.
The Dockerfiles default to `Production`; each deploy sets the environment explicitly.

Every push to `main` builds both images once (tagged with the commit SHA, in the shared Artifact Registry in the prod
project) and deploys them to dev (`deploy.yml`). Publishing a GitHub Release promotes that commit's images to
production after approval (`release.yml`); it fails if the commit was never deployed to dev. Both use
`deploy-environment.yml` with the `dev` / `production` GitHub Environment's variables. The frontends are rebuilt per
environment because Vite inlines the publishable keys.

## Configuration

Non-secret settings that differ per environment (`PUBLIC_BOOKING_BASE_URL`, `CORS_ALLOWED_ORIGIN`, `EMAIL_SENDER`,
`EMAIL_SENDER_NAME`, `EMAIL_SUBJECT_PREFIX`) live in `appsettings.{Environment}.json`; `PUBLIC_BOOKING_BASE_URL` and
`CORS_ALLOWED_ORIGIN` have no default and are required at startup. Timing comes from `Notifications__*` and
`Booking__*` (validated on startup). Secrets are flat environment variables injected from Secret Manager on Cloud Run,
and infrastructure IDs are passed by the deploy. Any key can be overridden with an environment variable:

| Variable | Service | Notes |
|---|---|---|
| `FIRESTORE_PROJECT_ID` | both | `FIRESTORE_EMULATOR_HOST` switches to the emulator |
| `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET` | public | |
| `EMAIL_SENDER`, `EMAIL_SENDER_NAME`, `EMAIL_SUBJECT_PREFIX`, `SMTP_PASSWORD` | both | Gmail SMTP login, From/Reply-To address (`info@bergerhouseboats.com.au`), display name (default "Berger Houseboats") and its Google app password. `EMAIL_SUBJECT_PREFIX` (`[DEV] ` in dev) is prepended to every subject. `EMAIL_MODE=Smtp` is the default outside Local; `EMAIL_MODE=File` + `EMAIL_OUTPUT_DIR` writes emails to disk instead (the Local default) |
| `TURNSTILE_SECRET` | public | |
| `EDGE_PROXY_SECRET` | both | required on every route except the webhook and jobs endpoint; each service has its own value (`edge-proxy-secret-public` / `-admin`) |
| `PAYMENT_LINK_SECRET` | both | derives each booking's stable payment-link token; never rotate after go-live |
| `PUBLIC_BOOKING_BASE_URL` | both | base of payment links (required) |
| `CORS_ALLOWED_ORIGIN` | both | the service's own frontend origin (required) |
| `CF_ACCESS_TEAM_DOMAIN`, `CF_ACCESS_AUD`, `ADMIN_ALLOWED_EMAILS` | admin | |
| `SCHEDULER_SERVICE_ACCOUNT_EMAIL`, `SCHEDULER_AUDIENCE` | admin | OIDC check for `/api/jobs/process-notifications` |

The frontends need `VITE_STRIPE_PUBLISHABLE_KEY` and `VITE_TURNSTILE_SITE_KEY` at build time.
