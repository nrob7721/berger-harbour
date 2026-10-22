# Berger Houseboats booking system — setup checklist

Work through the sections in order. Tick each box as you go. Where a step says "ask your developer", it needs
someone comfortable with a terminal; everything else is done in web dashboards.

You will create these things:

| What | Where | Example name |
|---|---|---|
| Booking app (customers) | `https://bookings.bergerhouseboats.com.au` (a subdomain of your existing domain) | Cloudflare Pages project `berger-booking` |
| Admin app (staff) | `https://berger-admin.pages.dev` (no custom domain; the name follows the project name) | Cloudflare Pages project `berger-admin` |
| Customer API | Google Cloud Run service `public-api` | Sydney region |
| Staff API | Google Cloud Run service `admin-api` | Sydney region |
| Database | Google Firestore (Native mode) | Sydney region |
| Payments | Stripe | |
| Email | Gmail SMTP (Google Workspace) | sender `info@bergerhouseboats.com.au` |

### Two environments: dev and prod

There are two complete copies of the system. **Dev** is where every change lands first (each merge to `main` deploys
it automatically) and where you test with fake bookings and Stripe test cards. **Prod** is what customers use; it only
changes when a GitHub Release is published and approved.

Work through this checklist **twice**: first for dev, then for prod. Steps marked **(prod only)** or **(dev only)** are
skipped the other time. Wherever a step names a prod value, use the dev value from this table when setting up dev:

| Value | Dev | Prod |
|---|---|---|
| GCP project | `berger-harbour-dev` | `berger-harbour-prod` |
| GitHub Environment | `dev` | `production` |
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Production` |
| Booking app Pages project / address | `berger-booking-dev` / `https://berger-booking-dev.pages.dev` | `berger-booking` / `https://bookings.bergerhouseboats.com.au` |
| Admin app Pages project / address | `berger-admin-dev` / `https://berger-admin-dev.pages.dev` | `berger-admin` / `https://berger-admin.pages.dev` |
| Stripe | a separate sandbox named "dev" (test keys only) | the account's test mode, then live mode at go-live |
| Turnstile widget | "Booking (dev)" for `berger-booking-dev.pages.dev` | "Booking" for the booking subdomain |
| Emails | from `info@bergerhouseboats.com.au` as "Berger Houseboats (DEV)", subjects start with `[DEV] ` | from `info@bergerhouseboats.com.au` as "Berger Houseboats" |

The addresses, sender and subject prefix are already set in the repository (`appsettings.Development.json` and
`appsettings.Production.json` in each API project); everything else is entered as you go. Secrets are **never** shared
between the two projects: generate new random values for dev.

Keep a private password-manager note called **"Booking system secrets"**. Several steps ask you to save a value
there; later steps paste them back in. Never email or chat these values.

---

## 1. Booking subdomain (prod only)

Dev has no custom domain: its apps live at their `pages.dev` addresses. For dev, only create the Cloudflare account if
it does not exist yet (free plan; use the business Google Workspace address).

The booking app lives at `bookings.bergerhouseboats.com.au`, a subdomain of the existing domain. The existing domain
`bergerhouseboats.com.au` (WordPress and Google Workspace email) **stays where it is**: its DNS is not moved to
Cloudflare and email is not touched. The only change is **one CNAME record** added at the current DNS host
(`partnerconsole.net`). Cloudflare Pages supports custom *subdomains* on domains that are not on Cloudflare.

The admin app has no domain at all. It lives at `https://berger-admin.pages.dev` (section 5.1) and is locked down by
Cloudflare Access, so staff never need a nice URL.

- [ ] Create the Cloudflare account if you have not already. You do **not** add `bergerhouseboats.com.au` to it as a
      site/zone.
- [ ] **Do this in section 5.1, in this order:** (1) create the `berger-booking` Pages project, (2) in its
      **Custom domains** add `bookings.bergerhouseboats.com.au`, (3) only then add the DNS record below. A CNAME created before the
      domain is added in Pages returns error 522.
- [ ] At the DNS host for `bergerhouseboats.com.au`, ask them (or your developer) to add:

      | Type | Name | Value |
      |---|---|---|
      | `CNAME` | `bookings` | `berger-booking.pages.dev` |

