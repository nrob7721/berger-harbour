# Berger Houseboats booking system — setup checklist

Work through this list once for **dev**, then again for **prod**. Steps marked **(prod only)** or **(dev only)** apply to one environment. Tick each box as you go.

You will need: the Google Workspace admin login, a Google Cloud billing account, the Cloudflare account, the Stripe account, the GitHub repository (admin access), and access to the DNS host for `bergerhouseboats.com.au` (partnerconsole.net).

## Values that differ between environments

| Value | dev | prod |
|---|---|---|
| GCP project id (example) | `berger-harbour-dev` | `berger-harbour-prod` |
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Production` |
| Booking app Pages project | `berger-booking-dev` | `berger-booking` |
| Booking app address | `https://berger-booking-dev.pages.dev` | `https://bookings.bergerhouseboats.com.au` |
| Admin app Pages project | `berger-admin-dev` | `berger-admin` |
| Admin app address | `https://berger-admin-dev.pages.dev` | `https://berger-admin.pages.dev` |
| Stripe keys | test mode (`pk_test_…`, `sk_test_…`) | live mode (`pk_live_…`, `sk_live_…`) |
| Turnstile widget hostname | `berger-booking-dev.pages.dev` | `bookings.bergerhouseboats.com.au` |
| Email subject prefix / sender name | `[DEV] ` / Berger Houseboats (DEV) | none / Berger Houseboats |
| GitHub Environment | `dev` | `production` |

The booking address, CORS origin, sender name and subject prefix are already set in `appsettings.Development.json` and `appsettings.Production.json`. Change those files (and redeploy) only if a Pages project gets a different name.

---

## 1. Booking subdomain (prod only)

- [ ] Do step 5 (Cloudflare Pages) first, so the `berger-booking` project exists.
- [ ] In Cloudflare → **Workers & Pages → berger-booking → Custom domains → Set up a custom domain**, enter `bookings.bergerhouseboats.com.au`. Cloudflare shows the CNAME target `berger-booking.pages.dev`.
- [ ] At the existing DNS host (partnerconsole.net) add **one** record: `CNAME  bookings  →  berger-booking.pages.dev`. Change nothing else; WordPress and email stay where they are.
- [ ] Check for CAA records on `bergerhouseboats.com.au`. If there are any, add CAA records allowing Cloudflare's certificate authorities (`letsencrypt.org`, `pki.goog`, `ssl.com`).
- [ ] Wait until the custom domain shows **Active** in Cloudflare (usually minutes, up to 24 hours).
- [ ] Fallback if the DNS host can't add a CNAME: buy a separate domain, add it to Cloudflare (free plan), use `bookings.<that domain>` instead, and update `PUBLIC_BOOKING_BASE_URL` and `CORS_ALLOWED_ORIGIN` in `appsettings.Production.json`, the CSP in `web/booking/public/_headers` (frame-ancestors stay the WordPress site), the `BOOKING_ORIGIN` in `wordpress/book-now-block.html`, and the Turnstile hostname.

## 2. Google Cloud

### 2.1 Project and APIs
- [ ] Create the project (see the table) and link the billing account.
- [ ] Enable these APIs: Cloud Run, Artifact Registry, Firestore, Secret Manager, Cloud Scheduler, IAM Credentials, Security Token Service, Cloud Logging, Cloud Monitoring.
- [ ] Create **Firestore** in **Native mode**, location `australia-southeast1`.
- [ ] Deploy the indexes: `firebase deploy --only firestore:indexes --project <project id>` (from the repository root).

### 2.2 Artifact Registry (once, in the prod project)
- [ ] In the **prod** project create a Docker repository in `australia-southeast1` (e.g. `berger`). Both environments use it.

### 2.3 Service accounts
- [ ] `public-api` — roles: **Cloud Datastore User**.
- [ ] `admin-api` — roles: **Cloud Datastore User**.
- [ ] `scheduler` — no roles (it only identifies the Scheduler job to admin-api).
- [ ] `github-deploy` — roles in this project: **Cloud Run Admin**, **Service Account User** (on `public-api` and `admin-api`), **Artifact Registry Reader** on the prod repository. **(dev only)** also **Artifact Registry Writer** on the prod repository — and nothing else in prod.
- [ ] Give the Cloud Run service agent of the **dev** project (`service-<dev project number>@serverless-robot-prod.iam.gserviceaccount.com`) **Artifact Registry Reader** on the prod repository so dev can pull the images.

