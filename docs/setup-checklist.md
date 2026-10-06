# Berger Houseboats booking system — setup checklist

Work through the sections in order. Tick each box as you go. Where a step says "ask your developer", it needs
someone comfortable with a terminal; everything else is done in web dashboards.

You will create these things:

| What | Where | Example name |
|---|---|---|
| Booking app (customers) | `https://book.bergerhouseboats.com.au` | Cloudflare Pages project `berger-booking` |
| Admin app (staff) | `https://admin.bergerhouseboats.com.au` | Cloudflare Pages project `berger-admin` |
| Customer API | Google Cloud Run service `public-api` | Sydney region |
| Staff API | Google Cloud Run service `admin-api` | Sydney region |
| Database | Google Firestore (Native mode) | Sydney region |
| Payments | Stripe | |
| Email | Brevo | sender `bookings@bergerhouseboats.com.au` |

Keep a private password-manager note called **"Booking system secrets"**. Several steps ask you to save a value
there; later steps paste them back in. Never email or chat these values.

---

## 1. Move the domain's DNS to Cloudflare

Today the nameservers for `bergerhouseboats.com.au` are at `partnerconsole.net`. Email is Google Workspace and
must keep working throughout.

- [ ] **Take a copy of the current DNS zone.** Log in to the current DNS host (partnerconsole.net) and export or
      screenshot *every* record. You will compare against this list.
- [ ] Create a free Cloudflare account (use the business Google Workspace address) and choose
      **Add a site → `bergerhouseboats.com.au` → Free plan**.
- [ ] Cloudflare scans and imports the existing records. **Check every imported record against your copy.** In
      particular confirm these exist and are identical:
  - [ ] **MX** records for Google Workspace (e.g. `smtp.google.com` priority 1, or the older `aspmx.l.google.com`
        set).
  - [ ] **TXT** Google site verification (`google-site-verification=…`).
  - [ ] **TXT** SPF (`v=spf1 include:_spf.google.com …`). It must still include every sender that is already there
        (Brevo, Elastic Email). Do not create a second SPF record — there must be exactly one.
  - [ ] **Brevo** TXT/CNAME records (`brevo-code:…`, DKIM such as `brevo1._domainkey` / `brevo2._domainkey`).
  - [ ] **Elastic Email** TXT/CNAME records.
  - [ ] Any **DKIM** (`google._domainkey`) and **DMARC** (`_dmarc`) records.
  - [ ] The **WordPress** website records (`A` for `bergerhouseboats.com.au`, `CNAME` or `A` for `www`).
  - [ ] Anything else in your copy (e.g. `autodiscover`, other subdomains).
- [ ] Make sure every **mail-related record is "DNS only"** (grey cloud): MX, SPF/TXT, DKIM/DMARC, Brevo,
      Elastic Email, and any `mail.` / `autodiscover` hosts.
- [ ] The WordPress `A`/`CNAME` records may be **Proxied** (orange cloud). If you are unsure, leave them DNS only
      for now; you can switch later.
- [ ] **SSL/TLS → Overview**: if the WordPress host has a valid HTTPS certificate choose **Full (strict)**; if it
      only has a self-signed certificate choose **Full**. Never choose **Flexible** (it causes redirect loops on
      WordPress).
- [ ] Cloudflare shows two nameservers (e.g. `xxx.ns.cloudflare.com`). At the **domain registrar** (where the
      `.com.au` is registered — may also be partnerconsole.net), replace the existing nameservers with the two
      Cloudflare ones.
- [ ] Wait until Cloudflare says the site is **Active** (minutes to 24 hours).
- [ ] Test: send an email to and from a Workspace mailbox; open the website on `https://` and `https://www.`.

## 2. Google Cloud (GCP)

Use one production project. Choose **Sydney (`australia-southeast1`)** every time a region is asked.

- [ ] Create a Google Cloud project, e.g. `berger-harbour-prod`, and link a billing account. Note the **Project ID**.
- [ ] Set a **budget alert** (Billing → Budgets) at e.g. $20/month, emailing the owner.
- [ ] Enable these APIs (APIs & Services → Enable APIs): Cloud Run, Firestore, Secret Manager, Cloud Scheduler,
      Artifact Registry, IAM Credentials, Cloud Logging, Cloud Monitoring.
