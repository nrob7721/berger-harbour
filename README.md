# Berger Houseboats — house boat booking system

Online booking and deposit payment for Berger Houseboats' 14 house boats, embedded in the WordPress site, plus an
internal admin app for bookings, fleet, customers and settings. The full specification is in [`prompt.md`](prompt.md);
deployment is in [`docs/setup-checklist.md`](docs/setup-checklist.md).

```
WordPress boat page ── "Book Now" ──► book.bergerhouseboats.com.au (Cloudflare Pages, /api/* → Pages Function proxy)
                                                    │
                                     Cloud Run public-api ◄── Stripe webhooks
                                                    │
                                                Firestore ◄── Cloud Run admin-api ◄── Cloud Scheduler (OIDC, hourly)
                                                                    ▲
                     admin.bergerhouseboats.com.au (Cloudflare Pages behind Cloudflare Access, /api/* proxy)
```

## Repository

| Path | What |
|---|---|
| `src/BergerHarbour.Domain` | Aggregates (`Boat`, `Booking`, `Customer`, `BoatUnavailability`, `Season`, `BlockedPeriod`, `AddonDefinition`, `EmailTemplate`, `BusinessSettings`), value objects, domain services (`BookingAvailabilityService`, `PricingService`, `PaymentScheduleService`, `StayPeriodFactory`) and repository interfaces next to each aggregate. |
| `src/BergerHarbour.Application` | Use cases, the booking validation strategies (`OnlineBookingValidationStrategy`, `InternalBookingValidationStrategy`), notifications and the hourly job, seed, and ports (`IBoatScheduleLock`, `IPaymentGateway`, `IEmailSender`, `IClock`, `ITurnstileVerifier`, `IBookingReferenceGenerator`). |
| `src/BergerHarbour.Infrastructure` | Firestore repositories and the per-boat lock (`Firestore/`), Stripe, Brevo, Turnstile, Cloudflare Access JWT and Google OIDC validation, shared web hosting conventions. |
| `src/BergerHarbour.PublicApi` / `AdminApi` | ASP.NET Core controllers. `dotnet BergerHarbour.AdminApi.dll seed` runs the idempotent seed. |
| `tests/` | Domain and application unit tests; integration tests against the Firestore emulator. |
| `web/` | npm workspace: `shared` (generated API types, date/period logic, tokens), `booking` and `admin` Vite apps (TypeScript, no UI framework) with their Pages Functions. |
| `wordpress/book-now-block.html` | The HTML block pasted into each boat page. |
| `firestore.indexes.json`, `.github/workflows/` | Composite indexes; CI (PR) and deploy (main). |

## Local development

Requirements: .NET 10 SDK, Node 22+, Java 21+ and `npm install -g firebase-tools`.

```bash
./scripts/dev.sh
```

starts the Firestore emulator, both APIs (admin-api seeds on startup), and both Vite dev servers:

- Booking app: <http://localhost:5173/?boat=pacific-blue>
- Pay page: <http://localhost:5173/pay/?token=…>
- Admin app: <http://localhost:5174> (Cloudflare Access is bypassed only when `ASPNETCORE_ENVIRONMENT=Development`)
- Emails are written as HTML files to `.dev-emails/` instead of being sent.
- Turnstile uses Cloudflare's always-pass test keys.
- Payments: set `STRIPE_SECRET_KEY`/`STRIPE_WEBHOOK_SECRET` (test mode) for public-api and
  `VITE_STRIPE_PUBLISHABLE_KEY` for the booking app, then `stripe listen --forward-to localhost:5080/api/stripe/webhook`.
- Shorten notification timing for manual testing with e.g. `Notifications__BalanceDueBeforeHire=00:30:00`
  (all offsets are `TimeSpan` strings, see `prompt.md` §10.2). Trigger the job with
  `curl -X POST localhost:5081/api/jobs/process-notifications` (OIDC is bypassed in Development).

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

## Configuration

Timing comes from `Notifications__*` and `Booking__*` environment variables (validated on startup). Secrets and
other settings are flat environment variables injected from Secret Manager on Cloud Run:

| Variable | Service | Notes |
|---|---|---|
| `FIRESTORE_PROJECT_ID` | both | `FIRESTORE_EMULATOR_HOST` switches to the emulator |
| `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET` | public | |
| `BREVO_API_KEY` | both | `EMAIL_MODE=File` + `EMAIL_OUTPUT_DIR` writes emails to disk instead |
| `TURNSTILE_SECRET` | public | |
| `EDGE_PROXY_SECRET` | both | required on every route except the webhook and jobs endpoint |
| `PAYMENT_LINK_SECRET` | both | derives each booking's stable payment-link token; never rotate after go-live |
| `PUBLIC_BOOKING_BASE_URL` | both | base of payment links |
| `CORS_ALLOWED_ORIGIN` | both | the service's own frontend origin |
| `CF_ACCESS_TEAM_DOMAIN`, `CF_ACCESS_AUD`, `ADMIN_ALLOWED_EMAILS` | admin | |
| `SCHEDULER_SERVICE_ACCOUNT_EMAIL`, `SCHEDULER_AUDIENCE` | admin | OIDC check for `/api/jobs/process-notifications` |

The frontends need `VITE_STRIPE_PUBLISHABLE_KEY` and `VITE_TURNSTILE_SITE_KEY` at build time.