### 2.4 Secret Manager
Create each secret (exact names — the deploy refers to them):
- [ ] `stripe-secret-key` — Stripe secret key (`sk_test_…` in dev, `sk_live_…` in prod).
- [ ] `stripe-webhook-secret` — the webhook signing secret from step 3 (`whsec_…`).
- [ ] `gmail-app-password` — the Google app password from step 4.
- [ ] `turnstile-secret` — the Turnstile widget's secret key from step 5.
- [ ] `edge-proxy-secret-public` — a long random string (e.g. `openssl rand -base64 48`). Also goes into the booking Pages project (step 5).
- [ ] `edge-proxy-secret-admin` — another long random string. Also goes into the admin Pages project.
- [ ] `payment-link-secret` — another long random string (at least 32 characters). **Never change it** once bookings exist: it makes the payment links in emails.
- [ ] `admin-allowed-emails` — staff emails allowed into the admin app, comma-separated (e.g. `info@bergerhouseboats.com.au`).
- [ ] Grant **Secret Manager Secret Accessor**: `public-api` on the first six and `payment-link-secret`; `admin-api` on `gmail-app-password`, `edge-proxy-secret-admin`, `payment-link-secret`, `admin-allowed-emails`.

### 2.5 Workload Identity Federation (GitHub, no JSON keys)
- [ ] Create a workload identity pool `github` and an OIDC provider `github` with issuer `https://token.actions.githubusercontent.com`, attribute mapping `google.subject=assertion.sub`, `attribute.repository=assertion.repository`, `attribute.environment=assertion.environment`.
- [ ] Attribute condition — dev: `assertion.repository == "<owner>/<repo>"`; **prod**: `assertion.repository == "<owner>/<repo>" && assertion.environment == "production"`.
- [ ] Allow the pool to impersonate `github-deploy` (**Workload Identity User** for `principalSet://iam.googleapis.com/projects/<number>/locations/global/workloadIdentityPools/github/attribute.repository/<owner>/<repo>`).

### 2.6 First deploy, then Cloud Scheduler
- [ ] Do step 2.8 (GitHub Environments) and run the deploy (push to `main` for dev; publish a release for prod, see step 8). Note the `admin-api` service URL (`https://admin-api-….a.run.app`) and the `public-api` URL.
- [ ] Set the GitHub variable `ADMIN_API_URL` to the admin-api URL (it is the Scheduler token audience) and redeploy.
- [ ] Create a Cloud Scheduler job in `australia-southeast1`: name `process-notifications`, frequency `0 * * * *` (hourly), time zone `Australia/Sydney`, target **HTTP POST** `<admin-api URL>/api/jobs/process-notifications`, auth header **Add OIDC token**, service account `scheduler@…`, audience `<admin-api URL>`.
- [ ] Run the job once by hand and check it returns 200 in Cloud Run logs.

### 2.7 Log-based alerts
Create an email notification channel to the staff alert address, then three log-based alert policies (Logging → Logs Explorer → Create alert) on `resource.type="cloud_run_revision"` and:
- [ ] Paid but expired (refund by hand): `jsonPayload.Message:"PAID_BUT_EXPIRED"` (public-api).
- [ ] Webhook processing errors: `jsonPayload.Message:"WEBHOOK_FAILED"` (public-api).
- [ ] Job failures: `jsonPayload.Message:"JOB_FAILED"` (admin-api), plus `httpRequest.status>=500` on admin-api `/api/jobs`.

### 2.8 GitHub Environments
In GitHub → Settings → Environments create `dev` and `production` (production: add **required reviewers**). Set these **variables** in each (same names, different values):