- [ ] **Firestore**: create a database in **Native mode**, location `australia-southeast1`, database ID `(default)`.
- [ ] **Firestore indexes**: ask your developer to run
      `firebase deploy --only firestore:indexes --project <PROJECT_ID>` from the repository (uses
      `firestore.indexes.json`).
- [ ] **Artifact Registry**: create a Docker repository named `berger-harbour` in `australia-southeast1`.

### 2.1 Service accounts (least privilege)

Create these in IAM & Admin → Service Accounts:

| Service account | Roles |
|---|---|
| `public-api@…` | Cloud Datastore User; Secret Manager Secret Accessor (on its secrets only) |
| `admin-api@…` | Cloud Datastore User; Secret Manager Secret Accessor (on its secrets only) |
| `scheduler@…` | Cloud Run Invoker (on the `admin-api` service, after it exists) |
| `github-deploy@…` | Artifact Registry Writer; Cloud Run Admin; Service Account User (on `public-api@` and `admin-api@`) |

### 2.2 Secrets (Secret Manager)

Create each secret below (Security → Secret Manager → Create secret). Values come from later sections — create
the secret now and add its value when you have it. Generate random values with a password manager (40+ random
characters).

| Secret name | Value | Used by |
|---|---|---|
| `stripe-secret-key` | Stripe secret key (`sk_test_…` first, later `sk_live_…`) | public-api |
| `stripe-webhook-secret` | Stripe webhook signing secret (`whsec_…`) | public-api |
| `brevo-api-key` | Brevo API key | both |
| `turnstile-secret` | Turnstile secret key | public-api |
| `edge-proxy-secret` | random 40+ characters (also goes into both Cloudflare Pages projects) | both |
| `payment-link-secret` | random 40+ characters. **Never change it after go-live** — it would break emailed payment links. | both |
| `admin-allowed-emails` | comma-separated staff Google accounts, e.g. `office@bergerhouseboats.com.au` | admin-api |

Grant `public-api@` and `admin-api@` **Secret Manager Secret Accessor** on the secrets marked for them.

### 2.3 Workload Identity Federation for GitHub (no JSON keys)

Ask your developer to run (replace `OWNER/REPO` and `PROJECT_ID`):

```bash
gcloud iam workload-identity-pools create github --project=PROJECT_ID --location=global
gcloud iam workload-identity-pools providers create-oidc github --project=PROJECT_ID --location=global \
  --workload-identity-pool=github --issuer-uri=https://token.actions.githubusercontent.com \
  --attribute-mapping=google.subject=assertion.sub,attribute.repository=assertion.repository \
  --attribute-condition="assertion.repository=='OWNER/REPO'"
gcloud iam service-accounts add-iam-policy-binding github-deploy@PROJECT_ID.iam.gserviceaccount.com \
  --role=roles/iam.workloadIdentityUser \
  --member="principalSet://iam.googleapis.com/projects/PROJECT_NUMBER/locations/global/workloadIdentityPools/github/attribute.repository/OWNER/REPO"
```

- [ ] In GitHub → repository → Settings → Secrets and variables → Actions → **Variables**, add:

| Variable | Value |
|---|---|
| `GCP_PROJECT_ID` | the project ID |
| `GCP_ARTIFACT_REPOSITORY` | `berger-harbour` |
| `GCP_WORKLOAD_IDENTITY_PROVIDER` | `projects/PROJECT_NUMBER/locations/global/workloadIdentityPools/github/providers/github` |
| `GCP_DEPLOY_SERVICE_ACCOUNT` | `github-deploy@PROJECT_ID.iam.gserviceaccount.com` |
| `PUBLIC_API_SERVICE_ACCOUNT` | `public-api@PROJECT_ID.iam.gserviceaccount.com` |
| `ADMIN_API_SERVICE_ACCOUNT` | `admin-api@PROJECT_ID.iam.gserviceaccount.com` |
| `SCHEDULER_SERVICE_ACCOUNT` | `scheduler@PROJECT_ID.iam.gserviceaccount.com` |
| `ADMIN_API_URL` | the admin-api Cloud Run URL (after the first deploy, see 2.4) |
| `CF_ACCESS_TEAM_DOMAIN` | e.g. `bergerhouseboats.cloudflareaccess.com` (section 5) |
| `CF_ACCESS_AUD` | the Access application AUD tag (section 5) |
| `STRIPE_PUBLISHABLE_KEY` | `pk_test_…` / later `pk_live_…` |
| `TURNSTILE_SITE_KEY` | Turnstile site key |
| `CLOUDFLARE_ACCOUNT_ID` | Cloudflare account ID |

