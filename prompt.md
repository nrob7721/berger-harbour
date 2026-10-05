# Berger Houseboats — House Boat Booking System Specification

You are building an online booking system for **Berger Houseboats** (NSW, Australia). This document is the complete specification. Every decision in it has been agreed with the business; do not re-open them. Where something is genuinely not covered, choose the simplest option consistent with this document and list it under "Assumptions made" in your final summary.

---

## 1. Context

Berger Houseboats hires out 14 house boats and 5 BBQ boats.

- BBQ boats are already booked through a third-party online system. That stays as is.
- House boats can currently only be booked by phone. Availability lives on paper and is duplicated in Excel. Staff cannot keep up with availability enquiries.
- Customers learn about each house boat on the existing WordPress site (`https://bergerhouseboats.com.au/houseboats/<boat-slug>/`). Each boat has its own page.

**Goal:** let customers see availability and book and pay a deposit for a house boat online, from a "Book Now" button on each boat's WordPress page. Give staff an internal system to manage bookings, the fleet, customers and settings.

## 2. Scope

### In scope (v1)
- A customer booking app, embedded in the WordPress site.
- Online deposit payment, plus later balance payments through emailed payment links (Stripe).
- Automatic notification emails (Brevo).
- An internal admin app with Bookings, Fleet, Customers and Settings pages.
- Backend APIs, Firestore persistence, GCP and Cloudflare infrastructure, CI/CD and a setup checklist.

### Out of scope (v1) — do not build
- BBQ boats. `BoatType.BBQ` exists in the model for future use only. No BBQ boats are seeded or shown.
- Integration with the existing BBQ booking system.
- Customers cancelling or changing a booking themselves. Customers contact staff.
- Refunds through the system. Staff refund in the Stripe dashboard; the system only records the booking status.
- Taking or holding the security bond. It is displayed only.
- Online booking of long weekends, holiday periods or multi-period stays. These are staff-only.
- Importing historical bookings.
- Formal tax invoices. Stripe receipts are enough.
- Terraform / IaC. Use a documented manual setup checklist.
- A separate dev/staging environment. There is one prod GCP project; development is local, against emulators.
- Drag-and-drop on the admin calendar.

## 3. Glossary

| Term | Meaning |
|---|---|
| **Period type** | `Midweek` (Mon→Fri), `Weekend` (Fri→Mon), `Week` (Mon→Mon or Fri→Fri), `LongWeekend` (staff only), `Custom` (staff only, any dates). |
| **Stay** | A booking's dates as a half-open range `[StartDate, EndDate)`. `StartDate` is the check-in day (from **1:00pm**) and `EndDate` is the check-out day (by **8:00am**). The nights occupied are `StartDate … EndDate−1`. Times are fixed and not configurable. |
| **Same-day changeover** | Allowed. A booking ending Monday 8am and another starting Monday 1pm do not overlap. |
| **Blocked period** | A staff-defined date range (Christmas/New Year, Easter, long weekends, …) that customers cannot book online. Any online stay that overlaps it shows a "please call or email us" message. Defined by inclusive `FirstNight` and `LastNight`. |
| **Unavailability** | A staff-defined range when a specific boat cannot be hired (maintenance, etc.). Defined by inclusive `FirstNight` and `LastNight`. |
| **Stand-by booking** | A "store credit" style booking (e.g. a customer with credit waiting for a date). It **never blocks availability**, is **never shown to customers**, **sends no emails**, and is **superseded** when an Active non-stand-by booking overlaps it. |
| **Hold** | A `PendingPayment` booking created when a customer starts checkout. It blocks the dates until payment completes or the hold expires. |
| **Season** | A named recurring day-month range (e.g. Off Peak 1 May–31 Aug) used for pricing. Dates not covered by a season fall in the default **Normal** season. |
| **Extended payment schedule** | Applies to bookings whose stay overlaps a blocked period flagged `UsesExtendedPaymentSchedule` (Christmas/New Year). 50% of the total is due 120 days before check-in and 100% by 90 days before. All other bookings use the **standard schedule**: 100% due 30 days before check-in. |

**Overlap rules.** Every overlap check in the system compares **nights**:
- Booking vs booking: `a.Start < b.End && b.Start < a.End`.
- Booking vs blocked period or unavailability (inclusive nights `[F, L]`): `booking.Start <= L && F < booking.End`.

Example: Midweek Mon 28/09 → Fri 02/10 does **not** overlap a long weekend with FirstNight Fri 02/10.
Example: Midweek Mon 17/12 → Fri 21/12 **does** overlap Christmas with FirstNight 20/12.

All dates are **Australia/Sydney local dates**. "Today" always means today in Australia/Sydney.

## 4. Decision log (do not re-litigate)

1. House boats only. BBQ is a future enum value only.
2. Pricing is calculated by the system per boat × season × period type. The price is **snapshotted onto the booking** when it is created.
3. The deposit is a Settings value (default **$1000 AUD**) and counts toward the hire fee. It is charged at online booking. If the balance due date is today or earlier at booking time, the **full hire price** is charged at checkout instead. The deposit charged is `min(deposit, hire price)`.
4. Balances are paid through payment links in emails. These lead to our own pay page, which creates a Stripe Checkout session for the amount currently due.
5. Payment provider: **Stripe**, using **Embedded Checkout**, because the booking app runs inside an iframe and Stripe's hosted Checkout cannot be framed.
6. Online bookings cover exactly one Midweek, Weekend or Week period. There are no consecutive or multi-period online bookings.
7. Calendar click rule: in the selected mode, a click selects **the period of that type that contains the clicked day**. In Week mode, only Mondays and Fridays are clickable, and the clicked day is the start.
8. Long weekends, Christmas/New Year and Easter are all **blocked periods**, entered yearly by staff in Settings. Only Christmas/New Year uses the extended payment schedule.
9. Overdue balances are **never auto-cancelled**. The customer is reminded, staff are alerted, and staff decide.
10. **Emails the system sends** (exactly these):
    - #1 booking confirmed
    - #2 staff alert for a new online booking
    - #4 payment due, with a link
    - #5 payment received
    - #6 pre-hire instructions
    - #7 staff alert for an overdue balance
    - #8 staff alert for a superseded stand-by

    **The system does NOT email customers about cancellations or add-on confirmations/declines.** Staff contact the customer by hand for both. This is deliberate.
