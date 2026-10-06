#!/usr/bin/env bash
# Starts everything for local development:
#   Firestore emulator (127.0.0.1:8080), public-api (:5080), admin-api (:5081, seeds on startup),
#   booking app (http://localhost:5173/?boat=pacific-blue) and admin app (http://localhost:5174).
# Requirements: .NET 10 SDK, Node 20+, Java 21+, firebase-tools (npm i -g firebase-tools).
# Emails are written to ./.dev-emails instead of being sent. Ctrl+C stops everything.
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT=$(pwd)
mkdir -p .dev-emails
export FIRESTORE_EMULATOR_HOST=127.0.0.1:8080
export ASPNETCORE_ENVIRONMENT=Development
export EMAIL_OUTPUT_DIR="$ROOT/.dev-emails"
# Optional for payments: export STRIPE_SECRET_KEY=sk_test_… STRIPE_WEBHOOK_SECRET=whsec_… and
# VITE_STRIPE_PUBLISHABLE_KEY=pk_test_…, then run: stripe listen --forward-to localhost:5080/api/stripe/webhook

pids=()
cleanup() { kill "${pids[@]}" 2>/dev/null || true; }
trap cleanup EXIT INT TERM

firebase emulators:start --only firestore --project demo-berger-harbour & pids+=($!)
until curl -s -o /dev/null "http://$FIRESTORE_EMULATOR_HOST"; do sleep 1; done

dotnet build BergerHarbour.slnx -v q
dotnet run --no-build --project src/BergerHarbour.AdminApi --urls http://localhost:5081 & pids+=($!)
dotnet run --no-build --project src/BergerHarbour.PublicApi --urls http://localhost:5080 & pids+=($!)

(cd web && npm install --no-audit --no-fund && npm run gen:api)
(cd web/booking && npx vite) & pids+=($!)
(cd web/admin && npx vite) & pids+=($!)

echo
echo "Booking app: http://localhost:5173/?boat=pacific-blue"
echo "Admin app:   http://localhost:5174"
echo "Emails:      $ROOT/.dev-emails"
wait