- [ ] Under **Secrets** add `CLOUDFLARE_API_TOKEN` (a Cloudflare API token with *Cloudflare Pages: Edit*).

### 2.4 Cloud Run (first deploy)

- [ ] Merge to `main` (or run the **Deploy** workflow manually). It builds both containers, pushes them to Artifact
      Registry and deploys `public-api` (max 3 instances) and `admin-api` (max 2), both with min instances 0 and
      ingress "all".
- [ ] Copy each service URL (Cloud Run → service → URL, like `https://admin-api-xxxx-ts.a.run.app`). Set the GitHub
      variable `ADMIN_API_URL` to the admin-api URL and re-run the Deploy workflow (it becomes the OIDC audience).
- [ ] Give `scheduler@` the **Cloud Run Invoker** role on the `admin-api` service.

The services are reachable on the internet, but every route requires the edge proxy secret, a Cloudflare Access
login (admin), a Stripe signature (webhook) or a Google OIDC token (jobs).

### 2.5 Cloud Scheduler

- [ ] Cloud Scheduler → Create job: name `process-notifications`, region `australia-southeast1`, frequency
      `0 * * * *` (hourly), time zone `Australia/Sydney`.
- [ ] Target **HTTP**, method **POST**, URL `<admin-api URL>/api/jobs/process-notifications`.
- [ ] Auth header **Add OIDC token**, service account `scheduler@…`, audience = the admin-api URL (exactly the
      value of `ADMIN_API_URL`).
- [ ] Press **Force run** and check it succeeds (HTTP 200).