11. Database: **Firestore**. Every Firestore-specific detail, including the per-boat lock document, lives in the Infrastructure layer so the database can be swapped later.
12. Admin authentication: Cloudflare Access with Google SSO (one Google Workspace account in v1, more later) **and** Country = AU. The admin API also validates the Access JWT. NSW-only geo-restriction is not possible in Access policies and is not required.
13. No Cloudflare Tunnel, because it is incompatible with Cloud Run scaling to zero. There are two Cloud Run services: `public-api` and `admin-api`.
14. Customer email is **mandatory** everywhere, including staff-created bookings. Customers are matched by normalised email.
15. A booking that overlaps an Active or PendingPayment booking, or an unavailability, is **never** allowed, for staff or customers. Stand-by bookings are the exception: they ignore other stand-bys and are superseded rather than blocked.
16. Frontend: TypeScript + Vite, **no UI framework**, separate HTML/CSS/TS files. Hosted on Cloudflare Pages.
17. Minimum lead time for online bookings is **30 days** (Settings). The latest date bookable online is the **"Bookings open until"** date (Settings), which staff move forward each year after entering the next year's blocked periods and prices. There is no rolling window.
18. The Customers admin page is **read-only** in v1.

## 5. Architecture overview

```
WordPress boat page ──(HTML block: "Book Now")──► modal <iframe> (desktop) / new tab (mobile)
                                                        │
                                       book.bergerhouseboats.com.au  (Cloudflare Pages: booking app)
                                                        │  /api/* → Pages Function proxy
                                                        ▼
                                              Cloud Run: public-api  ◄── Stripe webhooks
                                                        │
                                                   Firestore  ◄── Cloud Run: admin-api ◄── Cloud Scheduler (OIDC)
                                                                        ▲
                                                        │  /api/* → Pages Function proxy
                                       admin.bergerhouseboats.com.au  (Cloudflare Pages: admin app,
                                                                       behind Cloudflare Access)
External: Stripe (payments), Brevo (email), Cloudflare Turnstile (bot protection)
```

- **GCP region:** `australia-southeast1` (Sydney) for Cloud Run, Firestore (Native mode) and Cloud Scheduler.
- **Same-origin APIs:** each Pages project has a Pages Function at `functions/api/[[path]].ts` that proxies `/api/*` to its Cloud Run service. The proxy:
  - adds a shared secret header `X-Edge-Proxy-Secret`, which both APIs require on all routes except the Stripe webhook and the jobs endpoint;
  - forwards `Cf-Access-Jwt-Assertion` (admin only);
  - forwards `CF-Connecting-IP`.

  This avoids Cloud Run custom domain mapping and lets the frontends call APIs same-origin.
- **CORS:** both APIs only allow their own frontend origin (`https://book.bergerhouseboats.com.au` and `https://admin.bergerhouseboats.com.au` respectively). There are no wildcard origins and credentials are not allowed cross-origin. Since traffic is proxied same-origin, CORS is defence in depth.
- **Cold starts:** both services run with `min-instances=0` and are published with ReadyToRun. There is no separate "wake-up" endpoint. The booking app calls `GET /api/boats/{slug}` as soon as it loads, which wakes the service while a skeleton loader is shown.

## 6. Repository layout

```
/src
  BergerHarbour.Domain/            # aggregates, value objects, domain services, repository interfaces
    Boats/  Bookings/  Customers/  Unavailabilities/  Settings/  Shared/
  BergerHarbour.Application/       # use cases, validation strategies, ports (IPaymentGateway, IEmailSender, IClock, ITurnstileVerifier, IBoatScheduleLock, ...)
  BergerHarbour.Infrastructure/    # Firestore/, Stripe/, Brevo/, Turnstile/, Cloudflare/ (Access JWT validation)
  BergerHarbour.PublicApi/         # ASP.NET Core controllers for customers + Stripe webhook
  BergerHarbour.AdminApi/          # ASP.NET Core controllers for staff + jobs endpoint
/tests
  BergerHarbour.Domain.Tests/
  BergerHarbour.Application.Tests/
  BergerHarbour.Infrastructure.IntegrationTests/   # against the Firestore emulator
/web
  shared/        # generated API types, date/period utilities, shared CSS tokens
  booking/       # Vite app + functions/api/[[path]].ts
  admin/         # Vite app + functions/api/[[path]].ts
/wordpress
  book-now-block.html             # the HTML block pasted into each boat page
/docs
  setup-checklist.md              # GCP, Cloudflare, Stripe, Brevo, DNS migration, go-live
firestore.indexes.json
.github/workflows/
```

## 7. Backend

### 7.1 Technology and style
- **.NET 10**, ASP.NET Core with controllers, C#. Nullable reference types enabled.
- **DDD:**
  - Validation and invariants are encapsulated in domain objects (constructors, factory methods and intention-revealing methods such as `booking.Cancel()`, `booking.RecordPayment(...)`, `boat.Deactivate()`). Domain objects are never in an invalid state. There are no public setters on aggregates.
  - **Small aggregates referencing each other by Id** (Vaughn Vernon). Aggregates: `Boat`, `Booking`, `Customer`, `BoatUnavailability`, `Season`, `BlockedPeriod`, `AddonDefinition`, `EmailTemplate`, `BusinessSettings`. If a natural root appears later, they can be merged; do not pre-merge.
  - **Domain services** for cross-aggregate rules, e.g. `BookingAvailabilityService` (overlap checks given loaded bookings, unavailabilities and blocked periods), `PricingService`, `PaymentScheduleService`, `StayPeriodFactory` (period type + clicked date → stay).
- **Repositories:** one repository and interface per aggregate. **Interfaces live next to their aggregate in the Domain project** (e.g. `Domain/Bookings/IBookingRepository.cs`). Implementations live in `Infrastructure/Firestore`.
- **Validation strategy pattern** for booking requests: `IBookingRequestValidationStrategy`.
  - `OnlineBookingValidationStrategy` is registered in `PublicApi`. It enforces period shape, lead time, open-until, blocked periods, guest limits, rooming warning acceptance, group restriction answer, terms acceptance and allowed add-ons.
  - `InternalBookingValidationStrategy` is registered in `AdminApi`. It enforces only `StartDate < EndDate`, `NumberOfGuests >= 1`, and a valid customer.
  - Both strategies are always followed by the availability invariant (decision 15), which is not part of either strategy.
- **Money:** `decimal` AUD in the domain, stored as integer cents. All amounts include GST.
- **Dates:** `DateOnly` in the domain, stored as `yyyy-MM-dd` strings, which sort correctly for range queries. Instants are stored as UTC timestamps.
- `IClock` abstraction for all time access (tests must control time).
- **Optimistic concurrency:** every aggregate has an integer `Version`. Admin update requests include the version. A mismatch returns `409 Conflict` with "This record was changed elsewhere — reload and retry."
- OpenAPI document generated by both APIs. TypeScript types for the frontends are generated from it (e.g. `openapi-typescript`) as part of the web build.

### 7.2 Atomic booking (race conditions)
Any write that can create or move occupancy for a boat (create or update a booking, activate a hold, create or update an unavailability) must run inside a **per-boat exclusive section**:

- Application port: `IBoatScheduleLock.RunExclusiveAsync<T>(BoatId boatId, Func<IBoatScheduleSession, Task<T>> work)`. The session exposes transaction-bound reads and writes for bookings and unavailabilities of that boat.
- **Firestore implementation (Infrastructure only):** a Firestore transaction that first reads and then updates `boatLocks/{boatId}` (incrementing a counter). It queries that boat's potentially overlapping bookings (`boatId == X`, `status in [Active, PendingPayment]`, `startDate < newEnd`, then filters `endDate > newStart` in memory) and unavailabilities. It runs the domain availability check and writes. Concurrent transactions on the same boat are serialised by the lock document.
- A future relational implementation would use `SELECT … FOR UPDATE` on the boat row or a serialisable transaction, with no Domain or Application changes.
- **Integration test (required):** two concurrent requests for the same boat and overlapping dates → exactly one succeeds and the other gets `409 Conflict` "Those dates are no longer available".

### 7.3 Domain model

#### Boat (aggregate)
| Field | Notes |
|---|---|
| `Id` | |
| `Name` | required |
| `Slug` | required, unique, kebab-case. Matches the WordPress URL segment (e.g. `pacific-blue`). |
| `Type` | `House` \| `BBQ` (BBQ is future use) |
| `MaxNoOfGuests` | ≥ 1. Online guest dropdown is `1…MaxNoOfGuests`. |
| `NoOfBeds` | Number of physical beds; a double or queen counts as **one** bed. Drives the rooming warning. |
| `BeddingDescription` | Free text, e.g. "4 queen bedrooms, bunk area (2 singles), single in lounge, single in dining area" |
| `SecurityBond` | Money, display only |
| `Rates` | List of `BoatRate { SeasonId, PeriodType (Midweek/Weekend/Week/LongWeekend), Price }` |
| `AllowedAddonIds` | Add-ons offered for this boat |
| `IsActive` | Inactive boats cannot be booked online or by staff, but their history is kept. Boats are never deleted. |
| `CreatedDate`, `ModifiedDate`, `Version` | |

**Invariant:** a boat can only be **activated** when `NoOfBeds`, `BeddingDescription` and a rate for **every season × {Midweek, Weekend, Week}** are present.

#### Customer (aggregate)
`Id`, `FullName` (required), `Email` (required, stored normalised to lower-case trimmed, **unique**), `MobileNumber` (required for online bookings, optional for staff-created), `CreatedDate`, `ModifiedDate`, `Version`.

- Email uniqueness is enforced with a `customerEmails/{normalisedEmail}` index document written in the same transaction.
- When an existing email books again, `FullName` and `MobileNumber` are updated to the latest values.

#### BoatUnavailability (aggregate)
`Id`, `BoatId`, `FirstNight`, `LastNight` (inclusive, `FirstNight <= LastNight`), `Comments`, `CreatedDate`, `ModifiedDate`, `Version`.

Creating or editing one that overlaps an Active or PendingPayment non-stand-by booking is rejected. The error names the booking reference(s).

#### Booking (aggregate)
| Field | Notes |
|---|---|
| `Id` | |
| `Reference` | Human-readable `BH-YYNNNN` (e.g. `BH-260143`). YY = year created, NNNN = per-year sequence from a counter document incremented transactionally. |
| `CreatedBy` | `Customer` (website) \| `SystemUser` (staff) |
| `IsStandby` | bool |
| `Status` | `PendingPayment` \| `Active` \| `Superseded` \| `Cancelled` \| `Expired` |
| `CustomerId`, `BoatId` | Id references |
| `PeriodType` | `Midweek` \| `Weekend` \| `Week` \| `LongWeekend` \| `Custom` |
| `StartDate`, `EndDate` | Stay `[Start, End)`, `Start < End` |
| `NumberOfGuests` | ≥ 1 |
| `Comments` | optional |
| `HirePrice` | Snapshot at creation. Staff may override. |
| `DepositAmount` | Snapshot of the deposit setting at creation |
| `PaymentSchedule` | `Standard` \| `Extended` (derived from blocked-period overlap at creation, and re-derived when dates change) |
| `AddonLines` | List of `AddonLine { Id, AddonId, NameSnapshot, Quantity, UnitPrice (nullable while price on request), Status: Requested \| Confirmed \| Declined }` |
| `Payments` | List of `Payment { Id, Amount, Method: Stripe \| Manual, StripePaymentIntentId?, PaidAt, Note?, RecordedBy: Customer \| SystemUser }` |
| `HoldExpiresAt` | Instant, only while `PendingPayment` |
| `StripeCheckoutSessionId` | Of the deposit checkout |
| `RoomingWarningAcceptance` | `{ AcceptedAt, WarningTextShown }`, present when guests > `NoOfBeds` on an online booking |
| `TermsAcceptedAt` | Online bookings |
| `GroupRestrictionDeclaredNotApplicable` | Online bookings: the customer declared they are **not** an under-30s or all-male group |
| `PaymentLinkTokenHash` | SHA-256 of the random token used in payment links |
| `SentNotifications` | List of `{ Key, SentAt }`, used for idempotent emails |
| `CreatedDate`, `ModifiedDate`, `Version` | |

**Derived values (calculated, never stored as the source of truth):**
- `TotalPrice` = `HirePrice` + Σ(`Quantity × UnitPrice`) over **Confirmed** add-on lines with a price.
- `AmountPaid` = Σ payments.
- `AmountOwing` = `max(0, TotalPrice − AmountPaid)`.
- `PaymentStatus` (replaces the stored enum from the original brief):
  - `Outstanding` if `AmountPaid == 0`
  - else `FullyPaid` if `AmountPaid >= TotalPrice`
  - else `DepositPaid` if `AmountPaid <= DepositAmount`
  - else `PartPaid`

**Status transitions:**
- `PendingPayment → Active` when the deposit payment succeeds.
- `PendingPayment → Expired` when the hold lapses or the Stripe session expires.
- `Active → Cancelled` (staff).
- `Cancelled → Active` (staff, subject to the availability check).
- `Active (stand-by) → Superseded`. This is automatic when an Active non-stand-by booking for the same boat overlaps it.
- Staff-created bookings start as `Active` (no hold).

**What blocks availability:** non-stand-by bookings with status `Active`, and non-stand-by `PendingPayment` bookings whose `HoldExpiresAt` is in the future. Also boat unavailabilities, and (online only) blocked periods. Stand-by, Superseded, Cancelled and Expired bookings never block.

**Stand-by bookings:**
- They may not overlap Active or PendingPayment non-stand-by bookings or unavailabilities. Creating one on taken dates is pointless.
- They may overlap other stand-bys.
- When any non-stand-by booking **becomes Active**, all overlapping Active stand-by bookings for that boat become `Superseded` and email #8 is sent.