- [ ] If the DNS zone has **CAA** records, check they allow Cloudflare's certificate authorities (Let's Encrypt,
      Google Trust Services, SSL.com); otherwise the certificate cannot be issued and the domain stays pending.
- [ ] Wait until the Pages project shows the custom domain as **Active** (minutes to a few hours) and
      `https://bookings.bergerhouseboats.com.au` loads.
- [ ] Confirm the hostname is already set in `wordpress/book-now-block.html` (`APP_ORIGIN`) and in
      `appsettings.Production.json` of both `src/BergerHarbour.PublicApi` and `src/BergerHarbour.AdminApi`
      (`PUBLIC_BOOKING_BASE_URL`, and `CORS_ALLOWED_ORIGIN` for the public API). They already are; change them only if
      you use a different name.

**Fallback if the DNS host cannot add a CNAME:** buy a separate domain (a `.com.au` needs an ABN; use an Australian
registrar), add it to Cloudflare (**Add a site → Free plan**), replace its nameservers with Cloudflare's, wait until it
is **Active** (SSL/TLS → Full (strict)), and use that domain instead of `bookings.bergerhouseboats.com.au` everywhere, including the
three repository files above. Cloudflare's WAF rate limiting is still not used (see 5.4; the app limits requests itself).

## 2. Google Cloud (GCP)

Use one project per environment (`berger-harbour-dev`, then `berger-harbour-prod`). Choose **Sydney
(`australia-southeast1`)** every time a region is asked.

- [ ] Create a Google Cloud project, e.g. `berger-harbour-prod`, and link a billing account. Note the **Project ID**
      and **Project number**.
- [ ] Set a **budget alert** (Billing → Budgets) at e.g. $20/month, emailing the owner.
- [ ] Enable these APIs (APIs & Services → Enable APIs): Cloud Run, Firestore, Secret Manager, Cloud Scheduler,
      Artifact Registry, IAM Credentials, Cloud Logging, Cloud Monitoring.
- [ ] **Firestore**: create a database in **Native mode**, location `australia-southeast1`, database ID `(default)`.
- [ ] **Firestore indexes**: ask your developer to run
      `firebase deploy --only firestore:indexes --project <PROJECT_ID>` from the repository (uses
      `firestore.indexes.json`).
- [ ] **(prod only) Artifact Registry**: create a Docker repository named `berger-harbour` in `australia-southeast1`.
      There is only one, in the prod project: dev builds each commit's images into it and prod deploys the same images.
      If you set up dev first, create the prod project and this repository before dev's first deploy.
