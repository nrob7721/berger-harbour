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
- Automatic notification emails (Gmail SMTP).
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
- Drag-and-drop on the admin calendar.
- Concurrency protection for add-on capacity (see §7.5). A true simultaneous race may oversell; this is accepted.

## 3. Glossary

| Term | Meaning |
|---|---|
| **Period type** | `Midweek` (Mon→Fri), `Weekend` (Fri→Mon), `Week` (Mon→Mon or Fri→Fri), `LongWeekend` (staff only), `Custom` (staff only, any dates). |
| **Stay** | A booking's dates as a half-open range `[StartDate, EndDate)`. `StartDate` is the check-in day (check-in window **11am – 2pm**) and `EndDate` is the check-out day (from **8am**). The nights occupied are `StartDate … EndDate−1`. The standard times are fixed in code and not configurable. Staff may record custom check-in/check-out times on a booking (§7.3); these never affect availability or capacity. |
| **Same-day changeover** | Allowed. A booking ending Monday 8am and another starting Monday 11am do not overlap. |
| **Blocked period** | A staff-defined date range (Christmas/New Year, Easter, long weekends, …) that customers cannot book online. Any online stay that overlaps it shows a "please call or email us" message. Defined by inclusive `FirstNight` and `LastNight`. |
| **Unavailability** | A staff-defined range when a specific boat cannot be hired (maintenance, etc.). Defined by inclusive `FirstNight` and `LastNight`. |
| **Stand-by booking** | A "store credit" style booking (e.g. a customer with credit waiting for a date). It **never blocks availability**, is **never shown to customers**, **sends no emails**, and is **superseded** when an Active non-stand-by booking overlaps it. |
| **Hold** | A `PendingPayment` booking created when a customer starts checkout. It blocks the dates until payment completes or the hold expires. |
| **Booking draft** | The details a customer has entered in the online booking app (from the "Your details" step onwards) before paying. It is **not** a customer record. If it is not converted into an Active booking, staff are alerted so they can follow up (email #10). |
| **Season** | A named recurring day-month range (e.g. Off Peak 1 May–31 Aug) used for pricing. Dates not covered by a season fall in the default **Normal** season. |
| **Extended payment schedule** | Applies to bookings whose stay overlaps a blocked period flagged `UsesExtendedPaymentSchedule` (Christmas/New Year). 50% of the total is due 120 days before check-in and 100% by 90 days before. All other bookings use the **standard schedule**: 100% due 30 days before check-in. |
| **Capacity-limited add-on** | An add-on with a `Capacity` shared across **all boats and bookings** (e.g. Car parking: 11 spots in total). Usage is counted per night (§7.5). |
| **Guests** | Every booking records `NumberOfAdults` (≥ 1) and `NumberOfChildren` (≥ 0, children are under 12). "Guests" means the total of both. |

**Overlap rules.** Every overlap check in the system compares **nights**:
- Booking vs booking: `a.Start < b.End && b.Start < a.End`.
- Booking vs blocked period or unavailability (inclusive nights `[F, L]`): `booking.Start <= L && F < booking.End`.

Example: Midweek Mon 28/09 → Fri 02/10 does **not** overlap a long weekend with FirstNight Fri 02/10.
Example: Midweek Mon 17/12 → Fri 21/12 **does** overlap Christmas with FirstNight 20/12.

All dates are **Australia/Sydney local dates**. "Today" always means today in Australia/Sydney. Wherever a time is shown with a time zone, the label is **`AEST/NSW` or `AEDT/NSW`, whichever applies on that date** in Australia/Sydney.

**Display format.** Every date the system writes itself (UI text, tables, emails) is Australian `dd/MM/yyyy` — never American month-first. Times are shown as `11am` / `11:30am`, and timestamps as `dd/MM/yyyy h:mma`. Native `<input type="date">` controls are kept and follow the browser's locale. APIs keep ISO `yyyy-MM-dd`, which is never displayed.

## 4. Decision log (do not re-litigate)

1. House boats only. BBQ is a future enum value only.
2. Pricing is calculated by the system per boat × season × period type. The price is **snapshotted onto the booking** when it is created.
3. The deposit is set **per boat** (optional). When a boat has no deposit of its own, the Settings default applies (default **$1000 AUD**). It counts toward the hire fee. Online checkout **always** charges only `min(deposit, hire price)`, even when the balance is already due at booking time. The rest is paid through payment links.
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
    - #10 staff alert for an abandoned online booking

    **The system does NOT email customers about cancellations or add-on changes.** Staff contact the customer by hand for both. This is deliberate.
11. Database: **Firestore**. Every Firestore-specific detail, including the per-boat lock document, lives in the Infrastructure layer so the database can be swapped later.
12. Admin authentication: Cloudflare Access with Google SSO (one Google Workspace account in v1, more later) **and** Country = AU. The admin API also validates the Access JWT. NSW-only geo-restriction is not possible in Access policies and is not required.
13. No Cloudflare Tunnel, because it is incompatible with Cloud Run scaling to zero. There are two Cloud Run services: `public-api` and `admin-api`.
    **Domains:** the booking app is public (customers never log in or sign up) on a separate, newly bought domain (placeholder `bookings.example.com.au`) on Cloudflare. The admin app has no custom domain and is served at `berger-admin.pages.dev` behind Access. The existing `bergerhouseboats.com.au` DNS and email stay where they are. The deployed dev environment (§14) uses `berger-booking-dev.pages.dev` and `berger-admin-dev.pages.dev`.
14. Customer email is **mandatory** everywhere, including staff-created bookings. Customers are matched by normalised email. For online bookings, a `Customer` record is created or updated **only when the deposit is paid** (the booking becomes Active). Until then the details live in the booking draft and on the PendingPayment booking.
15. A booking that overlaps an Active or PendingPayment booking, or an unavailability, is **never** allowed, for staff or customers. Stand-by bookings are the exception: they ignore other stand-bys and are superseded rather than blocked.
16. Frontend: TypeScript + Vite, **no UI framework**, separate HTML/CSS/TS files. Hosted on Cloudflare Pages.
17. Minimum lead time for online bookings is **30 days** (Settings). The latest date bookable online is the **"Bookings open until"** date (Settings), which staff move forward each year after entering the next year's blocked periods and prices. There is no rolling window.
18. The Customers admin page is **read-only** in v1, apart from opening bookings from it.
19. Guests are split into **adults (≥ 1)** and **children (under 12)**. Online, adults + children must not exceed the boat's max guests. The rooming warning compares adults + children with the boat's adult beds + kids beds.
20. **Add-on lines have no status.** A line on a booking is accepted. Staff decline an add-on by deleting the line.
21. Add-ons may have a **capacity shared across all boats**, counted on the busiest night of the stay. Online it is a hard limit, checked without a lock. Staff only get a warning.
22. Abandoned online bookings are reported to staff (#10). They are never stored as customers.
23. Payment emails (#4 and #7) whose trigger time had already passed when the booking was created are **never sent**.

## 5. Architecture overview

```
WordPress boat page ──(HTML block: "Book Now")──► modal <iframe> (desktop) / new tab (mobile)
                                                        │
                                       bookings.example.com.au  (Cloudflare Pages: booking app)
                                                        │  /api/* → Pages Function proxy
                                                        ▼
                                              Cloud Run: public-api  ◄── Stripe webhooks
                                                        │
                                                   Firestore  ◄── Cloud Run: admin-api ◄── Cloud Scheduler (OIDC)
                                                                        ▲
                                                        │  /api/* → Pages Function proxy
                                       berger-admin.pages.dev  (Cloudflare Pages: admin app,
                                                                       behind Cloudflare Access)
External: Stripe (payments), Gmail SMTP (email), Cloudflare Turnstile (bot protection)
```

- **GCP region:** `australia-southeast1` (Sydney) for Cloud Run, Firestore (Native mode) and Cloud Scheduler.
- **Same-origin APIs:** each Pages project has a Pages Function at `functions/api/[[path]].ts` that proxies `/api/*` to its Cloud Run service. The proxy:
  - adds a shared secret header `X-Edge-Proxy-Secret`, which both APIs require on all routes except the Stripe webhook and the jobs endpoint;
  - forwards `Cf-Access-Jwt-Assertion` (admin only);
  - forwards `CF-Connecting-IP`.

  This avoids Cloud Run custom domain mapping and lets the frontends call APIs same-origin.
- **CORS:** both APIs only allow their own frontend origin (`https://bookings.example.com.au` and `https://berger-admin.pages.dev` respectively; `https://berger-booking-dev.pages.dev` and `https://berger-admin-dev.pages.dev` in dev). The origin has no default in code: a service without one refuses to start, so an environment can never fall back to another environment's frontend. There are no wildcard origins and credentials are not allowed cross-origin. Since traffic is proxied same-origin, CORS is defence in depth.
- **Cold starts:** both services run with `min-instances=0` and are published with ReadyToRun. There is no separate "wake-up" endpoint. The booking app calls `GET /api/boats/{slug}` as soon as it loads, which wakes the service while a skeleton loader is shown.

## 6. Repository layout

```
/src
  BergerHarbour.Domain/            # aggregates, value objects, domain services, repository interfaces
    Boats/  Bookings/  BookingDrafts/  Customers/  Unavailabilities/  Settings/  Shared/
  BergerHarbour.Application/       # use cases, validation strategies, ports (IPaymentGateway, IEmailSender, IClock, ITurnstileVerifier, IBoatScheduleLock, ...)
  BergerHarbour.Infrastructure/    # Firestore/, Stripe/, Email/ (Gmail SMTP), Turnstile/, Cloudflare/ (Access JWT validation)
  BergerHarbour.PublicApi/         # ASP.NET Core controllers for customers + Stripe webhook
  BergerHarbour.AdminApi/          # ASP.NET Core controllers for staff + jobs endpoint
/tests
  BergerHarbour.Domain.Tests/
  BergerHarbour.Application.Tests/
  BergerHarbour.Infrastructure.IntegrationTests/   # against the Firestore emulator
/web
  shared/        # generated API types, date/period utilities, phone input, shared CSS tokens
  booking/       # Vite app + functions/api/[[path]].ts
  admin/         # Vite app + functions/api/[[path]].ts
/wordpress
  book-now-block.html             # the HTML block pasted into each boat page
/docs
  setup-checklist.md              # booking domain, GCP, Cloudflare, Stripe, Gmail SMTP, go-live
firestore.indexes.json
.github/workflows/
```

## 7. Backend

### 7.1 Technology and style
- **.NET 10**, ASP.NET Core with controllers, C#. Nullable reference types enabled.
- **DDD:**
  - Validation and invariants are encapsulated in domain objects (constructors, factory methods and intention-revealing methods such as `booking.Cancel()`, `booking.RecordPayment(...)`, `boat.Deactivate()`). Domain objects are never in an invalid state. There are no public setters on aggregates.
  - **Small aggregates referencing each other by Id** (Vaughn Vernon). Aggregates: `Boat`, `Booking`, `BookingDraft`, `Customer`, `BoatUnavailability`, `Season`, `BlockedPeriod`, `AddonDefinition`, `EmailTemplate`, `BusinessSettings`. If a natural root appears later, they can be merged; do not pre-merge.
  - **Domain services** for cross-aggregate rules, e.g. `BookingAvailabilityService` (overlap checks given loaded bookings, unavailabilities and blocked periods), `PricingService`, `PaymentScheduleService`, `AddonCapacityService`, `StayPeriodFactory` (period type + clicked date → stay).
- **Repositories:** one repository and interface per aggregate. **Interfaces live next to their aggregate in the Domain project** (e.g. `Domain/Bookings/IBookingRepository.cs`). Implementations live in `Infrastructure/Firestore`.
- **Validation strategy pattern** for booking requests: `IBookingRequestValidationStrategy`.
  - `OnlineBookingValidationStrategy` is registered in `PublicApi`. It enforces period shape, lead time, open-until, blocked periods, adult/child limits, rooming warning acceptance, terms acceptance, allowed add-ons, add-on capacity and a valid booking draft.
  - `InternalBookingValidationStrategy` is registered in `AdminApi`. It enforces only `StartDate < EndDate`, `NumberOfAdults >= 1`, `NumberOfChildren >= 0`, and a valid customer. Guest, bed and capacity limits are returned as non-blocking warnings.
  - Both strategies are always followed by the availability invariant (decision 15), which is not part of either strategy.
- **Money:** `decimal` AUD in the domain, stored as integer cents. All amounts include GST.
- **Dates:** `DateOnly` in the domain, stored as `yyyy-MM-dd` strings, which sort correctly for range queries. Times of day are `TimeOnly`, stored as `HH:mm`. Instants are stored as UTC timestamps.
- **Phone numbers:** validated with `libphonenumber-csharp` and stored in E.164 format (e.g. `+61412345678`). Shown in national format for Australian numbers and international format for others.
- `IClock` abstraction for all time access (tests must control time).
- **Optimistic concurrency:** every aggregate has an integer `Version`. Admin update requests include the version. A mismatch returns `409 Conflict` with "This record was changed elsewhere — reload and retry."
- OpenAPI document generated by both APIs. TypeScript types for the frontends are generated from it (e.g. `openapi-typescript`) as part of the web build.

### 7.2 Atomic booking (race conditions)
Any write that can create or move occupancy for a boat (create or update a booking, activate a hold, create or update an unavailability) must run inside a **per-boat exclusive section**:

- Application port: `IBoatScheduleLock.RunExclusiveAsync<T>(BoatId boatId, Func<IBoatScheduleSession, Task<T>> work)`. The session exposes transaction-bound reads and writes for bookings and unavailabilities of that boat.
- **Firestore implementation (Infrastructure only):** a Firestore transaction that first reads and then updates `boatLocks/{boatId}` (incrementing a counter). It queries that boat's potentially overlapping bookings (`boatId == X`, `status in [Active, PendingPayment]`, `startDate < newEnd`, then filters `endDate > newStart` in memory) and unavailabilities. It runs the domain availability check and writes. Concurrent transactions on the same boat are serialised by the lock document.
- A future relational implementation would use `SELECT … FOR UPDATE` on the boat row or a serialisable transaction, with no Domain or Application changes.
- **Integration test (required):** two concurrent requests for the same boat and overlapping dates → exactly one succeeds and the other gets `409 Conflict` "Those dates are no longer available".
- Add-on capacity spans all boats and is deliberately **not** protected by this lock (§7.5).

### 7.3 Domain model

#### Boat (aggregate)
| Field | Notes |
|---|---|
| `Id` | |
| `Name` | required |
| `Slug` | required, unique, kebab-case. Matches the WordPress URL segment (e.g. `pacific-blue`). |
| `Type` | `House` \| `BBQ` (BBQ is future use) |
| `MaxNoOfGuests` | ≥ 1. Online, `NumberOfAdults + NumberOfChildren` must not exceed it. |
| `AdultBeds` | Number of physical adult beds; a double or queen counts as **one** bed. |
| `KidsBeds` | Number of physical kids beds (e.g. bunks). May be 0. `AdultBeds + KidsBeds` drives the rooming warning. |
| `BeddingDescription` | Free text, e.g. "4 queen bedrooms, bunk area (2 singles), single in lounge, single in dining area" |
| `SecurityBond` | Money, display only |
| `DepositAmount` | Optional money (> 0 when set). When empty, `BusinessSettings.DepositAmount` applies. **Effective deposit** = boat value ?? Settings value. |
| `Rates` | List of `BoatRate { SeasonId, PeriodType (Midweek/Weekend/Week/LongWeekend), Price }` |
| `AllowedAddonIds` | Add-ons offered for this boat |
| `IsActive` | Inactive boats cannot be booked online or by staff, but their history is kept. Boats are never deleted. |
| `CreatedDate`, `ModifiedDate`, `Version` | |

**Invariant:** a boat can only be **activated** when `AdultBeds`, `KidsBeds` (0 allowed), `BeddingDescription` and a rate for **every season × {Midweek, Weekend, Week}** are present.

**Ordering:** everywhere the admin app lists boats (timeline rows, Fleet grid, every boat dropdown), boats are sorted by `MaxNoOfGuests` descending, then `Name`.

#### Customer (aggregate)
`Id`, `FullName` (required), `Email` (required, stored normalised to lower-case trimmed, **unique**), `MobileNumber` (E.164; required for online bookings, optional for staff-created), `SecondaryPhone` (E.164, optional, staff-entered only, empty when the customer is created), `CreatedDate`, `ModifiedDate`, `Version`.

- Email uniqueness is enforced with a `customerEmails/{normalisedEmail}` index document written in the same transaction.
- When an existing email books again, `FullName` and `MobileNumber` are updated to the latest values.
- Online, the customer is created or updated **only when the booking becomes Active** (deposit paid), from the booking's `CustomerDetails` snapshot. Abandoned drafts and expired holds never create or change a customer.

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
| `CustomerId` | Id reference. For online bookings it is set when the booking becomes Active; it is empty while `PendingPayment` and stays empty if the hold expires. |
| `CustomerDetails` | Online bookings: snapshot `{ FullName, Email, MobileNumber }` entered by the customer. Used to create or update the Customer on activation, and shown to staff for bookings without a `CustomerId`. |
| `BookingDraftId` | Online bookings: the draft this booking came from |
| `BoatId` | Id reference |
| `PeriodType` | `Midweek` \| `Weekend` \| `Week` \| `LongWeekend` \| `Custom` |
| `StartDate`, `EndDate` | Stay `[Start, End)`, `Start < End` |
| `NumberOfAdults` | ≥ 1 |
| `NumberOfChildren` | ≥ 0 (children under 12) |
| `CheckInTime`, `CheckOutTime` | `TimeOnly`, default `11:00` and `08:00` for every booking (online and staff). Staff-only: never shown in the booking app. They do not affect availability or capacity. |
| `Comments` | optional |
| `HirePrice` | Snapshot at creation. Staff may override. |
| `DepositAmount` | Snapshot of the boat's effective deposit at creation |
| `PaymentSchedule` | `Standard` \| `Extended` (derived from blocked-period overlap at creation, and re-derived when dates change) |
| `AddonLines` | List of `AddonLine { Id, AddonId, NameSnapshot, Quantity, UnitPrice (nullable until staff set it) }`. There is **no line status**: a line on the booking is accepted; staff decline by deleting it. |
| `Payments` | List of `Payment { Id, Amount, Method: Stripe \| Manual, StripePaymentIntentId?, PaidAt, Note?, RecordedBy: Customer \| SystemUser }` |
| `HoldExpiresAt` | Instant, only while `PendingPayment` |
| `StripeCheckoutSessionId` | Of the deposit checkout |
| `RoomingWarningAcceptance` | `{ AcceptedAt, WarningTextShown }`, present when guests > `AdultBeds + KidsBeds` on an online booking |
| `TermsAcceptedAt` | Online bookings |
| `PaymentLinkTokenHash` | SHA-256 of the random token used in payment links |
| `SentNotifications` | List of `{ Key, SentAt }`, used for idempotent emails |
| `CreatedDate`, `ModifiedDate`, `Version` | |

**Derived values (calculated, never stored as the source of truth):**
- `NumberOfGuests` = `NumberOfAdults + NumberOfChildren`.
- `TotalPrice` = `HirePrice` + Σ(`Quantity × UnitPrice`) over add-on lines **with a price**. Lines without a price yet (price on request, LongWeekend/Custom bookings) are excluded until staff enter one.
- `AmountPaid` = Σ payments.
- `AmountOwing` = `max(0, TotalPrice − AmountPaid)`.
- `PaymentStatus`, evaluated in this order:
  - `Outstanding` if `AmountPaid == 0`
  - else `FullyPaid` if `AmountPaid >= TotalPrice`
  - else `DepositPaid` if `AmountPaid == DepositAmount`
  - else `PartPaid` (this includes amounts below the deposit)

**Status transitions:**
- `PendingPayment → Active` when the deposit payment succeeds. For online bookings this also creates or updates the Customer from `CustomerDetails`, sets `CustomerId`, and marks the booking draft converted.
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

#### BookingDraft (aggregate)
| Field | Notes |
|---|---|
| `Id` | Random and unguessable (≥ 128 bits). Returned to the booking app, which keeps it in `sessionStorage`. |
| `BoatId` | |
| `PeriodType`, `StartDate`, `EndDate` | As selected so far |
| `FullName`, `Email` (normalised), `MobileNumber` (E.164) | Required when the draft is created |
| `NumberOfAdults`, `NumberOfChildren` | Optional, filled in as the customer progresses |
| `AddonRequests` | List of `{ AddonId, Quantity }` |
| `LastStepReached` | `Details` \| `Guests` \| `Addons` \| `Summary` \| `Payment` |
| `LastActivityAt` | Instant, updated on every create or update |
| `ConvertedBookingId`, `ConvertedAt` | Set when its booking becomes Active |
| `AbandonedNotifiedAt` | Set when email #10 is sent |
| `CreatedDate`, `Version` | |

Rules:
- A draft is **abandoned** when it is not converted and `now > LastActivityAt + Booking__AbandonedDraftAfter`.
- Email #10 is sent **once**, and only for the **latest draft per normalised email** (by `LastActivityAt`). It is not sent if any booking for that email became Active after the draft's `CreatedDate`.
- Drafts are **deleted** `Booking__DraftRetention` (30 days) after `CreatedDate`, whether converted or not.
- A converted draft can no longer be updated.

#### Settings aggregates
- `BusinessSettings` (singleton):
  - `DepositAmount` (default 1000; used for boats without their own deposit)
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
  - `Prices` by period type (Midweek, Weekend, Week; ignored when price on request). There are no add-on prices for LongWeekend or Custom bookings; staff enter them per line.
  - `QuantityApplies` (bool; when false, quantity is always 1)
  - `Capacity` (optional whole number ≥ 0). When set, usage is shared across all boats and bookings (§7.5).
  - `CapacityUnitLabel` (optional text, e.g. "car spots"), used in the "remaining" message.
  - `SortOrder` (internal integer). Never shown to or typed by staff; it is changed only through the ▲/▼ reorder buttons. New add-ons go to the end. Deactivated add-ons keep their place.
  - `IsActive`, `Version`
- `EmailTemplate`: `Key` (enum, see §9), `Subject`, `HtmlBody`, `Version`. Defaults are seeded at setup. Saving a template with an unknown placeholder is rejected.

### 7.4 Pricing (`PricingService`)
- **Season** = the season whose range contains the booking's `StartDate`, or `Normal`.
- **Hire price** = the boat's rate for (season, period type).
- **Add-on unit price** = the add-on's price for the booking's period type when the period type is Midweek, Weekend or Week. It is `null` (staff enter it) when the add-on is `PriceOnRequest` or the booking is `LongWeekend` or `Custom`.
- Online: requested add-ons are shown with their prices (or "price on request"). Lines with a price count toward `TotalPrice` straight away; staff decline an add-on by deleting the line.
- Staff bookings:
  - For `Midweek`, `Weekend`, `Week` and `LongWeekend`, the hire price is pre-filled from the rate table.
  - For `Custom` (e.g. Christmas 7 nights), staff type the price.
  - Staff may always override.
  - A "Recalculate from rates" action re-prices on request. Changing dates does **not** silently re-price.
- The price is always calculated on the server. Prices sent by the client are ignored.

### 7.5 Add-on capacity (`AddonCapacityService`)
For an add-on with a `Capacity`:
- **Used on a night** = Σ `Quantity` of that add-on's lines over bookings of **all boats** that block availability (Active non-stand-by, and non-stand-by PendingPayment with `HoldExpiresAt` in the future) and whose stay includes that night.
- **Remaining for a stay `[start, end)`** = `max(0, Capacity − max(used on each night start … end−1))`, i.e. measured on the busiest night.
- Custom check-in/check-out times do not affect the count.

Worked example (Car parking, capacity 11). Existing bookings: Pacific Blue 12/10 → 15/10 with 3 spots, Misty Blue 14/10 → 19/10 with 2 spots. A new booking for any boat:
- 12/10 → 15/10 → **6** remaining (night 14/10 uses 5)
- 12/10 → 14/10 → **8** remaining
- 14/10 → 16/10 → **6** remaining
- 15/10 → 18/10 → **9** remaining

Enforcement:
- **Online: hard limit.** The quote and `POST /api/bookings` check `quantity <= remaining` **without a lock** and return `400` "Only {X} {unit} remain for your dates." A truly simultaneous race may oversell; this is accepted.
- **Staff: non-blocking warning** (e.g. "This exceeds car parking capacity on 14/10/2026"). When editing, the booking itself is excluded from the count.
- Infrastructure note: bookings store a denormalised `addonIds` array so capacity queries can use `array-contains`.

### 7.6 Payment schedule (`PaymentScheduleService`)
Milestones are instants relative to check-in (**StartDate 11:00 Australia/Sydney**). Offsets come from configuration (§10.2).

| Schedule | Milestones (required cumulative amount paid) |
|---|---|
| Both | **Deposit**: `DepositAmount`, due at booking creation. Not reminded by the job; online it is paid at checkout and staff bookings use "Send payment link". |
| Standard | **Balance**: 100% of `TotalPrice`, due `check-in − BalanceDueBeforeHire` (30d) |
| Extended | **First instalment**: 50% of `TotalPrice`, due `check-in − ExtendedFirstInstalmentBeforeHire` (120d). **Final**: 100%, due `check-in − ExtendedFinalInstalmentBeforeHire` (90d). |

- **Amount due now** (used by the pay page) = the largest shortfall `requiredCumulative − AmountPaid` among milestones whose reminder window has opened (`now >= due − PaymentReminderBeforeDue`). If there is none, it is `AmountOwing`. This covers ad-hoc links after a price-on-request add-on is priced on an already-paid booking.
- **Online checkout amount** = `min(DepositAmount, HirePrice)`, **always**, even when the balance milestone is already due. The rest is requested through the payment link in email #1.
- A milestone whose due instant is at or before the booking's `CreatedDate` is **already due at creation**. See §9 for how notifications treat it.

### 7.7 Online booking rules (`OnlineBookingValidationStrategy`)
1. The boat exists, `IsActive`, and `Type == House`.
2. `PeriodType ∈ {Midweek, Weekend, Week}`:
   - Midweek starts Monday and ends Friday (+4).
   - Weekend starts Friday and ends Monday (+3).
   - Week starts Monday or Friday and ends +7.
3. `StartDate >= today + MinimumLeadTimeDays`.
4. `EndDate − 1 <= BookingsOpenUntil` (every night is inside the open window).
5. The stay does not overlap any `BlockedPeriod`.
6. `NumberOfAdults >= 1`, `NumberOfChildren >= 0`, and `NumberOfAdults + NumberOfChildren <= MaxNoOfGuests`.
7. If `NumberOfAdults + NumberOfChildren > AdultBeds + KidsBeds`: rooming acceptance is present and its text matches the warning the server would generate.
8. Terms accepted.
9. Every add-on is active and in `boat.AllowedAddonIds`. `Quantity >= 1`, and it is 1 when `!QuantityApplies`. For capacity-limited add-ons, `Quantity <= remaining` (§7.5).
10. Name, email (valid format) and mobile (valid for its country, E.164) are required.
11. A valid, unconverted `BookingDraft` id for this boat is supplied. (Turnstile was verified when the draft was created.)

Then the availability invariant runs inside `IBoatScheduleLock`.

## 8. API

All routes are under `/api`. JSON uses camelCase, dates are `yyyy-MM-dd`, times are `HH:mm`, phone numbers are E.164, and money is a decimal number in AUD. Validation failures return `400` with ProblemDetails and per-field errors. Overlaps return `409`.

### 8.1 public-api (customer-facing, no login)
| Method & route | Purpose |
|---|---|
| `GET /api/boats/{slug}` | Customer-safe boat data: name, maxNoOfGuests, adultBeds, kidsBeds, beddingDescription, securityBond, effective depositAmount, isActive, allowed add-ons in admin-defined order (id, name, description, priceOnRequest, quantityApplies, prices by period type, hasCapacity, capacityUnitLabel), plus booking settings (minimum lead time, bookings open until, contact phone/email, hire terms URL). Also the warm-up call. |
| `GET /api/boats/{slug}/availability?from=&to=` | Returns only **ranges**, never booking or customer data: `{ unavailable: [{start, end}], enquireOnly: [{firstNight, lastNight}] }`. `unavailable` = blocking bookings (as `[start,end)`) and unavailabilities. `enquireOnly` = blocked periods. Max span 15 months. |
| `GET /api/addons/{addonId}/availability?startDate=&endDate=` | Returns `{ remaining }` for a capacity-limited add-on across all boats (§7.5), and nothing else. `404` if the add-on has no capacity. |
| `POST /api/booking-drafts` | Body: `{ slug, periodType, startDate, fullName, email, mobile, turnstileToken }`. Verifies Turnstile, validates the details and creates a `BookingDraft`. Returns `{ draftId }`. Never creates a Customer. |
| `PUT /api/booking-drafts/{draftId}` | Body: `{ periodType, startDate, fullName, email, mobile, numberOfAdults, numberOfChildren, addons[{addonId, quantity}], lastStepReached }`. Updates the draft and `LastActivityAt`. `404` if the draft is unknown, converted or deleted. |
| `POST /api/boats/{slug}/quote` | `{ periodType, startDate, numberOfAdults, numberOfChildren, addons[] }` → hire price, season name, add-on prices, amount due at checkout, payment schedule with due dates, rooming warning text if applicable. Runs the online validation rules (except the draft, terms and acceptance) and returns their errors. |
| `POST /api/bookings` | Body: `{ draftId, slug, periodType, startDate, numberOfAdults, numberOfChildren, fullName, email, mobile, addons[{addonId, quantity}], roomingWarningAccepted, roomingWarningText, termsAccepted }`. Validates, creates a `PendingPayment` hold (atomic) holding a `CustomerDetails` snapshot (**no Customer is created yet**), updates the draft, creates a Stripe Embedded Checkout session (`expires_at` = now + hold duration, minimum 30 min), and returns `{ reference, clientSecret }`. |
| `GET /api/bookings/{reference}/status?session_id=` | Used by the completion screen. Returns status and basic summary only if `session_id` matches the booking. |
| `GET /api/payments/{token}` | Pay page: booking reference, boat, dates, total, paid, owing, amount due now. |
| `POST /api/payments/{token}/checkout` | Creates an Embedded Checkout session for the amount due now and returns `clientSecret`. Returns 400 if nothing is owing. |
| `POST /api/stripe/webhook` | Stripe-signature verified. Exempt from the proxy secret. See §8.3. |

**Abuse protection:**
- Turnstile is required on `POST /api/booking-drafts`. `POST /api/bookings` requires a valid, unconverted draft id.
- A Cloudflare rate-limiting rule applies to `bookings.example.com.au/api/*`.
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
| Bookings | `GET /api/bookings?from=&to=&includeInactive=` (timeline data incl. customer name (from the Customer or the `CustomerDetails` snapshot), status, isStandby, paymentStatus, reference; boats ordered by maxNoOfGuests desc, then name) · `GET /api/bookings/search?q=` (name, email or reference, including `CustomerDetails` snapshots) · `GET /api/bookings/{id}` · `POST /api/bookings` · `PUT /api/bookings/{id}` (boat, dates, period type, adults, children, check-in/check-out times, standby, comments, hire price, status Active/Cancelled, customer contact details incl. secondary phone; includes `version`) · `POST /api/bookings/{id}/recalculate-price` · `POST /api/bookings/{id}/addons` · `PUT /api/bookings/{id}/addons/{lineId}` (quantity, unit price) · `DELETE /api/bookings/{id}/addons/{lineId}` · `POST /api/bookings/{id}/payments` (manual payment: amount, date, note) · `POST /api/bookings/{id}/send-payment-link` (sends email #4 immediately for the amount due now) · `GET /api/bookings/quote?boatId=&periodType=&startDate=` (pre-fill price) · `GET /api/addons/{addonId}/availability?startDate=&endDate=&excludeBookingId=` (remaining capacity) |
| Fleet | `GET /api/boats` (ordered by maxNoOfGuests desc, then name) · `GET /api/boats/{id}` · `POST /api/boats` · `PUT /api/boats/{id}` (all editable fields incl. adult beds, kids beds, deposit, rates, allowed add-ons, active flag) · `GET /api/boats/{id}/unavailabilities` · `POST /api/boats/{id}/unavailabilities` · `PUT /api/boats/{id}/unavailabilities/{uid}` · `DELETE /api/boats/{id}/unavailabilities/{uid}` |
| Customers | `GET /api/customers?q=` (read-only list) · `GET /api/customers/by-email?email=` (used by the booking dialog to pre-fill) · `GET /api/customers/{id}/bookings` (all statuses, ordered by StartDate, newest first) |
| Settings | `GET/PUT /api/settings/business` · CRUD `/api/settings/seasons` · CRUD `/api/settings/blocked-periods` · CRUD `/api/settings/addons` (incl. capacity and capacity unit label; delete = deactivate) · `PUT /api/settings/addons/order` (body: the full ordered list of add-on ids; the server rewrites `SortOrder`) · `GET /api/settings/email-templates` · `PUT /api/settings/email-templates/{key}` · `POST /api/settings/email-templates/{key}/test` (sends a sample to the staff alert address) |
| Jobs | `POST /api/jobs/process-notifications`. **Not** behind Access. It validates a Google OIDC token: issuer Google, audience = service URL, email = the Cloud Scheduler service account. |

Staff-created bookings (`POST /api/bookings`):
- Email and name are required. The customer is matched or created by email.
- Status is `Active` immediately. `CreatedBy = SystemUser`.
- Validated by `InternalBookingValidationStrategy` plus the availability invariant.
- Responses include non-blocking `warnings[]` (guests above max, guests above beds, add-on capacity exceeded, custom time clashing with a same-day changeover).
- If it is not a stand-by, email #1 is sent.
- If it becomes Active and overlaps stand-bys, they are superseded.

### 8.3 Stripe
- Only synchronous methods (card + wallets) are allowed, so `checkout.session.completed` means paid.
- Checkout session `metadata`: `bookingId`, `purpose` (`Deposit` | `Payment`).
- **`checkout.session.completed`:**
  - Record a `Payment`. This is idempotent on PaymentIntent id.
  - If `purpose == Deposit` and the booking is `PendingPayment`, activate it inside `IBoatScheduleLock`, create or update the Customer from `CustomerDetails` and set `CustomerId`, mark the booking draft converted, supersede stand-bys, then send #1 to the customer and #2 to staff.
  - If activation is impossible (the hold had already expired and the dates were taken), keep the payment recorded, leave the booking `Expired` (no Customer is created; staff use the `CustomerDetails` snapshot), and log an **error**. The log-based alert notifies staff, who refund by hand.
  - If `purpose == Payment`, send #5.
- **`checkout.session.expired`:** if the booking is still `PendingPayment` with that session id, set it to `Expired`.
- The webhook returns 2xx only after persistence succeeds, so Stripe retries on failure.
- Apple Pay / Google Pay should work inside the WordPress modal (iframe has `allow="payment"`; register the payment method domains in Stripe). Card payment must work regardless.
- Set the Checkout session `locale` to `"auto"`. Stripe only accepts its own fixed list (e.g. `en`, `en-GB`)

## 9. Notifications

**Provider:** Gmail SMTP (`smtp.gmail.com:587`, STARTTLS) using MailKit. The sender is the business mailbox `info@bergerhouseboats.com.au` (a Google Workspace user), authenticated with a Google app password. It is the SMTP login, the From address (display name "Berger Houseboats") and the Reply-To. Customer replies and bounces arrive in that mailbox, and every email sent is kept in its Sent folder; the system does no delivery or open tracking. Workspace allows about 2,000 emails a day through SMTP, far above this system's volume. Sending is behind the `IEmailSender` port; local development uses a fake that writes emails to the console or a folder.

| # | Key | To | Trigger |
|---|---|---|---|
| 1 | `BookingConfirmed` | Customer | Online: deposit paid (booking becomes Active). Staff: non-stand-by booking created. Includes the deposit receipt, payment schedule, security bond notice, requested add-ons "subject to confirmation", and the hire terms link. If a milestone is **already due at creation**, it states that the balance of `{{AmountDue}}` is now due and includes `{{PaymentLink}}`. |
| 2 | `StaffNewOnlineBooking` | Staff alert address | Online booking becomes Active |
| 4 | `PaymentDue` | Customer | For each Balance / First / Final milestone with a shortfall: once at `due − PaymentReminderBeforeDue` (7d) and once at `due`. Each is skipped if its trigger instant was at or before the booking's creation. Also sent immediately from "Send payment link". Contains `{{PaymentLink}}`. |
| 5 | `PaymentReceived` | Customer | Any Stripe payment other than the deposit checkout, and any manual payment |
| 6 | `PreHireInstructions` | Customer | `check-in − PreHireInstructionsBeforeHire` (7d). This is the existing "canned message". If the booking is created inside that window, it is sent at the next job run. |
| 7 | `StaffBalanceOverdue` | Staff alert address | Once per milestone, when `now > due` and a shortfall remains. **Never sent for a milestone that was already due at creation.** |
| 8 | `StaffStandbySuperseded` | Staff alert address | A stand-by is superseded |
| 10 | `StaffAbandonedBooking` | Staff alert address | A booking draft is abandoned (§7.3 BookingDraft rules). Includes the customer's name, email and phone, the boat, dates, adults/children, requested add-ons and the last step reached, so staff can follow up. |

Numbers 3 (add-on confirmed/declined) and 9 (cancellation) are **deliberately not implemented** (decision 10).

**Rules:**
- No email of any kind for stand-by bookings, or for bookings that are PendingPayment, Cancelled, Expired or Superseded (except #8 to staff).
- **Payment emails after a late booking:** #4 and #7 notifications whose trigger instant was at or before the booking's `CreatedDate` are never sent (decision 23). Example: a booking made 35 days before check-in gets no 7-day reminder but does get the "due" email 30 days before check-in. A booking made 10 days before check-in gets no #4 or #7 for the balance; its #1 includes the payment link instead.
- **Idempotency:** before sending, check `SentNotifications` for a key like `PaymentDue:Balance:Reminder`, `PaymentDue:Balance:Due`, `Overdue:Final`, `PreHire`. Record it after a successful send. Manual "Send payment link" sends are not deduplicated. Abandoned-draft emails are deduplicated with `BookingDraft.AbandonedNotifiedAt`.
- **Templates:**
  - Editable in Settings: subject + HTML body. Defaults are seeded at setup with sensible Australian-English wording.
  - Placeholders: `{{CustomerName}} {{CustomerEmail}} {{CustomerPhone}} {{BookingReference}} {{BoatName}} {{CheckInDate}} {{CheckOutDate}} {{CheckInTime}} {{CheckOutTime}} {{NumberOfGuests}} {{NumberOfAdults}} {{NumberOfChildren}} {{HirePrice}} {{TotalPrice}} {{AmountPaid}} {{AmountOwing}} {{AmountDue}} {{DueDate}} {{PaymentLink}} {{SecurityBond}} {{RequestedAddons}} {{PaymentSchedule}} {{HireTermsUrl}} {{ContactPhone}} {{ContactEmail}} {{LastStepReached}}`. Placeholders that do not apply to a message (e.g. booking fields in #10) render empty.
  - `{{CheckInTime}}` shows the standard window "11am – 2pm" and `{{CheckOutTime}}` shows "8am", each with the AEST/NSW or AEDT/NSW label for that date. If staff changed a booking's time from its default, the custom time is shown instead.
  - `{{RequestedAddons}}` lists lines in the admin-defined add-on order.
  - Values are HTML-encoded. Dates are `dd/MM/yyyy`. Money is `$1,234.00`.
- **Payment link:** `https://bookings.example.com.au/pay/?token=<token>`. The token is 32 random bytes, base64url. Only its SHA-256 hash is stored. It is generated with the booking and is stable for the booking's life.

**Scheduled job** `process-notifications`:
- Cloud Scheduler runs it **hourly** (OIDC-authenticated HTTP call to admin-api).
- It is idempotent and safe to run at any frequency. Each run:
  1. Expires `PendingPayment` holds past `HoldExpiresAt`.
  2. Sends due #4, #6 and #7 emails for Active non-stand-by bookings with `StartDate >= today`.
  3. Sends #10 for abandoned booking drafts.
  4. Deletes booking drafts older than `Booking__DraftRetention`.
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
| `Booking__AbandonedDraftAfter` | `02:00:00` (inactivity before a draft counts as abandoned; longer than the maximum hold) |
| `Booking__DraftRetention` | `30.00:00:00` |

### 10.3 Environments, secrets and other config
`ASPNETCORE_ENVIRONMENT` selects the environment: `Local` (a developer machine against emulators), `Development` (the deployed dev project) or `Production`. Non-secret settings that differ per environment live in `appsettings.{Environment}.json`, committed with the code: the booking base URL, the allowed CORS origin, the email sender, sender name and `EMAIL_SUBJECT_PREFIX` (`[DEV] ` in dev, empty in prod). `PUBLIC_BOOKING_BASE_URL` and `CORS_ALLOWED_ORIGIN` have no defaults in code and are required at startup. Infrastructure IDs that only exist once a project is provisioned (Firestore project, Access team domain and AUD, Scheduler service account and audience) are passed as env vars by the deploy. Only `Local` relaxes any security check (§14); the services refuse to start if `DEV_ADMIN_EMAIL` or `DEV_JOBS_BYPASS` is set in any other environment.

Secrets are stored in each project's Secret Manager and injected as env vars:
- Stripe secret key and webhook signing secret
- `SMTP_PASSWORD`: the Google app password for the sender mailbox. `EMAIL_SENDER` (`info@bergerhouseboats.com.au`) and `EMAIL_SENDER_NAME` are plain settings. In `Smtp` mode the services refuse to start if the sender or password is missing; `EMAIL_MODE=File` (the development default) writes emails to a folder instead
- Turnstile secret
- `EDGE_PROXY_SECRET`
- `CF_ACCESS_TEAM_DOMAIN`, `CF_ACCESS_AUD`, `ADMIN_ALLOWED_EMAILS` (comma-separated)
- `SCHEDULER_SERVICE_ACCOUNT_EMAIL`

The frontends need the Stripe publishable key and the Turnstile site key at build time.

## 11. Customer booking app (`bookings.example.com.au`)

### 11.1 Embedding on WordPress
Each boat page gets this HTML block (deliver it as `/wordpress/book-now-block.html` with the slug as the only thing to change):
- A `<button class="bh-book-now" data-boat="pacific-blue">Book Now</button>`.
- An inline script, guarded so it only initialises once per page. On click:
  - **Mobile** (`matchMedia('(max-width: 768px), (pointer: coarse)')`): open `https://bookings.example.com.au/?boat=<slug>` in a new tab.
  - **Desktop:** open an accessible modal overlay (focus trap, Esc to close, close button, body scroll lock) containing an `<iframe src=".../?boat=<slug>" allow="payment" title="Book <boat>">`.
  - It listens for `postMessage` `{ type: 'bh-booking:close' }`, accepted **only** from origin `https://bookings.example.com.au`.
- The booking app sends:
  - `Content-Security-Policy: frame-ancestors https://bergerhouseboats.com.au https://www.bergerhouseboats.com.au` (configured in Pages `_headers`)
  - a CSP compatible with Stripe.js and Turnstile.

### 11.2 Flow
The app is a single page with steps, in this order: **Dates → Your details → Guests → Add-ons → Summary → Payment → Complete**. The progress is visible. The layout is responsive and works at 360px width (16px gutters, no horizontal scroll) and in the ~900×700 modal. Once a booking draft exists, moving forward through each step updates it (`PUT /api/booking-drafts/{draftId}`).

1. **Load:** read `?boat=`. Show a skeleton while `GET /api/boats/{slug}` loads.
   - Unknown slug → friendly error.
   - Inactive boat → everything greyed out, with an info message at the top: "This boat is currently unavailable for online booking. Please contact us on {phone} / {email}."
2. **Dates:**
   - Radio buttons: **Mid-week (Mon–Fri)**, **Weekend (Fri–Mon)**, **Week (Mon–Mon or Fri–Fri)**.
   - A classic month calendar with previous/next navigation and a legend. Above it, show **"Check-in 11am – 2pm · Check-out from 8am (AEDT/NSW)"**, with the time zone label (§3) for the selected dates, or for the visible month when nothing is selected. When the check-in and check-out dates fall in different zones (the stay crosses a daylight-saving change), each part carries its own label: "Check-in 11am – 2pm (AEST/NSW) · Check-out from 8am (AEDT/NSW)".
   - Availability is fetched for the visible months.
   - **Clickable days per mode:**
     - Mid-week: Mon–Fri clickable; Sat and Sun disabled.
     - Weekend: Fri, Sat, Sun, Mon clickable; Tue–Thu disabled.
     - Week: only Mondays and Fridays clickable.
   - **Click mapping:** the period of the selected type that contains the day.
     - Mid-week: Mon–Fri of that week. Clicking Fri selects the Mon→Fri ending that day. Clicking a different day in the same period doesn't change the selection.
     - Weekend: the Fri→Mon containing it. Clicking Mon selects the weekend ending that day.
     - Week: Mon→next Mon or Fri→next Fri, starting on the clicked day.
   - The whole selected period is highlighted. The start shows a check-in marker and the end a check-out marker. **Every day inside the selection uses the active selection colour with normal (not grey) day numbers, even days that are not clickable in the current mode** (e.g. Tue–Thu inside a Week selection).
   - The **inner days** of the selection (every day strictly between check-in and check-out, e.g. Tue–Thu of a Mon→Fri stay) use a darker brand tint (`--c-brand-tint-strong`) than the general light tint, so the selected period stands out clearly against the disabled and non-selectable days around it. The difference must be obvious at a glance, not a subtle shade. The check-in and check-out days keep their solid brand colour and markers.
   - Below the calendar, a line states the selection without repeating the times, e.g. **"Selected: check-in Fri 16/10/2026, check-out Fri 23/10/2026."** (weekday + `dd/MM/yyyy`). The same text is announced in the ARIA live region.
   - **Greying is per period, not per day:** a day is disabled in a mode when the period it maps to is unavailable or invalid. For example, a Monday on which a weekend booking checks out is still clickable in Mid-week mode.
   - A period is unavailable if:
     - it overlaps `unavailable` ranges;
     - it starts before today + lead time;
     - it ends after `BookingsOpenUntil`.
   - **Blocked periods:** days inside `enquireOnly` ranges are greyed out with a distinct style. If the period a click maps to overlaps a blocked period, show a pop-up instead of selecting: "Bookings over {period name} must be made by phone or email: {phone} / {email}." Example: Mid-week mode, click Tue 18/12 → Mon 17/12–Fri 21/12 overlaps Christmas → pop-up.
   - Changing the radio option clears the selection.
3. **Your details:**
   - Full name, email and mobile, all required and validated.
   - The mobile field has a searchable **country code selector** defaulting to **Australia (+61)**, validated with `libphonenumber-js` and sent as E.164. Bookings may come from overseas.
   - The Turnstile widget.
   - A short note: "If you don't finish your booking, our team may contact you to help."
   - On Continue: `POST /api/booking-drafts` the first time (then keep `draftId` in `sessionStorage`), or `PUT` if a draft already exists.
4. **Guests:**
   - Two dropdowns: **Adults** (`1…MaxNoOfGuests`) and **Children (Under 12)** (`0…MaxNoOfGuests − adults`). Changing adults clamps children so the total never exceeds max guests.
   - If `adults + children > AdultBeds + KidsBeds`, show a modal **rooming warning** with the bedding description, e.g. "This boat has {AdultBeds} adult beds and {KidsBeds} children's beds ({BeddingDescription}). With {n} guests, some guests will need to share beds. Please make sure your group's sleeping arrangements suit this layout." It has an **Accept** button and a Cancel button. Cancel returns to the guest selection. The booking cannot continue without accepting. The exact text shown is sent with the booking.
5. **Add-ons:**
   - Checkboxes for the allowed add-ons, in the admin-defined order, with a quantity input where `QuantityApplies`.
   - Each shows a price for the selected period type, or "Price on request".
   - **Capacity-limited add-ons:** call `GET /api/addons/{addonId}/availability` for the selected dates **every time the Add-ons step is shown** (never cache it: going back to change the dates, or returning from the Summary, must show current figures). Show "{X} {CapacityUnitLabel} remaining" under the add-on (e.g. "6 car spots remaining"; "{X} available for your dates" when no unit label is set). The quantity input cannot go above X. When X is 0, the add-on is **not shown at all**.
   - Notice: "Add-ons are requests and will be confirmed by our staff. Confirmed add-ons are added to your balance. Add-on prices are subject to change during peak periods."
6. **Summary:**
   - Server quote showing boat, a **Check-in** row and a **Check-out** row, each with its date, time and the time zone label (§3) for that date (e.g. "Check-in: Fri 16/10/2026, 11am – 2pm AEDT/NSW" and "Check-out: Fri 23/10/2026, from 8am AEDT/NSW"; across a daylight-saving change each row shows its own label), adults and children, season, hire price, requested add-ons (with price or "price on request"), **amount payable now** (the deposit), the balance schedule with due dates, and the security bond notice ("A ${bond} security bond applies — details will be provided").
   - Terms checkbox: "I accept the [Hire Terms and Procedures]" (link opens in a new tab).
   - Button: **Pay ${amount}**, enabled only once the terms are accepted.
7. **Payment:** `POST /api/bookings` → mount Stripe Embedded Checkout.
   - On `409`, return to the Dates step with "Sorry, those dates were just taken."
   - On a capacity `400`, return to the Add-ons step with the message and refreshed remaining figures.
8. **Complete:** poll `/status` until `Active` (show "Confirming…" for up to ~30s, then say an email will follow). Show the reference and "A confirmation email has been sent to {email}". In the modal, show a **Close** button that posts `bh-booking:close`.

**Pay page** `/pay/?token=`: shows the booking summary, total, paid, owing and amount due now, with a **Pay** button that mounts Embedded Checkout. If nothing is owing, show a "Nothing to pay" state.

Accessibility: keyboard-operable calendar (arrow keys, Enter), visible focus, ARIA labels and live region for selection, contrast AA.

## 12. Admin app (`berger-admin.pages.dev`)

Behind Cloudflare Access. There is a top menu bar: **Bookings · Fleet · Customers · Settings**. The layout is desktop-first and usable on a tablet. All dates, times and timestamps follow the display format in §3. Boats are always listed by max guests (largest first), then name.

### 12.1 Bookings
- **Full-screen timeline:** one row per boat (sticky boat-name column), one column per day, Prev/Next and Today buttons, weekend columns shaded.
- **Week | Month toggle** (a segmented control next to Today):
  - **Month view:** one column per day of the month, horizontal scroll. Prev/Next move one month; Today goes to the current month. Title e.g. "October 2026".
  - **Week view:** 7 columns, **Mon → Sun**, stretched to fill the width (no horizontal scroll). Prev/Next move one week; Today goes to the week containing today. Title e.g. "Mon 19/10/2026 – Sun 25/10/2026".
  - Switching Month → Week shows the week containing today if today is in the displayed month, otherwise the week containing the 1st of that month. Switching Week → Month shows the month containing the displayed week's Monday.
  - Month is the default. The last chosen view is remembered per browser (`localStorage`).
  - Everything else (boat rows, blocked-period band, weekend shading, Show inactive, clicking an empty cell, search) works the same in both views.
- **Bars:**
  - Bars are positioned by time of day: each day cell spans 24 hours, and a booking starts at its `CheckInTime` on the check-in day and ends at its `CheckOutTime` on the check-out day, linearly within the cell (e.g. 11am starts ≈46% into the cell, 8am ends ≈33% into it; a custom 11pm check-out ends close to the next day's border). Custom times are reflected. This applies in both views, so changeovers are visible.
  - Bars in a boat row are stacked into lanes by time, not by date: two bars share a lane only if one's check-out time is at or before the other's check-in time. A standard same-day changeover (8am out, 11am in) therefore stays in one lane, while custom times that clash (e.g. an 11pm check-out and an 11am check-in on the same day) put the later booking in a new lane. Bars never cover each other.
  - Bar label: customer name + reference.
  - Styles: `Active` (solid, tinted by payment status), `PendingPayment` (dashed outline), stand-by (hatched).
  - Unavailabilities are grey blocks positioned with the standard times, from 11am on `FirstNight` to 8am on the day after `LastNight`, so they line up with booking bars. Blocked periods are a labelled column band across all rows, covering whole days.
  - "Show inactive" toggle reveals Cancelled, Expired and Superseded bookings (faded).
- **"+ New" button**, and clicking an empty cell, opens the booking dialog (the cell pre-fills boat and start date).
- Clicking a bar opens the same dialog in edit mode.
- **Search box:** name, email or reference → results list → opens the dialog.

**Booking dialog fields:**
- Boat
- Period type (Midweek/Weekend/Week/LongWeekend/Custom) — selecting one pre-fills the end date
- Start date, end date
- **Number of adults** (≥ 1) and **number of children** (≥ 0). Show a non-blocking warning if the total is above max guests or above `AdultBeds + KidsBeds`.
- **Check-in time** (default 11am) and **check-out time** (default 8am). Staff-only. Show a non-blocking warning when a custom time clashes with a same-day changeover on that boat (this booking's check-in is earlier than the previous booking's check-out on the same day, or its check-out is later than the next booking's check-in).
- **Customer:** Email (required; on blur, look up an existing customer and pre-fill), Full name (required), Phone (optional), **Secondary phone** (optional, empty by default). Both phone fields use the same country code selector as the booking app.
- Stand-by checkbox
- Comments
- Hire price (pre-filled, editable, "Recalculate from rates" button)
- Status (Active/Cancelled editable; other statuses read-only)

**Add-ons section:**
- Lines with name, quantity, unit price and a remove button, plus an "Add add-on" control (add-ons listed in the admin-defined order).
- Unit price is editable. It must be entered for price-on-request add-ons (e.g. the welcome hamper) and for LongWeekend and Custom bookings; until then the line doesn't count toward the total.
- There is no line status. Staff remove an add-on they decline; a line left on the booking is accepted.
- Capacity-limited add-ons show the remaining capacity for the booking's dates (excluding this booking) and a non-blocking warning when exceeded. On the new-booking form the figure is re-fetched whenever the start date, end date, period type or picked add-on changes (including when choosing a period type rewrites the end date); a slower, older response must never overwrite a newer one.

**Payments section:**
- A list of payments, plus totals: Total, Paid, Owing, Payment status, and the schedule with due dates.
- **"Add manual payment"** (amount, date, note).
- **"Send payment link"** (disabled when nothing is owing; shows a confirmation toast).

**Read-only info for online bookings:** created by, created date, terms accepted at, rooming warning accepted at + text, and the customer details entered online.

**Behaviour:**
- Saving checks overlaps. On conflict, show the conflicting booking references.
- Version conflicts show "reload and retry".

### 12.2 Fleet
- A grid of boat cards: name, max guests, adult beds + kids beds, an active/inactive badge.
- "+ New boat".
- Clicking a card opens a dialog with tabs:
  - **Details:** Name, Slug, MaxNoOfGuests, Number of adult beds, Number of kids beds, BeddingDescription, SecurityBond, **Deposit** (optional; help text "Leave empty to use the default deposit of ${default} from Settings"), Active toggle (activation shows the missing data if the invariant fails).
  - **Rates:** a grid of seasons × {Midweek, Weekend, Week, LongWeekend}.
  - **Add-ons:** checklist of allowed add-ons.
  - **Unavailabilities:** a scrollable table (first night, last night, comments) with edit and delete, and a **"+ New Unavailability"** button.

### 12.3 Customers
- A table: name, email, mobile, secondary phone, created date. A client-side text filter.
- Each customer row has a **dropdown expander** listing all of that customer's bookings (loaded on expand from `GET /api/customers/{id}/bookings`): reference, boat, dates, period type, status and payment status. All statuses are shown, with inactive ones faded, ordered by StartDate, newest first.
  - Clicking a booking opens the **same booking dialog** as clicking its bar on the Bookings timeline.
- Otherwise the page is read-only. Contact details are updated from the booking dialog.

### 12.4 Settings
Sections:
- **General:** default deposit (used for boats without their own deposit), minimum lead time, bookings open until, staff alert email, contact phone/email, hire terms URL.
- **Seasons:** list + editor of recurring day-month ranges; overlap validation.
- **Blocked periods:** name, first night, last night, extended payment schedule flag. Help text: "Enter each year's Christmas/New Year, Easter and long weekends. Customers must phone to book these."
- **Add-ons:** catalogue editor including capacity and capacity unit label. **▲/▼ buttons** on each add-on change its position; no order number is shown or typed. This order is used in the booking app, the admin add-on picker and emails.
- **Email templates:** a select list of keys, subject and HTML body editors, a placeholder reference list, and "Send test".

## 13. Seed data (run once at setup; idempotent)

- **Seasons:**
  - Normal (default)
  - Off Peak (01-05 → 31-08)
  - Peak (01-12 → 31-01)
- **Blocked periods:** "Christmas / New Year 2026–27" 20/12/2026 → 05/01/2027, extended schedule = true. Staff add Easter and long weekends.
- **Add-ons** (names from the brief, seeded in this order):
  - Yabby pumps, Ice, Bait, Pizza oven, Fishing gear, Car parking, Grocery service, On mooring jetty stays, Outdoor heater
  - Outboard motor: $95 Midweek, $95 Weekend, $130 Week
  - Welcome hamper and Skipper: always `PriceOnRequest = true`
  - All add-ons except the outboard motor are seeded as `PriceOnRequest = true` until staff enter prices.
  - Ice, Bait and Car parking have `QuantityApplies = true`.
  - Car parking has `Capacity = 11` and `CapacityUnitLabel = "car spots"`.
- **Boats** (all 14 are seeded with beds, bedding, bond, rates and every add-on allowed, taken from each boat's page on bergerhouseboats.com.au; only Pacific Blue is active, the rest stay inactive until staff verify them in Fleet; staff must verify slugs against WordPress URLs). Own deposits: Pacific Blue, Ocean Spirit and Island Dream $1000, Runaway Bear $250; every other boat uses the Settings default. The full data is in `SeedService.cs`; Pacific Blue is the reference:

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
  - AdultBeds 6 (4 queens + 2 singles), KidsBeds 2 (bunks)
  - Bedding: "4 queen bedrooms, bunk area (2 singles), single in lounge, single in dining area"
  - Bond $2,000
  - Deposit: empty (uses the Settings default)
  - Rates (Weekend / Midweek / LongWeekend / Week):
    - Normal: 4060 / 4060 / 4620 / 5850
    - Off Peak: 3570 / 3570 / 4050 / 5250
    - Peak: 4500 / 4500 / 5100 / 6200
  - With 8 beds in total and 12 max guests, 9+ guests (adults + children) triggers the rooming warning.
- **Email templates:** defaults for #1, #2, #4, #5, #6, #7, #8, #10.
- **BusinessSettings:** deposit 1000, lead time 30, open-until 30/11/2027, contact phone/email placeholders for staff to fill in.

## 14. Infrastructure and operations

- **Environments:** two GCP projects, **dev** and **prod**, with identical infrastructure and nothing shared except the Artifact Registry. Dev runs with `ASPNETCORE_ENVIRONMENT=Development`, prod with `Production`; both behave identically apart from their `appsettings.{Environment}.json`. Dev uses Stripe test mode, its own Turnstile widget and Access application, and the same Gmail sender as prod with the `[DEV] ` subject prefix and the sender name "Berger Houseboats (DEV)". Dev uses the default timing (§10.2) unless a test needs shorter offsets.
- **GCP (per project):**
  - Firestore Native (`australia-southeast1`)
  - two Cloud Run services (min 0; max instances 3 for public, 2 for admin), each with its own service account and least privilege
  - Secret Manager
  - Cloud Scheduler job (hourly → admin-api `/api/jobs/process-notifications`, OIDC)
  - a Workload Identity pool and `github-deploy` service account. The prod pool only trusts GitHub jobs running in the `production` environment.
- **Artifact Registry:** one Docker repository in the prod project. Dev's deploy identity may push to it (and nothing else in prod); dev's Cloud Run pulls from it. Images are tagged with the commit SHA.
- **Cloud Run ingress:** `all`, because Cloudflare Pages Functions, Stripe and Scheduler must reach it. Protection comes from the proxy secret, Access JWT and OIDC checks in the apps.
- **Cloudflare:**
  - **`bergerhouseboats.com.au` DNS is not touched.** WordPress and Google Workspace mail stay at `partnerconsole.net`. A separate booking domain (placeholder `bookings.example.com.au`, to be bought) is added to Cloudflare (free plan); it hosts only the booking app. If Google's DKIM, SPF or DMARC records for `bergerhouseboats.com.au` are missing, they are added at the existing DNS host.
  - Two Pages projects per environment. `berger-booking` has the booking domain as its custom domain. `berger-admin` has **no custom domain** and is served at `https://berger-admin.pages.dev` (project name placeholder). Dev uses `berger-booking-dev` and `berger-admin-dev`, served only at their `pages.dev` addresses.
  - An Access application covering `berger-admin.pages.dev`, created through the Pages project's "Enable access policy" and edited to cover the production address rather than the preview wildcard. Google Workspace IdP. Policy: include emails in the allow-list, **require** country AU. Customers are never put behind Access. Dev has its own Access application on `berger-admin-dev.pages.dev`, set up the same way.
  - A Turnstile widget for `bookings.example.com.au`, and a separate one for `berger-booking-dev.pages.dev`.
  - A rate limiting rule on `bookings.example.com.au/api/*`. Dev has none (`pages.dev` is not in our zone); this is accepted.
- **Firestore collections:**
  - `boats`, `bookings`, `bookingDrafts`, `customers`, `customerEmails`, `boatUnavailabilities`, `boatLocks`
  - `seasons`, `blockedPeriods`, `addons`, `emailTemplates`, `settings`, `counters`
  - Composite indexes go in `firestore.indexes.json` (e.g. bookings by `boatId` + `status` + `startDate`; bookings by `addonIds` (array-contains) + `status` + `startDate`; unavailabilities by `boatId` + `firstNight`; bookingDrafts by `email` + `lastActivityAt`).
- **Logging and alerts:**
  - Structured logging to Cloud Logging. Never log card data or full tokens.
  - Log-based alert policies email the staff alert address on: webhook processing errors, job failures, and paid-but-expired bookings.
- **CI/CD (GitHub Actions):**
  - On PR: build, unit tests, integration tests against the Firestore emulator, web typecheck/lint/build.
  - On `main`: build and push both containers **once** (tagged with the commit SHA) → deploy both Cloud Run services to **dev** → build the frontends with dev's keys and deploy the dev Pages projects.
  - On a published GitHub Release (tag `vX.Y.Z`, pre-releases ignored): after approval by the `production` environment's required reviewers, deploy that commit's existing images to **prod** (fail if they do not exist, i.e. the commit never went through dev; never rebuild) → rebuild the frontends with prod's keys and deploy the prod Pages projects. Rolling back is re-running an older release's deploy.
  - Per-environment values live in GitHub Environments `dev` and `production` with the same variable names. Concurrency groups `deploy-dev` and `deploy-prod`.
  - Auth to GCP via Workload Identity Federation (no JSON keys), one pool per project.
- **Local development:**
  - Firestore emulator
  - Stripe test mode + Stripe CLI for webhooks
  - fake email sender
  - Turnstile test keys
  - `ASPNETCORE_ENVIRONMENT=Local` (never `Development`, which is the deployed dev environment), with settings in `appsettings.Local.json`
  - a dev bypass of Access JWT validation, the Scheduler OIDC check and the edge proxy secret, **only** when `ASPNETCORE_ENVIRONMENT=Local`
  - one command (e.g. `docker compose up` or a script) starts the emulator, both APIs and both Vite dev servers

## 15. Testing

- **Domain unit tests** (high coverage), at least:
  - Period mapping for every weekday in every mode, including boundary Mondays and Fridays.
  - Overlap rules: same-day changeover (8am check-out, 11am check-in) allowed; booking vs blocked/unavailability inclusive-night rules, including the 28/09→02/10 and 17/12→21/12 examples.
  - Lead time and open-until boundaries.
  - Season lookup, including the year-wrapping Peak range; price snapshotting; add-on pricing; price on request; no add-on prices for LongWeekend/Custom.
  - Payment schedule: standard vs extended, milestones anchored at 11:00 check-in, the 50%/100% cumulative milestones, amount due now, checkout always charging only `min(deposit, hire price)` even when the balance is already due, deposit > hire price, per-boat deposit falling back to the Settings default.
  - PaymentStatus derivation, including paid below the deposit → `PartPaid` and paid equal to the deposit → `DepositPaid`.
  - `TotalPrice` counts every priced add-on line and excludes unpriced ones.
  - Status transitions and illegal transitions.
  - Stand-by superseding.
  - Guest rules: adults ≥ 1, adults + children ≤ max guests (online), rooming warning trigger (`adults + children > AdultBeds + KidsBeds`).
  - Add-on capacity: busiest-night calculation including the four worked examples in §7.5; stand-by, Cancelled, Expired, Superseded and expired holds don't count; excluding a booking being edited.
  - Boat activation invariant.
  - Customer email normalisation; phone number E.164 normalisation.
  - Booking draft abandonment rules.
- **Application tests:**
  - Online vs internal validation strategies (including capacity hard limit online vs warning for staff).
  - Notification job, using a fake clock and short offsets: idempotency; which emails at which instants; #4 and #7 skipped when their trigger had passed at creation; #7 never sent for a milestone already due at creation; #1 including the payment link for late bookings; #6 sent for bookings created inside the pre-hire window.
  - Abandoned drafts: threshold, latest draft per email only, suppression after a booking for that email becomes Active, deletion after retention.
  - Webhook handling: idempotent replay, deposit activation creating/updating the Customer and converting the draft, expired-hold conflict path creating no Customer.
  - An expired hold never creates or changes a Customer.
- **Integration tests (Firestore emulator):**
  - Repositories round-trip.
  - **Concurrent booking race → exactly one winner.**
  - Hold expiry releases dates.
  - Optimistic concurrency 409.
  - Customer email uniqueness under concurrency.
  - Add-on capacity queries across boats.
- **Frontend:** unit tests (Vitest) for:
  - the calendar period mapping, per-period greying logic, and highlighting of non-clickable days inside a selection;
  - the check-in/check-out line, including the AEST/NSW and AEDT/NSW labels and the split label for a stay that crosses a daylight-saving change;
  - the "Selected: …" text (dates only, no times), and the Summary's check-in and check-out rows (each with its own zone label);
  - children dropdown clamping, and the capacity "remaining" display and hiding;
  - admin timeline week view date ranges (Mon–Sun, Prev/Next, Today) and the Month ↔ Week switching rules;
  - admin timeline bar positioning from check-in/check-out times (default and custom), and unavailability block positioning;
  - admin timeline lane assignment by check-in/check-out time (a standard changeover shares a lane; clashing custom times get a new lane).

## 16. Deliverables

1. The full solution as laid out in §6, building and passing all tests.
2. `/wordpress/book-now-block.html`.
3. `/docs/setup-checklist.md`, a step-by-step checklist a non-developer can follow:
   1. **Booking domain:** buy a separate domain for the booking app, add it to Cloudflare (free plan) and point its nameservers there. The existing `bergerhouseboats.com.au` DNS and email are not changed.
   2. GCP project, APIs, Firestore, service accounts, Secret Manager, Cloud Run, Scheduler, alerts, Workload Identity Federation for GitHub, GitHub Environments. The checklist is run once per environment (dev, then prod), with a table of the values that differ, and marks the steps that apply to only one of them (shared Artifact Registry, Releases, rate limiting, booking domain).
   3. Stripe account, webhook endpoint (`checkout.session.completed`, `checkout.session.expired`), payment method domains, switching from test to live keys.
   4. Gmail SMTP: 2-Step Verification and a Google app password for `info@bergerhouseboats.com.au`, and checking SPF, DKIM and DMARC for `bergerhouseboats.com.au` at its existing DNS host.
   5. Cloudflare Pages projects (booking custom domain; admin on `pages.dev` with no custom domain), `_headers`, Access app on the admin `pages.dev` address + Google IdP + policy, Turnstile, rate limiting.
   6. Run the seed. In the admin app, complete every boat (adult beds, kids beds, bedding, bond, optional deposit, rates, allowed add-ons) and activate it. Set add-on prices, order and car parking capacity. Enter blocked periods (Easter, long weekends) up to "bookings open until". Review email templates. Fill in contact details.
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