#### Settings aggregates
- `BusinessSettings` (singleton):
  - `DepositAmount` (default 1000)
  - `MinimumLeadTimeDays` (default 30)
  - `BookingsOpenUntil` (date)
  - `StaffAlertEmail`
  - `ContactPhone`, `ContactEmail` (shown in "please enquire" messages)
  - `HireTermsUrl` (default `https://bergerhouseboats.com.au/hire-information/hire-terms-and-procedures/`)
  - `Version`
- `Season`: `Id`, `Name`, `Ranges: [{ StartDayMonth, EndDayMonth }]` (recurring every year; a range may wrap the year end, e.g. 01-12 → 31-01), `Version`.
  - Exactly one built-in season `Normal` has no ranges and is the default. It cannot be deleted.
  - Season ranges must not overlap each other.
- `BlockedPeriod`: `Id`, `Name`, `FirstNight`, `LastNight` (inclusive), `UsesExtendedPaymentSchedule` (bool), `Version`.
- `AddonDefinition`:
  - `Id`, `Name`, `Description`
  - `PriceOnRequest` (bool). When true, staff set the price per booking line.
  - `Prices` by period type (Midweek, Weekend, Week; ignored when price on request)
  - `QuantityApplies` (bool; when false, quantity is always 1)
  - `IsActive`, `Version`
- `EmailTemplate`: `Key` (enum, see §9), `Subject`, `HtmlBody`, `Version`. Defaults are seeded at setup. Saving a template with an unknown placeholder is rejected.

### 7.4 Pricing (`PricingService`)
- **Season** = the season whose range contains the booking's `StartDate`, or `Normal`.
- **Hire price** = the boat's rate for (season, period type).
- **Add-on unit price** = the add-on's price for the booking's period type, or `null` if `PriceOnRequest`.
- Online: requested add-ons are shown with estimated prices (or "price to be confirmed"). They are **not** included in `TotalPrice` until staff confirm them.
- Staff bookings:
  - For `Midweek`, `Weekend`, `Week` and `LongWeekend`, the hire price is pre-filled from the rate table.
  - For `Custom` (e.g. Christmas 7 nights), staff type the price.
  - Staff may always override.
  - A "Recalculate from rates" action re-prices on request. Changing dates does **not** silently re-price.
- The price is always calculated on the server. Prices sent by the client are ignored.

### 7.5 Payment schedule (`PaymentScheduleService`)
Milestones are instants relative to check-in (StartDate 13:00 Australia/Sydney). Offsets come from configuration (§10.2).

| Schedule | Milestones (required cumulative amount paid) |
|---|---|
| Both | **Deposit**: `DepositAmount`, due at booking creation. Not reminded by the job; online it is paid at checkout and staff bookings use "Send payment link". |
| Standard | **Balance**: 100% of `TotalPrice`, due `check-in − BalanceDueBeforeHire` (30d) |
| Extended | **First instalment**: 50% of `TotalPrice`, due `check-in − ExtendedFirstInstalmentBeforeHire` (120d). **Final**: 100%, due `check-in − ExtendedFinalInstalmentBeforeHire` (90d). |

- **Amount due now** (used by the pay page) = the largest shortfall `requiredCumulative − AmountPaid` among milestones whose reminder window has opened (`now >= due − PaymentReminderBeforeDue`). If there is none, it is `AmountOwing`. This covers ad-hoc links after a price-on-request add-on is confirmed on an already-paid booking.
- **Online checkout amount** = `min(DepositAmount, HirePrice)`, or the full `HirePrice` if the balance milestone is due at or before now.

### 7.6 Online booking rules (`OnlineBookingValidationStrategy`)
1. The boat exists, `IsActive`, and `Type == House`.
2. `PeriodType ∈ {Midweek, Weekend, Week}`:
   - Midweek starts Monday and ends Friday (+4).
   - Weekend starts Friday and ends Monday (+3).
   - Week starts Monday or Friday and ends +7.
3. `StartDate >= today + MinimumLeadTimeDays`.
4. `EndDate − 1 <= BookingsOpenUntil` (every night is inside the open window).
5. The stay does not overlap any `BlockedPeriod`.
6. `1 <= NumberOfGuests <= MaxNoOfGuests`.
7. If `NumberOfGuests > NoOfBeds`: rooming acceptance is present and its text matches the warning the server would generate.
8. The group restriction is declared not applicable (a "Yes" answer never reaches the API; the UI stops the booking).
9. Terms accepted.
10. Every add-on is active and in `boat.AllowedAddonIds`. `Quantity >= 1`, and it is 1 when `!QuantityApplies`.
11. Name, email (valid format) and mobile are required.
12. The Turnstile token is verified server-side (in the use case, before the transaction).

Then the availability invariant runs inside `IBoatScheduleLock`.

## 8. API

All routes are under `/api`. JSON uses camelCase, dates are `yyyy-MM-dd`, and money is a decimal number in AUD. Validation failures return `400` with ProblemDetails and per-field errors. Overlaps return `409`.

### 8.1 public-api (customer-facing, no login)
| Method & route | Purpose |
|---|---|
| `GET /api/boats/{slug}` | Customer-safe boat data: name, maxNoOfGuests, noOfBeds, beddingDescription, securityBond, isActive, allowed add-ons (name, description, priceOnRequest, quantityApplies, prices by period type), plus booking settings (minimum lead time, bookings open until, contact phone/email, hire terms URL). Also the warm-up call. |
| `GET /api/boats/{slug}/availability?from=&to=` | Returns only **ranges**, never booking or customer data: `{ unavailable: [{start, end}], enquireOnly: [{firstNight, lastNight}] }`. `unavailable` = blocking bookings (as `[start,end)`) and unavailabilities. `enquireOnly` = blocked periods. Max span 15 months. |
| `POST /api/boats/{slug}/quote` | `{ periodType, startDate, numberOfGuests, addons[] }` → hire price, season name, add-on estimates, amount due at checkout, payment schedule with due dates, rooming warning text if applicable. Runs the online validation rules (except Turnstile, terms and acceptance) and returns their errors. |
| `POST /api/bookings` | Body: `{ slug, periodType, startDate, numberOfGuests, fullName, email, mobile, addons[{addonId, quantity}], roomingWarningAccepted, roomingWarningText, groupRestrictionApplies: false, termsAccepted, turnstileToken }`. Validates, upserts the customer, creates a `PendingPayment` hold (atomic), creates a Stripe Embedded Checkout session (`expires_at` = now + hold duration, minimum 30 min), and returns `{ reference, clientSecret }`. |
| `GET /api/bookings/{reference}/status?session_id=` | Used by the completion screen. Returns status and basic summary only if `session_id` matches the booking. |
| `GET /api/payments/{token}` | Pay page: booking reference, boat, dates, total, paid, owing, amount due now. |
| `POST /api/payments/{token}/checkout` | Creates an Embedded Checkout session for the amount due now and returns `clientSecret`. Returns 400 if nothing is owing. |
| `POST /api/stripe/webhook` | Stripe-signature verified. Exempt from the proxy secret. See §8.3. |