- [ ] **(dev only) Let dev use the shared repository.** In the **prod** project → Artifact Registry → `berger-harbour`
      → Permissions, grant:
  - `github-deploy@berger-harbour-dev.iam.gserviceaccount.com`: **Artifact Registry Writer** (it pushes the images)
  - `service-<DEV_PROJECT_NUMBER>@serverless-robot-prod.iam.gserviceaccount.com` (dev's Cloud Run service agent):
    **Artifact Registry Reader** (it pulls them)

### 2.1 Service accounts (least privilege)

Create these in IAM & Admin → Service Accounts:

| Service account | Roles |
|---|---|
| `public-api@…` | Cloud Datastore User; Secret Manager Secret Accessor (on its secrets only) |
| `admin-api@…` | Cloud Datastore User; Secret Manager Secret Accessor (on its secrets only) |
| `scheduler@…` | none (it only identifies the Scheduler job; admin-api checks its OIDC token itself) |
| `github-deploy@…` | Cloud Run Admin; Service Account User (on `public-api@` and `admin-api@`). Prod only: Artifact Registry Reader (to check the release's images exist). Dev's push rights on the shared repository are granted above. |

### 2.2 Secrets (Secret Manager)

Create each secret below (Security → Secret Manager → Create secret). Values come from later sections — create
the secret now and add its value when you have it. Generate random values with a password manager (40+ random
characters).

| Secret name | Value | Used by |
|---|---|---|
| `stripe-secret-key` | Stripe secret key (`sk_test_…` first, later `sk_live_…`) | public-api |
| `stripe-webhook-secret` | Stripe webhook signing secret (`whsec_…`) | public-api |
| `gmail-app-password` | Google app password for `info@bergerhouseboats.com.au` (section 4) | both |
| `turnstile-secret` | Turnstile secret key | public-api |
| `edge-proxy-secret-public` | random 40+ characters (also goes into the `berger-booking` Pages project) | public-api |
| `edge-proxy-secret-admin` | a **different** random 40+ characters (also goes into the `berger-admin` Pages project) | admin-api |
| `payment-link-secret` | random 40+ characters. **Never change it after go-live** — it would break emailed payment links. | both |
| `admin-allowed-emails` | comma-separated staff Google accounts, e.g. `office@bergerhouseboats.com.au` | admin-api |

Use two different edge proxy secrets: if the customer-facing one ever leaks, the admin API's origin stays protected.
Grant `public-api@` and `admin-api@` **Secret Manager Secret Accessor** on the secrets marked for them.

### 2.3 Workload Identity Federation for GitHub (no JSON keys)

Each project trusts only GitHub jobs running in its own GitHub Environment, so a dev deploy can never obtain prod
credentials. Ask your developer to run (replace `OWNER/REPO`, `PROJECT_ID`, `PROJECT_NUMBER`, and `ENVIRONMENT` with
`dev` or `production`):

```bash
gcloud iam workload-identity-pools create github --project=PROJECT_ID --location=global
gcloud iam workload-identity-pools providers create-oidc github --project=PROJECT_ID --location=global \
  --workload-identity-pool=github --issuer-uri=https://token.actions.githubusercontent.com \
  --attribute-mapping=google.subject=assertion.sub,attribute.repository=assertion.repository,attribute.environment=assertion.environment \
  --attribute-condition="assertion.repository=='OWNER/REPO' && assertion.environment=='ENVIRONMENT'"
gcloud iam service-accounts add-iam-policy-binding github-deploy@PROJECT_ID.iam.gserviceaccount.com \
  --role=roles/iam.workloadIdentityUser \
  --member="principalSet://iam.googleapis.com/projects/PROJECT_NUMBER/locations/global/workloadIdentityPools/github/attribute.repository/OWNER/REPO"
```

- [ ] In GitHub → repository → Settings → **Environments**, create the environment (`dev` or `production`):
  - `dev`: Deployment branches and tags → **Selected branches and tags** → `main`.
  - `production`: tick **Required reviewers** and add whoever approves releases; Deployment branches and tags →
    **Selected branches and tags** → add the tag rule `v*`.
- [ ] Once (the first time only), under Settings → Secrets and variables → Actions → **Repository variables**, add:

| Variable | Value |
|---|---|
| `GCP_REGISTRY_PROJECT_ID` | the **prod** project ID (where the shared Artifact Registry lives) |
| `GCP_ARTIFACT_REPOSITORY` | `berger-harbour` |
| `CLOUDFLARE_ACCOUNT_ID` | Cloudflare account ID |

- [ ] Also once, under **Repository secrets**, add `CLOUDFLARE_API_TOKEN` (a Cloudflare API token with *Cloudflare
      Pages: Edit*).
- [ ] In the environment (Settings → Environments → `dev`/`production` → **Environment variables**), add:

| Variable | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` (dev) or `Production` (prod) |
| `GCP_PROJECT_ID` | the project ID |
| `GCP_WORKLOAD_IDENTITY_PROVIDER` | `projects/PROJECT_NUMBER/locations/global/workloadIdentityPools/github/providers/github` |
| `GCP_DEPLOY_SERVICE_ACCOUNT` | `github-deploy@PROJECT_ID.iam.gserviceaccount.com` |
| `PUBLIC_API_SERVICE_ACCOUNT` | `public-api@PROJECT_ID.iam.gserviceaccount.com` |
| `ADMIN_API_SERVICE_ACCOUNT` | `admin-api@PROJECT_ID.iam.gserviceaccount.com` |
| `SCHEDULER_SERVICE_ACCOUNT` | `scheduler@PROJECT_ID.iam.gserviceaccount.com` |
| `ADMIN_API_URL` | the admin-api Cloud Run URL (after the first deploy, see 2.4) |
| `CF_ACCESS_TEAM_DOMAIN` | e.g. `bergerhouseboats.cloudflareaccess.com` (section 5) |
| `CF_ACCESS_AUD` | the Access application AUD tag (section 5) |
| `STRIPE_PUBLISHABLE_KEY` | `pk_test_…` (dev: the dev sandbox's key; prod: later `pk_live_…`) |
| `TURNSTILE_SITE_KEY` | Turnstile site key of this environment's widget |
| `PAGES_BOOKING_PROJECT` | `berger-booking-dev` (dev) or `berger-booking` (prod) |
| `PAGES_ADMIN_PROJECT` | `berger-admin-dev` (dev) or `berger-admin` (prod) |

### 2.4 Cloud Run (first deploy)

- [ ] **Dev:** merge to `main` (or run the **Deploy (dev)** workflow manually). It builds both containers, pushes them
      to the shared Artifact Registry and deploys `public-api` (max 1 instance) and `admin-api` (max 2), both with min
      instances 0, ingress "all" and Cloud Run's invoker IAM check turned off (see the check below).
- [ ] **Prod:** GitHub → Releases → **Draft a new release** → new tag `vX.Y.Z` on a commit that dev has already deployed
      → **Publish release**. The **Release (production)** workflow waits for a required reviewer to approve, then deploys
      that commit's images (it refuses if dev never built them). This is also how every later change reaches customers.
- [ ] Copy each service URL (Cloud Run → service → URL, like `https://admin-api-xxxx-ts.a.run.app`). Set the
      environment variable `ADMIN_API_URL` to the admin-api URL and re-run the deploy (it becomes the OIDC audience).
- [ ] **Check both services reach the app.** The Cloudflare Pages Functions call the APIs without a Google token, so
      the deploy turns off Cloud Run's own sign-in check (`--no-invoker-iam-check`) instead of granting the invoker role
      to `allUsers` (which the org policy *Domain restricted sharing* of a Google Workspace organisation blocks). Run
      this for each service URL:

      ```bash
      curl -i <service URL>/api/bookings
      ```

      The answer must be `403` with the body `Status Code: 403; Forbidden`: that is the app rejecting the missing
      edge proxy secret, so the request got through. An HTML page saying "Your client does not have permission" (and
      Cloud Run logs "The request was not authenticated") means Cloud Run still requires sign-in: check the deploy
      log of that step. If it failed because the organisation enforces the policy *Require Invoker IAM*
      (`run.managed.requireInvokerIam`), ask the Workspace administrator to turn that policy off for this project.

The services are reachable on the internet, but every route requires the edge proxy secret, a Cloudflare Access
login (admin), a Stripe signature (webhook) or a Google OIDC token (jobs, checked by admin-api itself).

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

Dev and prod must not share a Stripe test mode: every test webhook endpoint receives every test payment, so each
environment would log the other's payments as "paid for unknown booking". Dev therefore uses its own **sandbox**.

- [ ] **(dev only)** In the Stripe dashboard, open the account menu → **Sandboxes** → create a sandbox named "dev".
      Do every Stripe step below inside that sandbox, skip the steps about verification, payment method domains and
      go-live, and never use live keys for dev.
- [ ] Create or sign in to the Stripe account; complete business verification (ABN, bank account).
- [ ] Stay in **Test mode** for now. Developers → API keys: copy the **publishable key** (`pk_test_…`) into the
      GitHub environment variable `STRIPE_PUBLISHABLE_KEY` and the **secret key** (`sk_test_…`) into the `stripe-secret-key`
      secret.
- [ ] Developers → Webhooks (Workbench → Event destinations) → **Add endpoint**:
  - URL: `<public-api URL>/api/stripe/webhook` (the Cloud Run URL directly, not the booking domain)
  - Events: `checkout.session.completed` and `checkout.session.expired`
  - Copy the signing secret (`whsec_…`) into the `stripe-webhook-secret` secret.
- [ ] Settings → Payment methods: keep **Cards** on; turn on **Apple Pay** and **Google Pay**. Leave delayed methods
      (bank debits, BNPL) off — the system only accepts card and wallet payments.
- [ ] **(prod only)** Settings → Payment method domains: add the booking domain (`bookings.bergerhouseboats.com.au`) and
      `bergerhouseboats.com.au` (the booking app runs inside the WordPress page on desktop). Follow Stripe's
      verification steps.
- [ ] Settings → Branding: logo and colours (used by the embedded checkout and receipts).
- [ ] Settings → Customer emails: turn on **Successful payments** receipts (receipts are the customer's payment
      record; the system does not issue tax invoices).
- [ ] Refunds are made in the Stripe dashboard (Payments → payment → Refund). The booking system does not refund.

## 4. Gmail SMTP (email)

The system sends every email through `smtp.gmail.com` as `info@bergerhouseboats.com.au`, using an app password. Dev
sends from the same mailbox (as "Berger Houseboats (DEV)", with `[DEV] ` in front of every subject), so for dev only
copy the existing app password into dev's `gmail-app-password` secret; the steps below are done once.
Replies from customers arrive in the same mailbox, and every email sent is also kept in its **Sent** folder. Bounces
(undeliverable emails) come back to its inbox as well; the system does not track delivery or opens.

- [ ] Sign in to Google as `info@bergerhouseboats.com.au` (it must be a real user mailbox, not a group or an alias).
- [ ] Google Account → Security → turn on **2-Step Verification**.
- [ ] Google Account → Security → **App passwords** → create one named "Berger booking system". Save the 16
      characters (without spaces) in the secret `gmail-app-password`. If the App passwords option does not appear,
      ask whoever administers the Workspace (the `partnerconsole.net` reseller) to allow it.
- [ ] Check the email is authenticated, so customers' mail providers do not send it to spam. In the Google Admin
      console (Apps → Google Workspace → Gmail → Authenticate email) make sure DKIM for `bergerhouseboats.com.au` is
      **on**. If it is not, generate the key and add the TXT record Google shows where `bergerhouseboats.com.au`'s DNS
      is hosted today (partnerconsole.net), not in Cloudflare.
- [ ] Check the DNS of `bergerhouseboats.com.au` has exactly one SPF record that includes `_spf.google.com`, and a
      DMARC record (`_dmarc` TXT). Add or fix them at partnerconsole.net only if they are missing.
- [ ] Once the admin app is running (section 6), press **Send test** on each template (Settings → Email templates)
      and check the email arrives from "Berger Houseboats <info@bergerhouseboats.com.au>" and not in spam.

Limits: Google Workspace allows about 2,000 emails a day through SMTP, far more than this system sends.

## 5. Cloudflare Pages, Access and Turnstile

### 5.1 Pages projects

- [ ] Workers & Pages → Create → Pages → **Direct upload** project `berger-booking` (the deploy workflow uploads it).
      Custom domains → add the booking domain (`bookings.bergerhouseboats.com.au`). For dev, the project is
      `berger-booking-dev` with **no** custom domain.
- [ ] Same for `berger-admin` (dev: `berger-admin-dev`), but **do not add a custom domain**: staff use
      `https://berger-admin.pages.dev`. If a name is taken, choose another project name and use it everywhere in
      its place (including the `PAGES_*_PROJECT` environment variables and `CORS_ALLOWED_ORIGIN` /
      `PUBLIC_BOOKING_BASE_URL` in the matching `appsettings.{Development,Production}.json`).
- [ ] In each project → Settings → Variables and secrets (Production), add:
  - `API_ORIGIN` = the matching Cloud Run URL (`public-api` for booking, `admin-api` for admin)
  - `EDGE_PROXY_SECRET` (type *Secret*) = the value of the matching GCP secret: `edge-proxy-secret-public` for
    `berger-booking`, `edge-proxy-secret-admin` for `berger-admin`. The two projects must **not** share a value.
- [ ] The `_headers` files in the repo set the security headers, including
      `frame-ancestors https://bergerhouseboats.com.au https://www.bergerhouseboats.com.au` for the booking app and
      no framing at all for the admin app. Nothing to configure.

### 5.2 Cloudflare Access (admin app)

- [ ] **(prod, Workspace accounts) Enforce 2-step verification in Google Workspace** (Admin console → Security → Authentication → 2-step
      verification → **Allow users to turn on 2-step verification** and **Enforcement: On**, for the organizational
      unit holding the staff accounts; prefer security keys or passkeys over SMS). Cloudflare Access cannot see
      whether a login used MFA when Google is the identity provider, so this is the only place it is enforced.
      Do this before the Access policy below, and confirm each staff member has enrolled.
- [ ] **Google OAuth client.** In the GCP project, Google Auth Platform (APIs & Services → OAuth consent screen):
  - **Audience** → **User type**. **Internal** only lets accounts in the Workspace organisation sign in; anyone else
    (for example a personal Gmail account used for testing dev) gets `Error 403: org_internal`. Use **Internal** when
    every staff account is on the `bergerhouseboats.com.au` Workspace (prod). Use **Make external** when anyone else
    needs to sign in (dev): then either add them under **Test users**, or **Publish app** (the `openid`, `email` and
    `profile` scopes need no Google verification). The Access policy below is what restricts who gets in.
  - **Clients** → **Create client** → *Web application*. Authorized JavaScript origin:
    `https://<team>.cloudflareaccess.com`; authorized redirect URI:
    `https://<team>.cloudflareaccess.com/cdn-cgi/access/callback`. Keep the client ID and secret for the next step.
- [ ] Zero Trust → **Integrations → Identity providers → Add new identity provider**, paste the client ID and secret.
      Pick **Google Workspace** if every staff account is on the Workspace (prod); pick **Google** if personal Gmail
      accounts must sign in (dev). Where the wizard offers to create the OAuth client for you, you can use that
      instead of the previous step, but the consent screen **User type** above still applies. In older dashboards this
      was Settings → Authentication → Login methods → Add new. Note the **team domain** (`<team>.cloudflareaccess.com`, shown under Settings → Custom
      pages or in the Zero Trust URL/overview) → GitHub environment variable `CF_ACCESS_TEAM_DOMAIN`.
- [ ] Protect the admin app's `pages.dev` address (dev: a separate application for `berger-admin-dev.pages.dev`,
      done the same way in the `berger-admin-dev` project). In the `berger-admin` Pages project → Settings → General →
      **Enable access policy**. Cloudflare creates an Access application with a preview wildcard
      (`*.berger-admin.pages.dev`). In Zero Trust → Access → Applications, edit it so the domain is exactly
      `berger-admin.pages.dev` (remove the wildcard from the subdomain field), set the identity provider to the Google
      one created above (and no other), and set the session duration e.g. 24 hours. Never add a custom domain to this project without
      also creating a separate self-hosted Access application for it.
- [ ] Replace the default policy with "Staff": **Include** → Emails → the staff account(s); **Require** → Country → Australia.
      (Restricting to NSW only is not possible in Access and not required.)
- [ ] Copy the application's **Application Audience (AUD) tag** → GitHub environment variable `CF_ACCESS_AUD`.
- [ ] Put the same staff email(s) into the `admin-allowed-emails` secret (the API checks them again).
- [ ] Re-run the latest deploy (dev: **Deploy (dev)**; prod: the latest release's **Release (production)**) so
      admin-api picks up the Access settings.

### 5.3 Turnstile

- [ ] Turnstile → Add widget: name "Booking", hostname `bookings.bergerhouseboats.com.au`, mode **Managed**. For dev, a separate
      widget "Booking (dev)" with hostname `berger-booking-dev.pages.dev`.
- [ ] Site key → GitHub environment variable `TURNSTILE_SITE_KEY`; secret key → `turnstile-secret` GCP secret.

### 5.4 Rate limiting (nothing to configure)

There is no Cloudflare rule: the booking subdomain is not in your Cloudflare zone, and WAF rate limiting is a paid
feature. Instead `public-api` limits each visitor IP to **30 requests per minute** on `/api/*` (the Stripe webhook is
exempt) and answers "Too many requests" beyond that. It runs in dev and prod. `public-api` is deployed with a single
instance so the limit is exact; an operator can change it with the `RATE_LIMIT_PER_MINUTE` environment variable on the
Cloud Run service (`0` turns it off).

## 6. Seed and complete the data

- [ ] Ask your developer to run the seed once against the environment (safe to repeat; it never overwrites edits).
      `--no-launch-profile` matters: the launch profile selects `Local`, which points at the emulator.
      ```bash
      ASPNETCORE_ENVIRONMENT=Production FIRESTORE_PROJECT_ID=<PROJECT_ID> \
      SEED_STAFF_ALERT_EMAIL=info@bergerhouseboats.com.au \
      SEED_CONTACT_PHONE="(02) xxxx xxxx" SEED_CONTACT_EMAIL=info@bergerhouseboats.com.au \
      dotnet run --no-launch-profile --project src/BergerHarbour.AdminApi -- seed
      ```
      (For dev use `ASPNETCORE_ENVIRONMENT=Development` and the dev project ID.)
      (needs `gcloud auth application-default login` with an account that can write Firestore.)
- [ ] Open `https://berger-admin.pages.dev` and sign in with Google.
- [ ] **Settings → General**: check the default deposit ($1,000), lead time (30 days), bookings open until, staff
      alert email, contact phone and email, hire terms URL.
- [ ] **Fleet**: for every boat, check the **slug matches its WordPress page URL**, then enter the number of adult
      beds and kids beds (0 if none), bedding description, security bond, a deposit only if it differs from the
      default, rates for every season (Mid-week, Weekend, Week, and Long weekend), allowed add-ons, and tick
      **Active**. Pacific Blue is pre-filled as the reference boat — check its figures too.
- [ ] **Settings → Add-ons**: enter prices for add-ons that are not "price on request" (Welcome hamper and Skipper
      stay price on request), put them in the order customers should see with ▲/▼, and check Car parking's
      capacity (11 car spots, shared by every booking).
- [ ] **Settings → Blocked periods**: enter Easter and every long weekend up to "bookings open until" (Christmas /
      New Year 2026–27 is pre-filled with the extended payment schedule).
- [ ] **Settings → Email templates**: review the wording of each template and press **Send test** for each.

For dev, the data only needs to be good enough to test with; the remaining steps of this section are optional.

## 7. Enter existing future bookings (prod only, before go-live)

- [ ] For every **future** booking on paper/Excel, create a staff booking in **Bookings → + New** with the
      customer's email, correct boat, dates, adults and children, and agreed hire price.
      Note: creating a non-stand-by booking emails the customer a confirmation — let customers know the new system
      is coming, or enter them as you contact them.
- [ ] Record payments already received with **Add manual payment** (amount, date, note such as "Deposit — EFT").
- [ ] Enter stand-by (store credit) customers with the **Stand-by** box ticked.
- [ ] Past bookings are **not** entered.

## 8. End-to-end test in Stripe test mode, then go live

Run the tests below on dev first. The go-live steps are prod only.

In test mode (cards: `4242 4242 4242 4242` succeeds, `4000 0025 0000 3155` asks for 3-D Secure, any future expiry
and any CVC):

- [ ] Book Pacific Blue online on desktop (inside the WordPress modal) and on a phone (new tab).
- [ ] Check: only the deposit is charged, the booking appears **Active** in the admin timeline, the customer now
      appears under **Customers**, the customer gets email #1 and staff get #2, and the Stripe receipt arrives.
- [ ] In the admin app, enter a price for a price-on-request add-on and press **Send payment link**; pay through the
      link; check email #5 and the booking's payment status.
- [ ] Start a booking, enter your details and stop: about two hours later staff get email #10 (unfinished online
      booking), and no customer record is created.
- [ ] Try two browsers booking the same dates at once: one must get "Sorry, those dates were just taken".
- [ ] Try a Christmas date: the "please call or email us" pop-up appears.
- [ ] Check Cloud Scheduler's last run succeeded.
- [ ] **Go live**: in Stripe switch to live mode, create a **new live webhook endpoint** (same URL and events), then
      update the `stripe-secret-key` and `stripe-webhook-secret` secrets with the live values and the GitHub
      `production` environment variable `STRIPE_PUBLISHABLE_KEY` with `pk_live_…`. Switch the Turnstile keys if you
      used test ones. Re-run the latest release's **Release (production)** workflow.
- [ ] Make one small real booking and refund it in Stripe, then cancel it in the admin app.

## 9. Add the Book Now button to WordPress (prod only)

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
