#!/usr/bin/env bash
# Seeds the dev Firestore database (safe to re-run; never overwrites edits).
set -euo pipefail
cd /home/jebus/Documentos/git/berger-harbour

PROJECT_ID=berger-harbour-dev

# 1. One-time login: creates Application Default Credentials (opens a browser).
#    Skipped if ADC already exist.
if ! gcloud auth application-default print-access-token >/dev/null 2>&1; then
  gcloud auth application-default login
fi
# Optional: only needed if Firestore calls complain about a missing quota project.
gcloud auth application-default set-quota-project "$PROJECT_ID" || \
  echo "Skipping quota project (account lacks serviceusage.services.use); continuing."

# 2. Make sure nothing points at the local emulator.
unset FIRESTORE_EMULATOR_HOST

# 3. Run the seed against the dev project.
ASPNETCORE_ENVIRONMENT=Development \
FIRESTORE_PROJECT_ID="$PROJECT_ID" \
SEED_STAFF_ALERT_EMAIL=info@bergerhouseboats.com.au \
SEED_CONTACT_PHONE="(07) 5524 3222" \
SEED_CONTACT_EMAIL=info@bergerhouseboats.com.au \
dotnet run --no-launch-profile --project src/BergerHarbour.AdminApi -- seed