**Abuse protection:**
- Turnstile is required on `POST /api/bookings`.
- A Cloudflare rate-limiting rule applies to `book.bergerhouseboats.com.au/api/*`.
- The proxy secret is required (except on the webhook).
- Strict CORS.
- Request size limits.
- All input is validated server-side.

### 8.2 admin-api (staff only)
**Authentication middleware** (all routes except jobs):
- Validate `Cf-Access-Jwt-Assertion` against the team's JWKS (`https://<team>.cloudflareaccess.com/cdn-cgi/access/certs`): signature, expiry, `aud` = the Access application AUD tag.
- Check the `email` claim is in `ADMIN_ALLOWED_EMAILS`.
- Require the proxy secret.

| Area | Routes |
|---|---|
| Bookings | `GET /api/bookings?from=&to=&includeInactive=` (timeline data incl. customer name, status, isStandby, paymentStatus, reference) · `GET /api/bookings/search?q=` (name, email or reference) · `GET /api/bookings/{id}` · `POST /api/bookings` · `PUT /api/bookings/{id}` (boat, dates, period type, guests, standby, comments, hire price, status Active/Cancelled; includes `version`) · `POST /api/bookings/{id}/recalculate-price` · `POST /api/bookings/{id}/addons` · `PUT /api/bookings/{id}/addons/{lineId}` (quantity, unit price, status) · `DELETE /api/bookings/{id}/addons/{lineId}` · `POST /api/bookings/{id}/payments` (manual payment: amount, date, note) · `POST /api/bookings/{id}/send-payment-link` (sends email #4 immediately for the amount due now) · `GET /api/bookings/quote?boatId=&periodType=&startDate=` (pre-fill price) |
| Fleet | `GET /api/boats` · `GET /api/boats/{id}` · `POST /api/boats` · `PUT /api/boats/{id}` (all editable fields incl. rates, allowed add-ons, active flag) · `GET /api/boats/{id}/unavailabilities` · `POST /api/boats/{id}/unavailabilities` · `PUT /api/boats/{id}/unavailabilities/{uid}` · `DELETE /api/boats/{id}/unavailabilities/{uid}` |
| Customers | `GET /api/customers?q=` (read-only list) · `GET /api/customers/by-email?email=` (used by the booking dialog to pre-fill) |
| Settings | `GET/PUT /api/settings/business` · CRUD `/api/settings/seasons` · CRUD `/api/settings/blocked-periods` · CRUD `/api/settings/addons` (delete = deactivate) · `GET /api/settings/email-templates` · `PUT /api/settings/email-templates/{key}` · `POST /api/settings/email-templates/{key}/test` (sends a sample to the staff alert address) |
| Jobs | `POST /api/jobs/process-notifications`. **Not** behind Access. It validates a Google OIDC token: issuer Google, audience = service URL, email = the Cloud Scheduler service account. |

Staff-created bookings (`POST /api/bookings`):
- Email and name are required. The customer is matched or created by email.
- Status is `Active` immediately. `CreatedBy = SystemUser`.
- Validated by `InternalBookingValidationStrategy` plus the availability invariant.
- If it is not a stand-by, email #1 is sent.
- If it becomes Active and overlaps stand-bys, they are superseded.

### 8.3 Stripe
- Only synchronous methods (card + wallets) are allowed, so `checkout.session.completed` means paid.
- Checkout session `metadata`: `bookingId`, `purpose` (`Deposit` | `Payment`).
- **`checkout.session.completed`:**
  - Record a `Payment`. This is idempotent on PaymentIntent id.
  - If `purpose == Deposit` and the booking is `PendingPayment`, activate it inside `IBoatScheduleLock`, supersede stand-bys, then send #1 to the customer and #2 to staff.
  - If activation is impossible (the hold had already expired and the dates were taken), keep the payment recorded, leave the booking `Expired`, and log an **error**. The log-based alert notifies staff, who refund by hand.
  - If `purpose == Payment`, send #5.
- **`checkout.session.expired`:** if the booking is still `PendingPayment` with that session id, set it to `Expired`.
- The webhook returns 2xx only after persistence succeeds, so Stripe retries on failure.
- Apple Pay / Google Pay should work inside the WordPress modal (iframe has `allow="payment"`; register the payment method domains in Stripe). Card payment must work regardless.

## 9. Notifications

**Provider:** Brevo transactional email API. Sender `bookings@bergerhouseboats.com.au`. Sending is behind the `IEmailSender` port; local development uses a fake that writes emails to the console or a folder.

| # | Key | To | Trigger |
|---|---|---|---|
| 1 | `BookingConfirmed` | Customer | Online: deposit paid (booking becomes Active). Staff: non-stand-by booking created. Includes the deposit receipt, payment schedule, security bond notice, requested add-ons "subject to confirmation", and the hire terms link. |
| 2 | `StaffNewOnlineBooking` | Staff alert address | Online booking becomes Active |
| 4 | `PaymentDue` | Customer | For each Balance / First / Final milestone with a shortfall: once at `due − PaymentReminderBeforeDue` (7d) and once at `due`. Also immediately from "Send payment link". Contains `{{PaymentLink}}`. |
| 5 | `PaymentReceived` | Customer | Any Stripe payment other than the deposit checkout, and any manual payment |
| 6 | `PreHireInstructions` | Customer | `check-in − PreHireInstructionsBeforeHire` (7d). This is the existing "canned message". |
| 7 | `StaffBalanceOverdue` | Staff alert address | Once per milestone, when `now > due` and a shortfall remains |
| 8 | `StaffStandbySuperseded` | Staff alert address | A stand-by is superseded |

Numbers 3 (add-on confirmed/declined) and 9 (cancellation) are **deliberately not implemented** (decision 10).

**Rules:**
- No email of any kind for stand-by bookings, or for bookings that are PendingPayment, Cancelled, Expired or Superseded (except #8 to staff).
- **Idempotency:** before sending, check `SentNotifications` for a key like `PaymentDue:Balance:Reminder`, `PaymentDue:Balance:Due`, `Overdue:Final`, `PreHire`. Record it after a successful send. Manual "Send payment link" sends are not deduplicated.
- **Templates:**
  - Editable in Settings: subject + HTML body. Defaults are seeded at setup with sensible Australian-English wording.
  - Placeholders: `{{CustomerName}} {{BookingReference}} {{BoatName}} {{CheckInDate}} {{CheckOutDate}} {{CheckInTime}} {{CheckOutTime}} {{NumberOfGuests}} {{HirePrice}} {{TotalPrice}} {{AmountPaid}} {{AmountOwing}} {{AmountDue}} {{DueDate}} {{PaymentLink}} {{SecurityBond}} {{RequestedAddons}} {{PaymentSchedule}} {{HireTermsUrl}} {{ContactPhone}} {{ContactEmail}}`
  - Values are HTML-encoded. Dates are `dd/MM/yyyy`. Money is `$1,234.00`.
- **Payment link:** `https://book.bergerhouseboats.com.au/pay/?token=<token>`. The token is 32 random bytes, base64url. Only its SHA-256 hash is stored. It is generated with the booking and is stable for the booking's life.

**Scheduled job** `process-notifications`:
- Cloud Scheduler runs it **hourly** (OIDC-authenticated HTTP call to admin-api).
- It is idempotent and safe to run at any frequency. Each run:
  1. Expires `PendingPayment` holds past `HoldExpiresAt`.
  2. Sends due #4, #6 and #7 emails for Active non-stand-by bookings with `StartDate >= today`.
- Cost note: Cloud Scheduler gives 3 free jobs per billing account; this is 1 job, and the hourly invocation fits in Cloud Run's free tier.

## 10. Configuration

### 10.1 Settings page (business data, stored in Firestore)
See the Settings aggregates in §7.3.

### 10.2 Environment variables (timing; shortened for testing)
All offsets are .NET `TimeSpan` strings (`d.hh:mm:ss`), bound with the options pattern. Validate them on startup.

| Variable | Default |
|---|---|
| `Notifications__BalanceDueBeforeHire` | `30.00:00:00` |
| `Notifications__ExtendedFirstInstalmentBeforeHire` | `120.00:00:00` |
| `Notifications__ExtendedFinalInstalmentBeforeHire` | `90.00:00:00` |
| `Notifications__PaymentReminderBeforeDue` | `7.00:00:00` |
| `Notifications__PreHireInstructionsBeforeHire` | `7.00:00:00` |
| `Booking__PendingHoldDuration` | `00:30:00` (Stripe minimum is 30 min; the hold is `max(value, 30min)` + `Booking__PendingHoldGrace`) |
| `Booking__PendingHoldGrace` | `00:10:00` |

### 10.3 Secrets and other config
Stored in Secret Manager and injected as env vars:
- Stripe secret key and webhook signing secret
- Brevo API key
- Turnstile secret
- `EDGE_PROXY_SECRET`
- `CF_ACCESS_TEAM_DOMAIN`, `CF_ACCESS_AUD`, `ADMIN_ALLOWED_EMAILS` (comma-separated)
- `SCHEDULER_SERVICE_ACCOUNT_EMAIL`
- `PUBLIC_BOOKING_BASE_URL`
- the allowed CORS origin

The frontends need the Stripe publishable key and the Turnstile site key at build time.

## 11. Customer booking app (`book.bergerhouseboats.com.au`)

### 11.1 Embedding on WordPress
Each boat page gets this HTML block (deliver it as `/wordpress/book-now-block.html` with the slug as the only thing to change):
- A `<button class="bh-book-now" data-boat="pacific-blue">Book Now</button>`.
- An inline script, guarded so it only initialises once per page. On click:
  - **Mobile** (`matchMedia('(max-width: 768px), (pointer: coarse)')`): open `https://book.bergerhouseboats.com.au/?boat=<slug>` in a new tab.
  - **Desktop:** open an accessible modal overlay (focus trap, Esc to close, close button, body scroll lock) containing an `<iframe src=".../?boat=<slug>" allow="payment" title="Book <boat>">`.
  - It listens for `postMessage` `{ type: 'bh-booking:close' }`, accepted **only** from origin `https://book.bergerhouseboats.com.au`.
- The booking app sends:
  - `Content-Security-Policy: frame-ancestors https://bergerhouseboats.com.au https://www.bergerhouseboats.com.au` (configured in Pages `_headers`)
  - a CSP compatible with Stripe.js and Turnstile.

### 11.2 Flow
The app is a single page with steps. The progress is visible. The layout is responsive and works at 360px width (16px gutters, no horizontal scroll) and in the ~900×700 modal.

1. **Load:** read `?boat=`. Show a skeleton while `GET /api/boats/{slug}` loads.
   - Unknown slug → friendly error.
   - Inactive boat → everything greyed out, with an info message at the top: "This boat is currently unavailable for online booking. Please contact us on {phone} / {email}."
2. **Dates:**
   - Radio buttons: **Mid-week (Mon–Fri)**, **Weekend (Fri–Mon)**, **Week (Mon–Mon or Fri–Fri)**.
   - A classic month calendar with previous/next navigation, a legend, and check-in/check-out times shown (1pm / 8am).
   - Availability is fetched for the visible months.
   - **Clickable days per mode:**
     - Mid-week: Mon–Fri clickable; Sat and Sun disabled.
     - Weekend: Fri, Sat, Sun, Mon clickable; Tue–Thu disabled.
     - Week: only Mondays and Fridays clickable.
   - **Click mapping:** the period of the selected type that contains the day.
     - Mid-week: Mon–Fri of that week. Clicking Fri selects the Mon→Fri ending that day. Clicking a different day in the same period doesn't change the selection.
     - Weekend: the Fri→Mon containing it. Clicking Mon selects the weekend ending that day.
     - Week: Mon→next Mon or Fri→next Fri, starting on the clicked day.
   - The whole selected period is highlighted. The start shows a check-in marker and the end a check-out marker.
   - **Greying is per period, not per day:** a day is disabled in a mode when the period it maps to is unavailable or invalid. For example, a Monday on which a weekend booking checks out is still clickable in Mid-week mode.
   - A period is unavailable if:
     - it overlaps `unavailable` ranges;
     - it starts before today + lead time;
     - it ends after `BookingsOpenUntil`.
   - **Blocked periods:** days inside `enquireOnly` ranges are greyed out with a distinct style. If the period a click maps to overlaps a blocked period, show a pop-up instead of selecting: "Bookings over {period name} must be made by phone or email: {phone} / {email}." Example: Mid-week mode, click Tue 18/12 → Mon 17/12–Fri 21/12 overlaps Christmas → pop-up.
   - Changing the radio option clears the selection.
3. **Guests:**
   - A dropdown from 1 to `MaxNoOfGuests`.
   - If `guests > NoOfBeds`, show a modal **rooming warning** with the bedding description, e.g. "This boat has {NoOfBeds} beds ({BeddingDescription}). With {n} guests, some guests will need to share beds. Please make sure your group's sleeping arrangements suit this layout." It has an **Accept** button and a Cancel button. Cancel returns to the guest selection. The booking cannot continue without accepting. The exact text shown is sent with the booking.
   - Required question: **"Is your group an under-30s group or an all-male group?"** Yes/No. **Yes** shows "Groups of under-30s or all-male groups require prior approval — please contact us on {phone} / {email}" and blocks continuing.
4. **Add-ons:**
   - Checkboxes for the allowed add-ons, with a quantity input where `QuantityApplies`.
   - Each shows a price for the selected period type, or "Price on request".
   - Notice: "Add-ons are requests and will be confirmed by our staff. Confirmed add-ons are added to your balance."
5. **Your details:** Full name, email, mobile (all required, validated), a terms checkbox "I accept the [Hire Terms and Procedures]" (link opens in a new tab), and the Turnstile widget.
6. **Summary:** server quote showing boat, dates and times, guests, season, hire price, requested add-ons (estimated / TBC), **amount payable now**, the balance schedule with due dates, and the security bond notice ("A ${bond} security bond applies — details will be provided"). Button: **Pay ${amount}**.
7. **Payment:** `POST /api/bookings` → mount Stripe Embedded Checkout. If it returns 409, return to step 2 with "Sorry, those dates were just taken."
8. **Complete:** poll `/status` until `Active` (show "Confirming…" for up to ~30s, then say an email will follow). Show the reference and "A confirmation email has been sent to {email}". In the modal, show a **Close** button that posts `bh-booking:close`.

**Pay page** `/pay/?token=`: shows the booking summary, total, paid, owing and amount due now, with a **Pay** button that mounts Embedded Checkout. If nothing is owing, show a "Nothing to pay" state.

Accessibility: keyboard-operable calendar (arrow keys, Enter), visible focus, ARIA labels and live region for selection, contrast AA.

## 12. Admin app (`admin.bergerhouseboats.com.au`)

Behind Cloudflare Access. There is a top menu bar: **Bookings · Fleet · Customers · Settings**. The layout is desktop-first and usable on a tablet. Dates are shown `dd/MM/yyyy`.

### 12.1 Bookings
- **Full-screen timeline:** one row per boat (sticky boat-name column), one column per day of the month, horizontal scroll, Prev/Next month and Today buttons, weekend columns shaded.
- **Bars:**
  - Bookings start at the right half of the check-in cell and end at the left half of the check-out cell, so changeovers are visible.
  - Bar label: customer name + reference.
  - Styles: `Active` (solid, tinted by payment status), `PendingPayment` (dashed outline), stand-by (hatched).
  - Unavailabilities are grey blocks. Blocked periods are a labelled column band across all rows.
  - "Show inactive" toggle reveals Cancelled, Expired and Superseded bookings (faded).
- **"+ New" button**, and clicking an empty cell, opens the booking dialog (the cell pre-fills boat and start date).
- Clicking a bar opens the same dialog in edit mode.
- **Search box:** name, email or reference → results list → opens the dialog.

**Booking dialog fields:**
- Boat
- Period type (Midweek/Weekend/Week/LongWeekend/Custom) — selecting one pre-fills the end date
- Start date, end date
- Number of guests. Show a non-blocking warning if it is above max guests or above the number of beds.
- **Customer:** Email (required; on blur, look up an existing customer and pre-fill), Full name (required), Phone (optional)
- Stand-by checkbox
- Comments
- Hire price (pre-filled, editable, "Recalculate from rates" button)
- Status (Active/Cancelled editable; other statuses read-only)

**Add-ons section:** lines with name, quantity, unit price (editable; required before Confirm when price on request, e.g. the welcome hamper), status (Requested/Confirmed/Declined), and an "Add add-on" control.

**Payments section:**
- A list of payments, plus totals: Total, Paid, Owing, Payment status, and the schedule with due dates.
- **"Add manual payment"** (amount, date, note).
- **"Send payment link"** (disabled when nothing is owing; shows a confirmation toast).

**Read-only info for online bookings:** created by, created date, terms accepted at, rooming warning accepted at + text, group restriction declaration.

**Behaviour:**
- Saving checks overlaps. On conflict, show the conflicting booking references.
- Version conflicts show "reload and retry".

### 12.2 Fleet
- A grid of boat cards: name, max guests, beds, an active/inactive badge.
- "+ New boat".
- Clicking a card opens a dialog with tabs:
  - **Details:** Name, Slug, MaxNoOfGuests, NoOfBeds, BeddingDescription, SecurityBond, Active toggle (activation shows the missing data if the invariant fails).
  - **Rates:** a grid of seasons × {Midweek, Weekend, Week, LongWeekend}.
  - **Add-ons:** checklist of allowed add-ons.
  - **Unavailabilities:** a scrollable table (first night, last night, comments) with edit and delete, and a **"+ New Unavailability"** button.

### 12.3 Customers
- A read-only table: name, email, mobile, created date. A client-side text filter.
- No editing in v1. Contact details are updated from the booking dialog.

### 12.4 Settings
Sections:
- **General:** deposit, minimum lead time, bookings open until, staff alert email, contact phone/email, hire terms URL.
- **Seasons:** list + editor of recurring day-month ranges; overlap validation.
- **Blocked periods:** name, first night, last night, extended payment schedule flag. Help text: "Enter each year's Christmas/New Year, Easter and long weekends. Customers must phone to book these."
- **Add-ons:** catalogue editor.
- **Email templates:** a select list of keys, subject and HTML body editors, a placeholder reference list, and "Send test".

## 13. Seed data (run once at setup; idempotent)

- **Seasons:**
  - Normal (default)
  - Off Peak (01-05 → 31-08)
  - Peak (01-12 → 31-01)
- **Blocked periods:** "Christmas / New Year 2026–27" 20/12/2026 → 05/01/2027, extended schedule = true. Staff add Easter and long weekends.
- **Add-ons** (names from the brief):
  - Yabby pumps, Ice, Bait, Pizza oven, Fishing gear, Car parking, Grocery service, On mooring jetty stays, Outdoor heater
  - Outboard motor: $95 Midweek, $95 Weekend, $130 Week
  - Welcome hamper and Skipper: always `PriceOnRequest = true`
  - All add-ons except the outboard motor are seeded as `PriceOnRequest = true` until staff enter prices.
  - Ice and Bait have `QuantityApplies = true`.
- **Boats** (inactive until completed in Fleet; staff must verify slugs against WordPress URLs):

| Name | Slug | Max guests |
|---|---|---|
| Pacific Blue | pacific-blue | 12 |
| Ocean Spirit | ocean-spirit | 12 |
| Island Dream | island-dream | 12 |
| Kalinda | kalinda | 12 |
| Blue Bayou | blue-bayou | 12 |
| Gypsea Belle | gypsea-belle | 12 |
| Image Anne | image-anne | 12 |
| Paradise II | paradise-ii | 10 |
| JFS Rhyanna | jfs-rhyanna | 8 |
| Marie Claire | marie-claire | 7 |
| Misty Blue | misty-blue | 9 |
| Wavebreak | wavebreak | 4 |
| Wanderer | wanderer | 6 |
| Runaway Bear | runaway-bear | 2 |

  Pacific Blue is seeded complete and active as the reference boat:
  - NoOfBeds 8
  - Bedding: "4 queen bedrooms, bunk area (2 singles), single in lounge, single in dining area"
  - Bond $2,000
  - Rates (Weekend / Midweek / LongWeekend / Week):
    - Normal: 4060 / 4060 / 4620 / 5850
    - Off Peak: 3570 / 3570 / 4050 / 5250
    - Peak: 4500 / 4500 / 5100 / 6200
  - With 8 beds and 12 guests, 9+ guests triggers the rooming warning.
- **Email templates:** defaults for #1, #2, #4, #5, #6, #7, #8.
- **BusinessSettings:** deposit 1000, lead time 30, open-until 30/11/2027, contact phone/email placeholders for staff to fill in.

## 14. Infrastructure and operations

- **GCP (one prod project):**
  - Firestore Native (`australia-southeast1`)
  - two Cloud Run services (min 0; max instances 3 for public, 2 for admin), each with its own service account and least privilege
  - Secret Manager
  - Cloud Scheduler job (hourly → admin-api `/api/jobs/process-notifications`, OIDC)
  - Artifact Registry
- **Cloud Run ingress:** `all`, because Cloudflare Pages Functions, Stripe and Scheduler must reach it. Protection comes from the proxy secret, Access JWT and OIDC checks in the apps.
- **Cloudflare:**
  - **Move `bergerhouseboats.com.au` nameservers to Cloudflare (free plan).** Today they are at `partnerconsole.net`. Mail is Google Workspace and must keep working; Brevo and Elastic Email TXT records exist.
  - Two Pages projects with custom domains `book.` and `admin.`.
  - An Access application covering `admin.bergerhouseboats.com.au`. Google Workspace IdP. Policy: include emails in the allow-list, **require** country AU.
  - A Turnstile widget for `book.bergerhouseboats.com.au`.
  - A rate limiting rule on `book.bergerhouseboats.com.au/api/*`.
- **Firestore collections:**
  - `boats`, `bookings`, `customers`, `customerEmails`, `boatUnavailabilities`, `boatLocks`
  - `seasons`, `blockedPeriods`, `addons`, `emailTemplates`, `settings`, `counters`
  - Composite indexes go in `firestore.indexes.json` (e.g. bookings by `boatId` + `status` + `startDate`; unavailabilities by `boatId` + `firstNight`).
- **Logging and alerts:**
  - Structured logging to Cloud Logging. Never log card data or full tokens.
  - Log-based alert policies email the staff alert address on: webhook processing errors, job failures, and paid-but-expired bookings.
- **CI/CD (GitHub Actions):**
  - On PR: build, unit tests, integration tests against the Firestore emulator, web typecheck/lint/build.
  - On `main`: build and push containers → deploy both Cloud Run services → deploy both Pages projects.
  - Auth to GCP via Workload Identity Federation (no JSON keys).
- **Local development:**
  - Firestore emulator
  - Stripe test mode + Stripe CLI for webhooks
  - fake email sender
  - Turnstile test keys
  - a dev bypass of Access JWT validation, **only** when `ASPNETCORE_ENVIRONMENT=Development`
  - one command (e.g. `docker compose up` or a script) starts the emulator, both APIs and both Vite dev servers

## 15. Testing

- **Domain unit tests** (high coverage), at least:
  - Period mapping for every weekday in every mode, including boundary Mondays and Fridays.
  - Overlap rules: same-day changeover allowed; booking vs blocked/unavailability inclusive-night rules, including the 28/09→02/10 and 17/12→21/12 examples.
  - Lead time and open-until boundaries.
  - Season lookup, including the year-wrapping Peak range; price snapshotting; add-on pricing; price on request.
  - Payment schedule: standard vs extended, the 50%/100% cumulative milestones, amount due now, full payment at checkout when the balance is already due, deposit > hire price.
  - PaymentStatus derivation.
  - Status transitions and illegal transitions.
  - Stand-by superseding.
  - Rooming warning trigger (`guests > NoOfBeds`).
  - Boat activation invariant.
  - Customer email normalisation.
- **Application tests:** online vs internal validation strategies, notification job (idempotency, which emails at which instants using a fake clock and short offsets), webhook handling (idempotent replay, deposit activation, expired-hold conflict path).
- **Integration tests (Firestore emulator):**
  - Repositories round-trip.
  - **Concurrent booking race → exactly one winner.**
  - Hold expiry releases dates.
  - Optimistic concurrency 409.
  - Customer email uniqueness under concurrency.
- **Frontend:** unit tests (Vitest) for the calendar period mapping and per-period greying logic.

## 16. Deliverables

1. The full solution as laid out in §6, building and passing all tests.
2. `/wordpress/book-now-block.html`.
3. `/docs/setup-checklist.md`, a step-by-step checklist a non-developer can follow:
   1. **Cloudflare DNS migration:** add the site to Cloudflare, verify every imported record against the current zone (MX, Google verification TXT, SPF, Brevo, any DKIM/DMARC, WordPress A/CNAME), keep mail-related records DNS-only, check SSL mode is compatible with the WordPress host, then switch nameservers at the registrar.
   2. GCP project, APIs, Firestore, service accounts, Secret Manager, Cloud Run, Scheduler, alerts, Workload Identity Federation for GitHub.
   3. Stripe account, webhook endpoint (`checkout.session.completed`, `checkout.session.expired`), payment method domains, switching from test to live keys.
   4. Brevo sender domain authentication (DKIM/DMARC as Brevo instructs).
   5. Cloudflare Pages projects, custom domains, `_headers`, Access app + Google IdP + policy, Turnstile, rate limiting.
   6. Run the seed. In the admin app, complete every boat (beds, bedding, bond, rates, allowed add-ons) and activate it. Set add-on prices. Enter blocked periods (Easter, long weekends) up to "bookings open until". Review email templates. Fill in contact details.
   7. **Before go-live: staff enter every existing future booking** from paper/Excel as staff bookings, recording payments already received as manual payments. Past bookings are not entered.
   8. End-to-end test in Stripe test mode, then switch to live keys.
   9. Paste the HTML block into each of the 14 WordPress boat pages.
4. A final summary listing anything not implemented and every assumption you made.

**Suggested build order:**
1. Domain + unit tests.
2. Firestore infrastructure + integration tests.
3. Application use cases.
4. APIs.
5. Booking app.
6. Admin app.
7. Notifications job.
8. WordPress block.
9. Docs and CI.