| Variable | Value |
|---|---|
| `GCP_PROJECT_ID` | the environment's project id |
| `GCP_REGISTRY_PROJECT_ID` | the **prod** project id (shared registry) |
| `GCP_ARTIFACT_REPOSITORY` | the repository name, e.g. `berger` |
| `GCP_WORKLOAD_IDENTITY_PROVIDER` | `projects/<number>/locations/global/workloadIdentityPools/github/providers/github` |
| `GCP_DEPLOY_SERVICE_ACCOUNT` | `github-deploy@<project>.iam.gserviceaccount.com` |
| `PUBLIC_API_SERVICE_ACCOUNT` | `public-api@<project>.iam.gserviceaccount.com` |
| `ADMIN_API_SERVICE_ACCOUNT` | `admin-api@<project>.iam.gserviceaccount.com` |
| `SCHEDULER_SERVICE_ACCOUNT` | `scheduler@<project>.iam.gserviceaccount.com` |
| `ADMIN_API_URL` | the admin-api Cloud Run URL (after the first deploy) |
| `ASPNETCORE_ENVIRONMENT` | `Development` or `Production` |
| `CF_ACCESS_TEAM_DOMAIN` | e.g. `berger.cloudflareaccess.com` |
| `CF_ACCESS_AUD` | the Access application's AUD tag (step 5) |
| `STRIPE_PUBLISHABLE_KEY` | `pk_test_…` / `pk_live_…` |
| `TURNSTILE_SITE_KEY` | the Turnstile site key |
| `CLOUDFLARE_ACCOUNT_ID` | the Cloudflare account id |
| `PAGES_BOOKING_PROJECT` | `berger-booking-dev` / `berger-booking` |
| `PAGES_ADMIN_PROJECT` | `berger-admin-dev` / `berger-admin` |

- [ ] And this **secret**: `CLOUDFLARE_API_TOKEN` — a Cloudflare API token with **Cloudflare Pages: Edit**.

## 3. Stripe

- [ ] In the Stripe dashboard (test mode for dev, live mode for prod) copy the publishable and secret keys into `STRIPE_PUBLISHABLE_KEY` and the `stripe-secret-key` secret.
- [ ] **Developers → Webhooks → Add endpoint**: URL `<public-api Cloud Run URL>/api/stripe/webhook`, events `checkout.session.completed` and `checkout.session.expired`. Copy the signing secret into `stripe-webhook-secret`.
- [ ] **Settings → Payment methods**: keep cards (with Apple Pay and Google Pay) on; the system only allows card payments so a completed Checkout means paid.
- [ ] **Settings → Payment method domains**: add `bergerhouseboats.com.au`, `www.bergerhouseboats.com.au` and the booking address (`bookings.bergerhouseboats.com.au`, or `berger-booking-dev.pages.dev` in test mode) so Apple Pay / Google Pay work inside the WordPress pop-up.
- [ ] **Settings → Branding / Public details**: business name, support email and phone (shown on receipts). Turn on **email receipts** for successful payments.
- [ ] Switching to live (prod): repeat the above in live mode and update the prod secrets and `STRIPE_PUBLISHABLE_KEY`, then redeploy.

## 4. Gmail SMTP

- [ ] Sign in as `info@bergerhouseboats.com.au` (a Google Workspace user). Turn on **2-Step Verification**.
- [ ] Create an **app password** (Google Account → Security → App passwords), name it "Booking system", and store it in the `gmail-app-password` secret. (Both environments may use the same mailbox; dev emails get the `[DEV] ` prefix.)
- [ ] At the existing DNS host check `bergerhouseboats.com.au` has: SPF `v=spf1 include:_spf.google.com ~all`, a Google DKIM record (Workspace admin → Apps → Gmail → Authenticate email), and a DMARC record (e.g. `v=DMARC1; p=none; rua=mailto:info@bergerhouseboats.com.au`). Add any that are missing.

## 5. Cloudflare

### 5.1 Pages projects
- [ ] Create `berger-booking` and `berger-admin` (dev: `berger-booking-dev`, `berger-admin-dev`) as **Direct Upload** Pages projects (the GitHub workflow uploads them).
- [ ] In each project → Settings → **Variables and secrets** (Production):
  - booking: `API_ORIGIN` = public-api Cloud Run URL; `EDGE_PROXY_SECRET` (encrypted) = the `edge-proxy-secret-public` value.
  - admin: `API_ORIGIN` = admin-api Cloud Run URL; `EDGE_PROXY_SECRET` (encrypted) = the `edge-proxy-secret-admin` value.