(Cloud Scheduler gives 3 free jobs per billing account; this is one job, and hourly runs fit in Cloud Run's free tier.)

### 2.6 Alerts (Cloud Logging → log-based alerts)

Create three log-based alert policies that email the staff alert address (add it as a notification channel first):

| Alert | Log query |
|---|---|
| Webhook processing error | `resource.type="cloud_run_revision" AND resource.labels.service_name="public-api" AND jsonPayload.Message=~"WEBHOOK_PROCESSING_ERROR"` |
| Notification job failure | `resource.type="cloud_run_revision" AND resource.labels.service_name="admin-api" AND jsonPayload.Message=~"JOB_FAILURE"` |
| Paid but expired booking (refund needed) | `resource.type="cloud_run_revision" AND jsonPayload.Message=~"PAID_BUT_EXPIRED"` |

Optionally add a catch-all for `jsonPayload.LogLevel="Error"` on either service (the services write structured
JSON logs; the level is in `jsonPayload.LogLevel`).

## 3. Stripe

- [ ] Create or sign in to the Stripe account; complete business verification (ABN, bank account).
- [ ] Stay in **Test mode** for now. Developers → API keys: copy the **publishable key** (`pk_test_…`) into the
      GitHub variable `STRIPE_PUBLISHABLE_KEY` and the **secret key** (`sk_test_…`) into the `stripe-secret-key`
      secret.
- [ ] Developers → Webhooks (Workbench → Event destinations) → **Add endpoint**:
  - URL: `<public-api URL>/api/stripe/webhook` (the Cloud Run URL directly, not the `book.` domain)
  - Events: `checkout.session.completed` and `checkout.session.expired`
  - Copy the signing secret (`whsec_…`) into the `stripe-webhook-secret` secret.
- [ ] Settings → Payment methods: keep **Cards** on; turn on **Apple Pay** and **Google Pay**. Leave delayed methods
      (bank debits, BNPL) off — the system only accepts card and wallet payments.
- [ ] Settings → Payment method domains: add `book.bergerhouseboats.com.au` and `bergerhouseboats.com.au` (the
      booking app runs inside the WordPress page on desktop). Follow Stripe's verification steps.
- [ ] Settings → Branding: logo and colours (used by the embedded checkout and receipts).
- [ ] Settings → Customer emails: turn on **Successful payments** receipts (receipts are the customer's payment
      record; the system does not issue tax invoices).
- [ ] Refunds are made in the Stripe dashboard (Payments → payment → Refund). The booking system does not refund.

## 4. Brevo (email)

- [ ] In Brevo → Senders, Domains & Dedicated IPs → **Domains**: authenticate `bergerhouseboats.com.au`. Brevo shows
      the DNS records to add (brevo-code TXT, DKIM, DMARC). Add them in Cloudflare DNS as **DNS only**. If records
      already exist, compare and keep only one SPF record.
- [ ] Add the sender `bookings@bergerhouseboats.com.au` (name "Berger Houseboats") and verify it.
- [ ] SMTP & API → API keys → **Generate** a key; save it in the `brevo-api-key` secret.
- [ ] Make sure replies to `bookings@bergerhouseboats.com.au` reach a monitored mailbox (Google Workspace group or
      alias).

## 5. Cloudflare Pages, Access, Turnstile and rate limiting

### 5.1 Pages projects

- [ ] Workers & Pages → Create → Pages → **Direct upload** project `berger-booking` (the deploy workflow uploads it).
      Custom domains → add `book.bergerhouseboats.com.au`.
- [ ] Same for `berger-admin` with custom domain `admin.bergerhouseboats.com.au`.
- [ ] In each project → Settings → Variables and secrets (Production), add:
  - `API_ORIGIN` = the matching Cloud Run URL (`public-api` for booking, `admin-api` for admin)
  - `EDGE_PROXY_SECRET` (type *Secret*) = the same value as the `edge-proxy-secret` GCP secret
- [ ] The `_headers` files in the repo set the security headers, including
      `frame-ancestors https://bergerhouseboats.com.au https://www.bergerhouseboats.com.au` for the booking app and
      no framing at all for the admin app. Nothing to configure.

### 5.2 Cloudflare Access (admin app)

- [ ] Zero Trust → Settings → Authentication → **Add new → Google Workspace** (follow the wizard; it creates a
      Google OAuth client). Note the **team domain** (`<team>.cloudflareaccess.com`) → GitHub variable
      `CF_ACCESS_TEAM_DOMAIN`.
- [ ] Zero Trust → Access → Applications → **Add → Self-hosted**: domain `admin.bergerhouseboats.com.au`, identity
      provider Google Workspace only, session duration e.g. 24 hours.
- [ ] Policy "Staff": **Include** → Emails → the staff account(s); **Require** → Country → Australia.
      (Restricting to NSW only is not possible in Access and not required.)
- [ ] Copy the application's **Application Audience (AUD) tag** → GitHub variable `CF_ACCESS_AUD`.
- [ ] Put the same staff email(s) into the `admin-allowed-emails` secret (the API checks them again).
- [ ] Re-run the Deploy workflow so admin-api picks up the Access settings.

### 5.3 Turnstile

- [ ] Turnstile → Add widget: name "Booking", hostname `book.bergerhouseboats.com.au`, mode **Managed**.
- [ ] Site key → GitHub variable `TURNSTILE_SITE_KEY`; secret key → `turnstile-secret` GCP secret.

### 5.4 Rate limiting

- [ ] Security → WAF → Rate limiting rules → Create: *if* hostname equals `book.bergerhouseboats.com.au` *and* URI
      path starts with `/api/`, *then* **Block** for 1 minute when a client makes more than **60 requests per
      minute**. (Free plan allows one rule; this is it.)

## 6. Seed and complete the data

- [ ] Ask your developer to run the seed once against production (safe to repeat; it never overwrites edits):
      ```bash
      FIRESTORE_PROJECT_ID=<PROJECT_ID> \
      SEED_STAFF_ALERT_EMAIL=bookings@bergerhouseboats.com.au \
      SEED_CONTACT_PHONE="(02) xxxx xxxx" SEED_CONTACT_EMAIL=bookings@bergerhouseboats.com.au \
      dotnet run --project src/BergerHarbour.AdminApi -- seed
      ```
      (needs `gcloud auth application-default login` with an account that can write Firestore.)
- [ ] Open `https://admin.bergerhouseboats.com.au` and sign in with Google.
- [ ] **Settings → General**: check deposit ($1,000), lead time (30 days), bookings open until, staff alert email,
      contact phone and email, hire terms URL.
- [ ] **Fleet**: for every boat, check the **slug matches its WordPress page URL**, then enter beds, bedding
      description, security bond, rates for every season (Mid-week, Weekend, Week, and Long weekend), allowed
      add-ons, and tick **Active**. Pacific Blue is pre-filled as the reference boat — check its figures too.
- [ ] **Settings → Add-ons**: enter prices for add-ons that are not "price on request". Welcome hamper and Skipper
      stay price on request.
- [ ] **Settings → Blocked periods**: enter Easter and every long weekend up to "bookings open until" (Christmas /
      New Year 2026–27 is pre-filled with the extended payment schedule).
- [ ] **Settings → Email templates**: review the wording of each template and press **Send test** for each.

## 7. Enter existing future bookings (before go-live)

- [ ] For every **future** booking on paper/Excel, create a staff booking in **Bookings → + New** with the
      customer's email, correct boat, dates and agreed hire price.
      Note: creating a non-stand-by booking emails the customer a confirmation — let customers know the new system
      is coming, or enter them as you contact them.
- [ ] Record payments already received with **Add manual payment** (amount, date, note such as "Deposit — EFT").
- [ ] Enter stand-by (store credit) customers with the **Stand-by** box ticked.
- [ ] Past bookings are **not** entered.

## 8. End-to-end test in Stripe test mode, then go live

In test mode (cards: `4242 4242 4242 4242` succeeds, `4000 0025 0000 3155` asks for 3-D Secure, any future expiry
and any CVC):

- [ ] Book Pacific Blue online on desktop (inside the WordPress modal) and on a phone (new tab).
- [ ] Check: the deposit is charged, the booking appears **Active** in the admin timeline, the customer gets
      email #1 and staff get #2, and the Stripe receipt arrives.
- [ ] In the admin app, confirm a price-on-request add-on and press **Send payment link**; pay through the link;
      check email #5 and the booking's payment status.
- [ ] Try two browsers booking the same dates at once: one must get "Sorry, those dates were just taken".
- [ ] Try a Christmas date: the "please call or email us" pop-up appears.
- [ ] Check Cloud Scheduler's last run succeeded.
- [ ] **Go live**: in Stripe switch to live mode, create a **new live webhook endpoint** (same URL and events), then
      update the `stripe-secret-key` and `stripe-webhook-secret` secrets with the live values and the GitHub
      variable `STRIPE_PUBLISHABLE_KEY` with `pk_live_…`. Switch the Turnstile keys if you used test ones. Re-run the
      Deploy workflow.
- [ ] Make one small real booking and refund it in Stripe, then cancel it in the admin app.

## 9. Add the Book Now button to WordPress

- [ ] Open `wordpress/book-now-block.html` from the repository and copy all of it.
- [ ] For each of the 14 boat pages: edit the page → add a **Custom HTML** block where the button should appear →
      paste → change only `data-boat="pacific-blue"` to that boat's slug (the last part of the page URL, e.g.
      `https://bergerhouseboats.com.au/houseboats/ocean-spirit/` → `ocean-spirit`) → Update.
- [ ] Test each page on desktop (modal opens, Esc closes it) and on a phone (opens a new tab).
- [ ] Inactive boats show "currently unavailable for online booking" — activate them in Fleet when ready.

---

### Every year

- [ ] Enter next year's Christmas/New Year (with the extended schedule ticked), Easter and long weekends.
- [ ] Check prices and seasons.
- [ ] Move **Settings → General → Bookings open until** forward.
