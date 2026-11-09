# Berger Houseboats booking system

Online booking for Berger Houseboats' house boats (customers) and an internal admin app (staff).

- **Booking app** (`web/booking`): embedded in each boat's WordPress page via `wordpress/book-now-block.html`; pays the deposit with Stripe Embedded Checkout. `/pay/?token=` is the balance pay page reached from emailed payment links.
- **Admin app** (`web/admin`): Bookings timeline and booking dialog, Fleet, Customers, Settings. Behind Cloudflare Access.
- **APIs** (`src/`): .NET 10, two Cloud Run services (`public-api`, `admin-api`) over Firestore, in DDD layers: `Domain` → `Application` → `Infrastructure` → `PublicApi` / `AdminApi`.

## Run it locally

Requirements: .NET 10 SDK, Node 20+, Java 21+, `npm i -g firebase-tools`.

```bash
scripts/dev.sh
```

That starts the Firestore emulator, both APIs (`ASPNETCORE_ENVIRONMENT=Local`; admin-api seeds the emulator on start), and both Vite dev servers:

- Booking app: http://localhost:5173/?boat=pacific-blue
- Admin app: http://localhost:5174 (Local signs you in as `staff@localhost`)
- Emails are written to `.dev-emails/` instead of being sent.

For payments locally, export `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET` and `VITE_STRIPE_PUBLISHABLE_KEY` (test keys) and run `stripe listen --forward-to localhost:5080/api/stripe/webhook`. The hourly job can be run by hand: `curl -X POST http://localhost:5081/api/jobs/process-notifications`.

## Tests

```bash
dotnet test tests/BergerHarbour.Domain.Tests
dotnet test tests/BergerHarbour.Application.Tests
dotnet test tests/BergerHarbour.ArchitectureTests
firebase emulators:exec --only firestore --project demo-berger-harbour "dotnet test tests/BergerHarbour.Infrastructure.IntegrationTests"

cd web && npm ci && npm run typecheck && npm run lint && npm test
```

The build enforces the code organisation rules (prompt.md §7.9): StyleCop SA1402/SA1649 (one type per file, file named after
the type) are errors, and `BergerHarbour.ArchitectureTests` checks layer references, aggregates/repositories and thin controllers.

The API builds write their OpenAPI documents to `web/shared/openapi`; `npm run gen:api` turns them into `web/shared/src/api`. CI fails if either is out of date, so commit both after changing an API.

## Deploying

See [docs/setup-checklist.md](docs/setup-checklist.md). Pushes to `main` deploy to dev; a published GitHub Release (`vX.Y.Z`) promotes the same images to production after approval.