- [ ] The `_headers` files are deployed with the apps (booking: CSP allowing Stripe and Turnstile and framing only by the WordPress site; admin: no framing).
- [ ] The admin app has **no custom domain**: it is served at `https://berger-admin.pages.dev`.

### 5.2 Access (admin app only)
- [ ] Zero Trust → Settings → Authentication: add **Google Workspace** as a login method (domain `bergerhouseboats.com.au`).
- [ ] In `berger-admin` → Settings → **Enable access policy**. Then in Zero Trust → Access → Applications edit that application so it covers the production address `berger-admin.pages.dev` (not only the preview wildcard).
- [ ] Policy: **Allow**, *Include* → Emails → the staff emails; *Require* → Country → **Australia**.
- [ ] Copy the application's **AUD tag** into `CF_ACCESS_AUD` and the team domain into `CF_ACCESS_TEAM_DOMAIN`.
- [ ] Dev: a separate Access application on `berger-admin-dev.pages.dev`, set up the same way.
- [ ] Customers are never put behind Access.

### 5.3 Turnstile
- [ ] Add a Turnstile widget (Managed) for the hostname in the table. Copy the site key into `TURNSTILE_SITE_KEY` and the secret into `turnstile-secret`.

No Cloudflare rate-limiting rules are needed (the public API limits requests per IP itself).

## 6. Seed and complete the data

- [ ] Run the seed once (it only adds what's missing and never overwrites edits). From a computer with the .NET SDK and `gcloud` signed in to the project:
  `ASPNETCORE_ENVIRONMENT=Production FIRESTORE_PROJECT_ID=<project id> SEED_STAFF_ALERT_EMAIL=info@bergerhouseboats.com.au SEED_CONTACT_PHONE="<phone>" SEED_CONTACT_EMAIL=info@bergerhouseboats.com.au dotnet run --project src/BergerHarbour.AdminApi -- seed`
  (`seeddata.sh` does the same for dev.)
- [ ] Admin app → **Fleet**: for every boat check berth, adult beds, kids beds, bedding, bond, optional deposit, image URL, Spa, Pet friendly, **slug** (must match the WordPress page address), rates and allowed add-ons, then switch it **Active**. Only Pacific Blue starts active.
- [ ] **Settings → Add-ons**: enter prices (untick Price on request), set the order with ▲/▼, check Car parking capacity (11 car spots).
- [ ] **Settings → Blocked periods**: enter Easter and every long weekend up to "Bookings open until" (Christmas / New Year 2026–27 is seeded).
- [ ] **Settings → General**: contact phone and email, staff alert email, and (optionally) bank details. Check "Bookings open until".
- [ ] **Settings → Email templates**: review each template and use **Send test**.

## 7. Before go-live: enter existing bookings

- [ ] Staff enter **every existing future booking** from paper / Excel as staff bookings (Bookings → + New), using the customer's email, and record payments already received with **Add manual payment**. Past bookings are not entered.
- [ ] Note: staff bookings email the customer a confirmation (#1). Use stand-by for placeholders, or tell customers to expect the email.

## 8. End-to-end test, then go live

- [ ] In **dev** (Stripe test mode): book online with card `4242 4242 4242 4242`; check the confirmation and staff emails, the booking on the timeline, a balance payment through the emailed link, Back from the payment step, and a staff booking.
- [ ] Prod release: create a GitHub **Release** with a tag like `v1.0.0` on a commit that has already deployed to dev; approve the `production` deployment. (Rolling back = re-run an older release's workflow.)
- [ ] Repeat a smaller test in prod with live keys (refund it in Stripe afterwards).

## 9. WordPress

- [ ] On each of the 14 boat pages add a **Custom HTML** block and paste `wordpress/book-now-block.html`. Change only `data-boat="pacific-blue"` to that boat's slug (the last part of the page address).
- [ ] Check on a desktop (opens a pop-up) and a phone (opens a new tab), and that **Close** on the final screen closes the pop-up.
