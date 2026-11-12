# Berger Houseboats — House Boat Booking System Specification

You are building an online booking system for **Berger Houseboats** (NSW, Australia). This document is the complete specification. Every decision in it has been agreed with the business; do not re-open them. Where something is genuinely not covered, choose the simplest option consistent with this document and list it under "Assumptions made" in your final summary.

---

## 1. Context

Berger Houseboats hires out 14 house boats and 5 BBQ boats.

- BBQ boats are already booked through a third-party online system. That stays as is.
- House boats can currently only be booked by phone. Availability lives on paper and is duplicated in Excel. Staff cannot keep up with availability enquiries.
- Customers learn about each house boat on the existing WordPress site (`https://bergerhouseboats.com.au/houseboats/<boat-slug>/`). Each boat has its own page.

**Goal:** let customers see availability, compare the boats free for their dates, and book and pay a deposit for a house boat online, starting from a "Book Now" button on each boat's WordPress page. Give staff an internal system to manage bookings, the fleet, customers and settings.

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
- Generating hire agreements. The customer's address is stored in parts (§7.3) so it can fill one later.

## 3. Glossary

| Term | Meaning |
|---|---|
| **Period type** | `Midweek` (Mon→Fri), `Weekend` (Fri→Mon), `Week` (Mon→Mon or Fri→Fri), `LongWeekend` (staff only), `Custom` (staff only, any dates). |
| **Stay** | A booking's dates as a half-open range `[StartDate, EndDate)`. `StartDate` is the check-in day (check-in window **11am – 2pm**) and `EndDate` is the check-out day (from **8am**). The nights occupied are `StartDate … EndDate−1`. The standard times are fixed in code and not configurable. Staff may record custom check-in/check-out times on a booking (§7.3); these never affect availability or capacity. |
| **Same-day changeover** | Allowed. A booking ending Monday 8am and another starting Monday 11am do not overlap. |
| **Blocked period** | A staff-defined date range (Christmas/New Year, Easter, long weekends, …) that customers cannot book online. Any online stay that overlaps it shows a "please call or email us" message. Defined by inclusive `FirstNight` and `LastNight`. |
| **Unavailability** | A staff-defined range when a specific boat cannot be hired (maintenance, etc.). Defined by inclusive `FirstNight` and `LastNight`. |
| **Stand-by booking** | A "store credit" style booking (e.g. a customer with credit waiting for a date). It **never blocks availability**, is **never shown to customers**, **sends no emails**, and is **superseded** when an Active non-stand-by booking overlaps it. |
| **Hold** | A `PendingPayment` booking created when a customer starts checkout. It blocks the dates until payment completes, the hold expires, or the customer goes back from the Payment step. It has no booking reference until it is paid or confirmed by staff. An expired hold can be **confirmed by staff** with a manual payment (§7.3). |
| **Staff confirmation** | Staff recording a manual payment of at least the checkout amount on an `Expired` hold. The booking becomes Active exactly as if the deposit had been paid online (§7.3). |
| **Booking draft** | The details a customer has entered in the online booking app (from the "Your details" step onwards) before paying. It is **not** a customer record. If it is not converted into an Active booking, staff are alerted so they can follow up (email #10). |
| **Saved progress** | The booking app's step state, kept in the browser's `localStorage` so a refresh or a later visit resumes the booking. It never holds the customer's personal details; those are reloaded from the booking draft (§11.2). |
| **Address** | A customer's postal address in parts: Address line 1, Address line 2, City/Locality, State, Postcode and Country. Every part is optional (§7.3). |
| **Season** | A named recurring day-month range (e.g. Off Peak 1 May–31 Aug) used for pricing. Dates not covered by a season fall in the default **Normal** season. |
| **Extended payment schedule** | Applies to bookings whose stay overlaps a blocked period flagged `UsesExtendedPaymentSchedule` (Christmas/New Year). 50% of the total is due 120 days before check-in and 100% by 90 days before. All other bookings use the **standard schedule**: 100% due 30 days before check-in. |
| **Capacity-limited add-on** | An add-on with a `Capacity` shared across **all boats and bookings** (e.g. Car parking: 11 spots in total). Usage is counted per night (§7.5). |
| **Guests** | Every booking records `NumberOfAdults` (≥ 1) and `NumberOfChildren` (≥ 0, children are under 12). "Guests" means the total of both. |
| **Requested boat** | The boat named by the booking app's `?boat=<slug>` parameter, i.e. the boat whose "Book Now" button the customer clicked. Optional: the app also works without one. |
| **Selected boat** | The boat the customer has chosen to book on the "Dates & boat" step (§11.2). It starts as the requested boat when that boat is active, and may be none. |
| **Candidate boats** | Active `House` boats that match the customer's current filters on the "Dates & boat" step: guests (adults + children ≤ `Berth`), Spa and Pet friendly. |
| **Berth** | The number of people a boat sleeps (industry term, e.g. "a 12 berth houseboat"). Online, adults + children must not exceed it. |
| **Booking reference** | `LASTNAME-YYMMDDX`, e.g. `SMITH-2701150` (§7.3). |
| **Frequent Floater** | A customer who makes a booking while they have at least one other booking starting within a year either side of it (§7.8). Staff decide the perk; the system only flags it. |
| **Mobile** | Every customer phone number is a mobile number: **Mobile** (primary) and **Secondary mobile**. Landlines are not accepted (§7.1). |

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
    **Domains:** the booking app is public (customers never log in or sign up) on the subdomain `bookings.bergerhouseboats.com.au` of the existing domain. It is a Cloudflare Pages custom domain reached by a single CNAME (`bookings` → `berger-booking.pages.dev`) added at the existing DNS host; the domain is **not** a Cloudflare zone. The admin app has no custom domain and is served at `berger-admin.pages.dev` behind Access. The existing `bergerhouseboats.com.au` DNS and email stay where they are (only that one CNAME is added). If the DNS host cannot add a CNAME, the fallback is to buy a separate domain, put it on Cloudflare (free plan) and use that for the booking app instead. The deployed dev environment (§14) uses `berger-booking-dev.pages.dev` and `berger-admin-dev.pages.dev`.
14. Customer email is **mandatory** everywhere, including staff-created bookings. Customers are matched by normalised email. For online bookings, a `Customer` record is created or updated **only when the booking becomes Active** (deposit paid online, or staff confirmation). Until then the details live in the booking draft and on the booking.
15. A booking that overlaps an Active or PendingPayment booking, or an unavailability, is **never** allowed, for staff or customers. Stand-by bookings are the exception: they ignore other stand-bys and are superseded rather than blocked.
16. Frontend: **React 19 + TypeScript + Vite**, organised with the **Bulletproof React** structure, with a co-located **CSS Module per component** (§6.1). Hosted on Cloudflare Pages.
17. Minimum lead time for online bookings is **30 days** (Settings). The latest date bookable online is the **"Bookings open until"** date (Settings), which staff move forward each year after entering the next year's blocked periods and prices. There is no rolling window.
18. On the Customers admin page only **Customer comments** are editable. Contact details are edited from the booking dialog. Bookings can be opened from the page.
19. Guests are split into **adults (≥ 1)** and **children (under 12)**. Online, adults + children must not exceed the boat's **Berth**. The rooming warning compares adults + children with the boat's adult beds + kids beds.
20. **Add-on lines have no status.** A line on a booking is accepted. Staff decline an add-on by deleting the line. In the admin app, add-on lines are edited as part of the booking and saved only with it.
21. Add-ons may have a **capacity shared across all boats**, counted on the busiest night of the stay. Online it is a hard limit, checked without a lock. Staff only get a warning.
22. Abandoned online bookings are reported to staff (#10). They are never stored as customers.
23. Payment emails (#4 and #7) whose trigger time had already passed when the booking became Active are **never sent**.
24. The booking app's first step, **"Dates & boat"**, searches the whole fleet. The customer picks the period, guests and filters, and chooses one of the candidate boats. The requested boat is **always** listed first. A checkbox "Show {Boat}'s availability only" limits the calendar to the selected boat; otherwise the calendar offers every period at least one candidate boat is free for.
25. Boats have an optional **image URL** (an image on the WordPress site, shown on the booking app's boat cards), and **Spa** and **Pet friendly** flags that customers can filter by.
26. **Booking references** are `LASTNAME-YYMMDDX` (§7.3). They are assigned when an online booking's deposit is paid or a staff booking is created, and never change afterwards.
27. Customers have separate **first name** and **last name** fields everywhere.
28. **Frequent Floaters** (repeat customers) are flagged to staff. The perk is decided by staff, never by the system (§7.8).
29. Every customer phone number must be a **mobile** number.
30. **Nothing is saved until the user saves.** A form, including the add-on lines of a booking, is saved only by its main button. While any backend request runs, the button that started it is disabled and shows progress (§6.1).
31. Customers can go **back from the Payment step**. Doing so releases their hold.
32. **Fields are validated as soon as they lose focus**, in every form of both apps (§6.1). The server repeats every check on save.
33. On the admin timeline, clicking an empty cell (or "+ New", using today) pre-fills the **Mid-week or Weekend period containing that day**, the same mapping as the booking app's calendar: Mon–Thu → Mid-week, Fri–Sun → Weekend (§12.1).
34. Customers have an optional **Address** in parts (Address line 1, Address line 2, City/Locality, State, Postcode, Country), entered by staff only. No part is required, so staff can enter what they know as they learn it. The booking app never asks for it.
35. **Backend code follows build-enforced organisation rules** (§7.9): one top-level type per file; one thin controller per resource, each paired with one application service (§8); pure booking validation strategies; a separate repository interface and Firestore implementation per aggregate; typed ids and value objects throughout the domain. Analyzers and architecture tests fail the build when a rule is broken.
36. **Holds are read-only in the admin app.** A `PendingPayment` hold cannot be edited or paid by staff, because the customer may still pay online. Once it is `Expired`, the customer can no longer pay online, and staff may **confirm** it by recording a manual payment of at least the checkout amount (§7.3).
37. The booking app keeps **saved progress** in `localStorage`, so a refresh or a later visit resumes the booking. Personal details are never stored in the browser; they are reloaded from the booking draft (§11.2).

## 5. Architecture overview

```
WordPress boat page ──(HTML block: "Book Now")──► modal <iframe> (desktop) / new tab (mobile)
                                                        │
                                       bookings.bergerhouseboats.com.au  (Cloudflare Pages: booking app)
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
- **CORS:** both APIs only allow their own frontend origin (`https://bookings.bergerhouseboats.com.au` and `https://berger-admin.pages.dev` respectively; `https://berger-booking-dev.pages.dev` and `https://berger-admin-dev.pages.dev` in dev). The origin has no default in code: a service without one refuses to start, so an environment can never fall back to another environment's frontend. There are no wildcard origins and credentials are not allowed cross-origin. Since traffic is proxied same-origin, CORS is defence in depth.
- **Instances:** `public-api` runs with `max-instances=1`, `admin-api` with `max-instances=2`. At the expected scale (about 1000 bookings a year, one staff user at a time) one public instance is enough, and the per-IP rate limiter (§8) keeps its state in memory, so it is only exact on a single instance.
- **Cold starts:** both services run with `min-instances=0` and are published with ReadyToRun. There is no separate "wake-up" endpoint. The booking app calls `GET /api/boats` (and `GET /api/boats/{slug}` for the requested boat) as soon as it loads, which wakes the service while a skeleton loader is shown.

## 6. Repository layout

```
/src
  BergerHarbour.Domain/            # aggregates, typed ids, value objects, domain services, repository interfaces (§7.9)
    Boats/  Bookings/  BookingDrafts/  Customers/  Unavailabilities/  Settings/  Shared/
  BergerHarbour.Application/       # application services (one per controller, §8), collaborators, DTOs
    Abstractions/                  # ports: IPaymentGateway, IEmailSender, IClock, ITurnstileVerifier, IBoatScheduleLock, IBookingReferenceGenerator
    Boats/  Bookings/  Bookings/Validation/  BookingDrafts/  Customers/  Unavailabilities/  Settings/  Notifications/  Payments/  Seed/
      Dtos/                        # in each area: request and response records
  BergerHarbour.Infrastructure/
    Firestore/                     # Repositories/, Documents/, the boat lock, the reference counter, collection names
    Stripe/  Email/ (Gmail SMTP)  Turnstile/  Cloudflare/ (Access JWT validation)  Time/ (SystemClock)
    Web/                           # hosting defaults, proxy secret, rate limiting
  BergerHarbour.PublicApi/         # Controllers/ for customers + Stripe webhook (§8)
  BergerHarbour.AdminApi/          # Controllers/ for staff + jobs endpoint (§8)
/tests
  BergerHarbour.Domain.Tests/
  BergerHarbour.Application.Tests/
  BergerHarbour.Infrastructure.IntegrationTests/   # against the Firestore emulator
  BergerHarbour.ArchitectureTests/                 # layer and structure rules (§7.9)
Directory.Build.props  .editorconfig  stylecop.json   # analyzer rules (§7.9)
/web
  shared/        # components, lib (http client, generated API types), utils, styles (§6.1)
  booking/       # React app (Bulletproof structure) + functions/api/[[path]].ts
  admin/         # React app (Bulletproof structure) + functions/api/[[path]].ts
/wordpress
  book-now-block.html             # the HTML block pasted into each boat page
/docs
  setup-checklist.md              # booking domain, GCP, Cloudflare, Stripe, Gmail SMTP, go-live
firestore.indexes.json
.github/workflows/
```

### 6.1 Frontend structure
- **Stack:** React 19, TypeScript and Vite in npm workspaces (`shared`, `booking`, `admin`).
- **Libraries:**
  - **TanStack Query** for all server data (queries, caching and mutations). A mutation's pending state drives the busy buttons described below.
  - **React Router** in the admin app only (`/bookings`, `/fleet`, `/customers`, `/settings`). The booking app's steps are internal state, not routes. The pay page `/pay/` is a separate Vite entry of the booking app.
  - **React Hook Form** for every form, created through one shared `useAppForm` hook that sets `onTouched` mode (see Field validation below), with `useFieldArray` for a booking's add-on lines. Forms with a zod schema (the booking dialog, the booking app's details step and the manual payment dialog) use `zodResolver`; the others use `register` validation rules. Both mirror the server's rules and messages. The API's per-field ProblemDetails errors are mapped onto the matching form fields. Client-side validation is a convenience; the server is authoritative.
  - **`@stripe/react-stripe-js`** for Embedded Checkout, **`@marsidev/react-turnstile`** for Turnstile, and **`libphonenumber-js`** behind the shared `MobileInput` component.
  - **No global client-state library.** The booking app's step state (period, guests, filters, selected boat, details, add-ons, draft id) lives in one `useReducer` behind a React context. Everything in it except the personal details is also written to `localStorage` as saved progress (§11.2). Everything else is server data (TanStack Query) or local to a component.
- **Bulletproof React layout**, in each app:
  ```
  src/
    app/                 # providers (QueryClient, router), routes, app-level composition
    features/<feature>/
      api/  components/  hooks/  types/  utils/
    components/          # app-wide UI
    hooks/  lib/  utils/  config/  testing/
  ```
  - **Booking app features:**
    - `dates-and-boat`, `details`, `addons`, `summary`.
    - `deposit-checkout`: the Payment step, which creates the hold, mounts Embedded Checkout for the deposit with the hold countdown, and handles Back and the hold running out.
    - `complete`.
    - `balance-payment`: the `/pay/?token=` page reached from payment links in emails.
    - The `<EmbeddedCheckout>` wrapper used by `deposit-checkout` and `balance-payment` lives in the app's `src/components`.
  - **Admin app features:** `bookings` (timeline and booking dialog), `fleet`, `customers`, `settings`.
  - **`web/shared`** holds what both apps use:
    - `components/`: Button, Dialog, Toast, Spinner (a small size for buttons and a large size for an area that is loading), Field, MobileInput.
    - `lib/`: the http client and the generated API types.
    - `utils/`: dates, periods, phone and money formatting.
    - `styles/`: `tokens.css` and a reset.
  - **Import rules**, enforced by ESLint (`import/no-restricted-paths`):
    - A feature never imports from another feature, and `shared` never imports from an app.
    - Imports flow `shared` → app `components/hooks/lib/utils` → `features` → `app`.
    - When a page needs another feature's component (the Customers page opens the booking dialog), the two are put together in the `app` layer (the route), not through a cross-feature import.
- **Styling:** every component has its own co-located CSS Module (e.g. `BoatCard.tsx` + `BoatCard.module.css`). The only global CSS is `shared/styles/tokens.css` (design tokens) and a small reset, loaded once per app. No Tailwind, no CSS-in-JS.
- **Busy states (both apps):**
  - Every button that sends a backend request is disabled while the request runs and shows a spinner with a progress label ("Creating…", "Saving…", "Sending…"). It can never be submitted twice.
  - In a dialog, the other actions and the fields are locked while it saves.
  - On success, the dialog closes, a toast confirms, and the data it changed (e.g. the timeline) refreshes.
  - On error, the dialog stays open with the error shown and everything the user typed kept.
- **Field validation (both apps, every form):**
  - A field is validated when it loses focus, so an invalid email, mobile or number is reported straight away, not on save. Leaving a required field empty shows its "Required" error.
  - Once a field shows an error, it is re-validated on every change, so the error clears as soon as the value is fixed. Changing the country in `MobileInput` re-validates a number already entered.
  - Rules that involve two fields (e.g. Berth ≥ adult beds + kids beds, first night ≤ last night, start date < end date) are checked when either field loses focus, and the error shows on the field that owns it.
  - The main button stays enabled. Pressing it validates every field and moves focus to the first invalid one.
  - Rules only the server can check (overlaps, version conflicts, unknown email placeholders, online add-on capacity) are reported on save.
- **No intermediate saves:** a form is saved only by its main button ("Create booking", "Save changes", "Save"). Editing part of a form, such as adding an add-on line, never saves anything and never resets other fields.

## 7. Backend

### 7.1 Technology and style
- **.NET 10**, ASP.NET Core with controllers, C#. Nullable reference types enabled.
- **DDD:**
  - Validation and invariants are encapsulated in domain objects (constructors, factory methods and intention-revealing methods such as `booking.Cancel()`, `booking.RecordPayment(...)`, `boat.Deactivate()`). Domain objects are never in an invalid state. There are no public setters on aggregates.
  - **Small aggregates referencing each other by Id** (Vaughn Vernon). Aggregates: `Boat`, `Booking`, `BookingDraft`, `Customer`, `BoatUnavailability`, `Season`, `BlockedPeriod`, `AddonDefinition`, `EmailTemplate`, `BusinessSettings`. If a natural root appears later, they can be merged; do not pre-merge.
  - **Domain services** for cross-aggregate rules, e.g. `BookingAvailabilityService` (overlap checks given loaded bookings, unavailabilities and blocked periods), `PricingService`, `PaymentScheduleService`, `AddonCapacityService`, `StayPeriodFactory` (period type + clicked date → stay), `FrequentFloaterService` (§7.8).
- **Repositories:** one repository interface and one implementation per aggregate.
  - The interface is its own file next to its aggregate in the Domain project (`Domain/Bookings/IBookingRepository.cs`), never declared inside the aggregate's file.
  - The implementation is `Infrastructure/Firestore/Repositories/Firestore<Aggregate>Repository.cs` (§7.9).
- **Validation strategy pattern** for booking requests, in `Application/Bookings/Validation/`, one type per file:
  - `IBookingRequestValidationStrategy` has one method: `ValidationErrors Validate(BookingRequest request, BookingValidationContext context)`. It is **synchronous and pure**. Strategies have no constructor dependencies and do no I/O: no repositories, no ports, no clock.
  - `BookingRequest` is the request as either channel sends it: period type, start date, end date (staff only), adults, children, contact details, add-on quantities, and the online acceptance fields (rooming warning, terms, draft id). Fields a channel doesn't send are empty.
  - `BookingValidationContext` holds everything the rules need. The calling application service loads it before validating: the boat, `BusinessSettings`, blocked periods, add-on definitions, today (Australia/Sydney) and the scope. Online it also holds the booking draft and the remaining capacity of each capacity-limited add-on for the stay (§7.5).
  - `BookingValidationScope` is `Quote` or `Submit`. `Quote` skips the draft, terms, rooming acceptance and contact details.
  - `OnlineBookingValidationStrategy` is registered in `PublicApi`. It enforces the rules in §7.7: period shape, lead time, open-until, blocked periods, adult/child limits, rooming warning acceptance, terms acceptance, allowed add-ons, add-on capacity and a valid booking draft.
  - `InternalBookingValidationStrategy` is registered in `AdminApi`. It enforces only `StartDate < EndDate`, `NumberOfAdults >= 1`, `NumberOfChildren >= 0`, and a valid customer: first name, last name and a valid email are required; Mobile and Secondary mobile are optional but must be valid mobile numbers; the address follows the `Address` rules (§7.3). It applies on create and on every update.
  - Strategies return errors only. Staff's non-blocking warnings come from `StaffBookingWarnings`, a separate class that is not a strategy: guests above berth, guests above beds, add-on capacity exceeded, and a custom time clashing with a same-day changeover. It is pure too and works on data the application service has loaded.
  - Both strategies are always followed by the availability invariant (decision 15), which is not part of either strategy.
- **Money:** `decimal` AUD in the domain, stored as integer cents. All amounts include GST.
- **Dates:** `DateOnly` in the domain, stored as `yyyy-MM-dd` strings, which sort correctly for range queries. Times of day are `TimeOnly`, stored as `HH:mm`. Instants are stored as UTC timestamps.
- **Mobile numbers:** every customer number (Mobile and Secondary mobile) must be a **mobile** number, held as a `MobileNumber` value object.
  - It must be valid for its country and of number type `MOBILE`, or `FIXED_LINE_OR_MOBILE` for countries where the two can't be told apart (e.g. the US and Canada). Landlines are rejected.
  - It is checked with `libphonenumber-csharp` on the server and `libphonenumber-js` in the browser. The error is "Enter a valid mobile number for the selected country."
  - It is stored in E.164 format (e.g. `+61412345678`), and shown in national format for Australian numbers and international format for others.
  - The business's contact phone (Settings) is not restricted.
- **Email addresses:** one rule, in the `EmailAddress` value object, for every email in the system (customers, booking drafts, the staff alert and contact emails in Settings). The browser uses the same rule.
  - Checked after trimming; at most 254 characters.
  - Exactly one `@`, with a non-empty local part without spaces.
  - The domain is two or more dot-separated labels of letters, digits and hyphens. No label is empty or starts or ends with `-`. The last label (the top-level domain) is letters only and at least 2 long.
  - Accepted: `jane@gmail.com`, `jane@bigpond.net.au`, `jane@example.co.uk`. Rejected: `jane@gmail`, `jane@b.c`, `jane@gmail..com`, `jane@-gmail.com`, `jane@gmail.c0m`, `jane smith@gmail.com`.
  - The error is "Enter a valid email address."
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
| `Berth` | ≥ 1. How many people the boat sleeps. Online, `NumberOfAdults + NumberOfChildren` must not exceed it. Always ≥ `AdultBeds + KidsBeds`. |
| `AdultBeds` | Number of physical adult beds; a double or queen counts as **one** bed. |
| `KidsBeds` | Number of physical kids beds (e.g. bunks). May be 0. `AdultBeds + KidsBeds` drives the rooming warning. |
| `BeddingDescription` | Free text, e.g. "4 queen bedrooms, bunk area (2 singles), single in lounge, single in dining area" |
| `SecurityBond` | Money, display only |
| `DepositAmount` | Optional money (> 0 when set). When empty, `BusinessSettings.DepositAmount` applies. **Effective deposit** = boat value ?? Settings value. |
| `Rates` | List of `BoatRate { SeasonId, PeriodType (Midweek/Weekend/Week/LongWeekend), Price }` |
| `AllowedAddonIds` | Add-ons offered for this boat |
| `ImageUrl` | Optional. An absolute `https://` URL whose host is `bergerhouseboats.com.au` or `www.bergerhouseboats.com.au` (an image on the WordPress site). Shown on the booking app's boat cards and in Fleet. |
| `HasSpa` | bool, default false |
| `IsPetFriendly` | bool, default false |
| `IsActive` | Inactive boats cannot be booked online or by staff, but their history is kept. Boats are never deleted. |
| `CreatedDate`, `ModifiedDate`, `Version` | |

**Invariants:**
- **Activation:** a boat can only be activated when `AdultBeds`, `KidsBeds` (0 allowed), `BeddingDescription` and a rate for **every season × {Midweek, Weekend, Week}** are present. `ImageUrl`, `HasSpa` and `IsPetFriendly` are not required.
- **Berth and beds:** `Berth >= AdultBeds + KidsBeds`. It applies always (active or not), on create and on every edit. A violation is rejected with a field error on `berth`: "Berth can't be less than adult beds + kids beds ({n})."

**Ordering:** everywhere boats are listed (admin timeline rows, Fleet grid, every boat dropdown, and the booking app's "Other boats available" list), boats are sorted by `Berth` descending, then `Name`.

#### Customer (aggregate)
`Id`, `FirstName` (required), `LastName` (required), `Email` (required, stored normalised to lower-case trimmed, **unique**), `MobileNumber` (E.164 mobile; required for online bookings, optional for staff-created), `SecondaryMobileNumber` (E.164 mobile, optional, staff-entered only, empty when the customer is created), `Address` (optional `Address` value object, staff-entered only, empty when the customer is created), `Comments` (optional), `CreatedDate`, `ModifiedDate`, `Version`.

- The customer's name is displayed everywhere as `FirstName LastName`.
- `Address` has the parts `Line1`, `Line2`, `Locality` (City/Locality), `State`, `Postcode` and `Country`:
  - **No part is required.** Staff may store a partial address and complete it later.
  - Every part is trimmed, and a blank part is stored empty. Limits: `Line1` and `Line2` 100 characters, `Locality` and `State` 60, `Postcode` 10.
  - `Country` is an ISO 3166-1 alpha-2 code (e.g. `AU`) from a hard-coded list. Any other value is rejected ("Choose a country from the list.").
  - When `Country` is `AU`, a `State` must be one of `NSW`, `VIC`, `QLD`, `SA`, `WA`, `TAS`, `NT`, `ACT` ("Choose an Australian state or territory.") and a `Postcode` must be 4 digits ("Enter a 4-digit postcode."). For other countries both are free text.
  - An address whose only part is `Country` counts as no address and is stored empty.
  - It is not available to email templates. It is kept to fill hire agreements later (§2).
- `Comments` are staff-only notes about the customer (e.g. preferences, history). They are shared by all of the customer's bookings, edited from the Customers page and the booking dialog, never shown to the customer, and never available to email templates.
- Email uniqueness is enforced with a `customerEmails/{normalisedEmail}` index document written in the same transaction.
- When an existing email books again, `FirstName`, `LastName` and `MobileNumber` are updated to the latest values. `SecondaryMobileNumber`, `Address` and `Comments` are only ever changed by staff.
- Online, the customer is created or updated **only when the booking becomes Active** (deposit paid online, or staff confirmation), from the booking's `CustomerDetails` snapshot. Abandoned drafts and expired holds that staff don't confirm never create or change a customer.

#### BoatUnavailability (aggregate)
`Id`, `BoatId`, `FirstNight`, `LastNight` (inclusive, `FirstNight <= LastNight`), `Comments`, `CreatedDate`, `ModifiedDate`, `Version`.

Creating or editing one that overlaps an Active or PendingPayment non-stand-by booking is rejected. The error names the booking reference(s).

#### Booking (aggregate)
| Field | Notes |
|---|---|
| `Id` | |
| `Reference` | Human-readable `LASTNAME-YYMMDDX` (e.g. `SMITH-2701150`, see **Booking references** below). Empty until assigned: online bookings get it when the deposit is paid or on staff confirmation, staff bookings at creation. Never changes once assigned. |
| `CreatedBy` | `Customer` (website) \| `SystemUser` (staff) |
| `IsStandby` | bool |
| `Status` | `PendingPayment` \| `Active` \| `Superseded` \| `Cancelled` \| `Expired` |
| `ActivatedAt` | Instant the booking first became Active (deposit paid online or staff confirmation; creation for staff bookings). Not changed by later Cancelled → Active transitions. Anchors the Frequent Floater check (§7.8) and the "already due at activation" rule (§7.6). |
| `CustomerId` | Id reference. For online bookings it is set when the booking becomes Active; it is empty while `PendingPayment` and `Expired`. |
| `CustomerDetails` | Online bookings: snapshot `{ FirstName, LastName, Email, MobileNumber }` entered by the customer. Used to create or update the Customer on activation, and shown to staff for bookings without a `CustomerId`. |
| `BookingDraftId` | Online bookings: the draft this booking came from |
| `BoatId` | Id reference |
| `PeriodType` | `Midweek` \| `Weekend` \| `Week` \| `LongWeekend` \| `Custom` |
| `StartDate`, `EndDate` | Stay `[Start, End)`, `Start < End` |
| `NumberOfAdults` | ≥ 1 |
| `NumberOfChildren` | ≥ 0 (children under 12) |
| `CheckInTime`, `CheckOutTime` | `TimeOnly`, default `11:00` and `08:00` for every booking (online and staff). Staff-only: never shown in the booking app. They do not affect availability or capacity. |
| `BookingComments` | optional, staff-only notes about this booking (customer notes live in `Customer.Comments`) |
| `HirePrice` | Snapshot at creation. Staff may override. |
| `DepositAmount` | Snapshot of the boat's effective deposit at creation |
| `PaymentSchedule` | `Standard` \| `Extended` (derived from blocked-period overlap at creation, and re-derived when dates change) |
| `AddonLines` | List of `AddonLine { Id, AddonId, NameSnapshot, Quantity, UnitPrice (nullable until staff set it) }`. There is **no line status**: a line on the booking is accepted; staff decline by deleting it. Staff edit lines as part of the booking and they are saved with it (§8.2). |
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

**Booking references** (`BookingReference` value object; the suffix comes from the `IBookingReferenceGenerator` port):
- **Format:** `LASTNAME-YYMMDDX`. That is the customer's last name, then the booking's **StartDate** as `yyMMdd`, then a suffix `X`. Example: John Smith from 15/01/2027 → `SMITH-2701150`. Andy Smith on the same start date → `SMITH-2701151`.
- **Name part:** the last name upper-cased with accents removed (Müller → `MULLER`), and every character other than A–Z dropped (O'Brien → `OBRIEN`, Smith-Jones → `SMITHJONES`, van der Berg → `VANDERBERG`). It is cut to at most 20 characters. If nothing is left (e.g. a name in a non-Latin script), it is `GUEST`.
- **Suffix:** `X` is the number of bookings already given a reference with the same `LASTNAME-YYMMDD` prefix, of any status. It starts at 0 and continues past 9 (`…10`, `…11`). This also covers the same customer booking two boats on the same date. It comes from a counter document per prefix, incremented transactionally, so references are unique.
- **When assigned:**
  - Staff bookings: at creation.
  - Online bookings: when the deposit is paid, on activation and also in the paid-but-expired case (§8.3), so staff can find it; or on staff confirmation.
  - Unpaid holds (expired or released) that staff don't confirm never get a reference and use up no suffix. A staff confirmation that fails because the dates were taken may use up a suffix.
- **Never changes:** not even if the start date or the customer's last name changes later. The reference appears in emails, Stripe and bank-transfer descriptions.
- **Search:** references are matched case-insensitively.

**Status transitions:**
- `PendingPayment → Active` when the deposit payment succeeds. **Activation** (`ActivateFromDeposit`) assigns the reference, sets `ActivatedAt`, creates or updates the Customer from `CustomerDetails`, sets `CustomerId`, marks the booking draft converted (unless it already is), and supersedes overlapping stand-bys.
- `PendingPayment → Expired` at the **earliest** of:
  - the customer going back from the Payment step (release, §8.1);
  - the Stripe Checkout session expiring (`checkout.session.expired`, §8.3);
  - `HoldExpiresAt` passing. admin-api **reports** a lapsed hold as `Expired` whenever it returns one (timeline, search, booking dialog, a customer's bookings), so staff never see a lapsed hold as `PendingPayment`. These reads never write. Staff confirmation expires a lapsed hold and confirms it in the same transaction. The hourly job saves the expiry of the rest (§9).
- `Expired → Active`, with the same activation as above, inside `IBoatScheduleLock` and only when the dates are still free:
  - **Online:** a deposit whose webhook arrives after the hold expired (§8.3).
  - **Staff confirmation:** staff record a manual payment of **at least the checkout amount** `min(DepositAmount, HirePrice)` (§7.6) on an Expired booking that has no payments. The payment is recorded and the booking activated in the same transaction. `CreatedBy` stays `Customer`, and the prices stay as snapshotted.
    - A smaller amount is rejected with a field error on `amount`: "To confirm this booking, record at least {amount}." Nothing is recorded.
    - If the dates are now taken, it returns `409` naming the conflicting booking references. Nothing is recorded.
    - An Expired booking that already has a payment (paid-but-expired, §8.3) cannot be confirmed by staff; it is refunded in Stripe.
- `Active → Cancelled` (staff).
- `Cancelled → Active` (staff, subject to the availability check).
- `Active (stand-by) → Superseded`. This is automatic when an Active non-stand-by booking for the same boat overlaps it.
- Staff-created bookings start as `Active` (no hold), with their reference and `ActivatedAt` set at creation.

**Staff editing by status:** `PendingPayment` and `Expired` bookings are **read-only**. A staff update of either is rejected, and no manual payment or payment link is accepted for a `PendingPayment` booking. The only staff action on an `Expired` booking is staff confirmation. Bookings in every other status are edited as described in §8.2.

**What blocks availability:** non-stand-by bookings with status `Active`, and non-stand-by `PendingPayment` bookings whose `HoldExpiresAt` is in the future. Also boat unavailabilities, and (online only) blocked periods. Stand-by, Superseded, Cancelled and Expired bookings never block.

**Stand-by bookings:**
- They may not overlap Active or PendingPayment non-stand-by bookings or unavailabilities. Creating one on taken dates is pointless.
- They may overlap other stand-bys.
- When any non-stand-by booking **becomes Active**, all overlapping Active stand-by bookings for that boat become `Superseded` and email #8 is sent.

#### BookingDraft (aggregate)
| Field | Notes |
|---|---|
| `Id` | Random and unguessable (≥ 128 bits). Returned to the booking app, which keeps it in its saved progress (§11.2). Holding it is enough to read the draft's personal details back (`GET /api/booking-drafts/{draftId}`). |
| `BoatId` | The selected boat. Updated when the customer goes back and chooses another boat. |
| `PeriodType`, `StartDate`, `EndDate` | As selected on the "Dates & boat" step |
| `NumberOfAdults`, `NumberOfChildren` | As selected on the "Dates & boat" step |
| `FirstName`, `LastName`, `Email` (normalised), `MobileNumber` (E.164 mobile) | Required when the draft is created |
| `AddonRequests` | List of `{ AddonId, Quantity }` |
| `LastStepReached` | `Details` \| `Addons` \| `Summary` \| `Payment` |
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
  - `AccountName`, `Bsb`, `AccountNumber` (all optional): the business's bank account, for email templates that ask customers to pay by bank transfer instead of, or as well as, a payment link. `Bsb` is 6 digits (stored without a separator, shown `123-456`). `AccountNumber` is 6–10 digits.
  - `Version`
- `Season`: `Id`, `Name`, `Ranges: [{ StartDayMonth, EndDayMonth }]` (recurring every year; a range may wrap the year end, e.g. 01-12 → 31-01), `Version`.
  - Exactly one built-in season `Normal` has no ranges and is the default. It cannot be deleted.
  - Season ranges must not overlap each other.
- `BlockedPeriod`: `Id`, `Name`, `FirstNight`, `LastNight` (inclusive), `UsesExtendedPaymentSchedule` (bool), `Version`.
- `AddonDefinition`:
  - `Id`, `Name`, `Description`
  - `PriceOnRequest` (bool). When true, staff set the price per booking line, and no `Prices` are stored (any sent are cleared).
  - `Prices` by period type (Midweek, Weekend, Week; ignored when price on request). There are no add-on prices for LongWeekend or Custom bookings; staff enter them per line.
  - `QuantityApplies` (bool; when false, quantity is always 1)
  - `Capacity` (optional whole number ≥ 1). When set, usage is shared across all boats and bookings (§7.5).
  - `CapacityUnitLabel` (text, at most 30 characters, e.g. "car spots"), used in the "remaining" message. **Required when `Capacity` is set**; cleared when it isn't.
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
  - When staff add an add-on line, its unit price is pre-filled with the add-on's price for the booking's period type. It is left empty for price-on-request add-ons and for LongWeekend and Custom bookings. Staff may change it.
  - A "Recalculate from rates" action re-prices on request. It updates the hire price, and every add-on line whose add-on has a price for the booking's period type; other lines are left as they are. The new values show in the form and are saved with it.
  - Changing the dates or period type does **not** silently re-price the hire price or existing lines.
- **Online,** prices are always calculated on the server, and prices sent by the booking app are ignored.
- **Staff** prices are accepted as typed. The server recalculates every total on save.

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
- **Online checkout amount** (the **checkout amount**) = `min(DepositAmount, HirePrice)`, **always**, even when the balance milestone is already due. The rest is requested through the payment link in email #1. It is also the least a manual payment must be to confirm an expired hold (§7.3).
- A milestone whose due instant is at or before the booking's `ActivatedAt` is **already due at activation**. See §9 for how notifications treat it.

### 7.7 Online booking rules (`OnlineBookingValidationStrategy`)
1. The boat exists, `IsActive`, and `Type == House`.
2. `PeriodType ∈ {Midweek, Weekend, Week}`:
   - Midweek starts Monday and ends Friday (+4).
   - Weekend starts Friday and ends Monday (+3).
   - Week starts Monday or Friday and ends +7.
3. `StartDate >= today + MinimumLeadTimeDays`.
4. `EndDate − 1 <= BookingsOpenUntil` (every night is inside the open window).
5. The stay does not overlap any `BlockedPeriod`.
6. `NumberOfAdults >= 1`, `NumberOfChildren >= 0`, and `NumberOfAdults + NumberOfChildren <= Berth`.
7. If `NumberOfAdults + NumberOfChildren > AdultBeds + KidsBeds`: rooming acceptance is present and its text matches the warning the server would generate.
8. Terms accepted.
9. Every add-on is active and in `boat.AllowedAddonIds`. `Quantity >= 1`, and it is 1 when `!QuantityApplies`. For capacity-limited add-ons, `Quantity <= remaining` (§7.5).
10. First name, last name, email (a valid email address, §7.1) and mobile (a valid mobile number for its country, §7.1, E.164) are required.
11. A valid, unconverted `BookingDraft` id for this boat is supplied. (Turnstile was verified when the draft was created.)

Then the availability invariant runs inside `IBoatScheduleLock`.

### 7.8 Frequent Floaters (`FrequentFloaterService`)
Repeat customers get a perk. Staff decide what it is; the system only flags them.
- **Rule:** a booking is a **Frequent Floater booking** when the same customer (matched by normalised email) has **at least one other** Active non-stand-by booking whose `StartDate` lies within one year either side of the anchor: `anchor − 1 year <= StartDate <= anchor + 1 year`, comparing Australia/Sydney dates. Cancelled, Expired, Superseded, PendingPayment and stand-by bookings never count.
- **Anchor:** the moment the booking became Active (`ActivatedAt`: deposit paid online, creation for staff bookings). Because the anchor is fixed, an existing booking's flag doesn't change just because time passes. A booking still being created (the admin new-booking form) uses now.
- **Result:** `{ isFrequentFloater, otherBookingCount }`.
- **Where it shows:**
  - In email #2 through `{{FrequentFloaterNotice}}` (§9).
  - As a badge in the admin booking dialog (§12.1).
  - Not on the Customers page.

### 7.9 Code organisation
These rules apply to every backend project, tests included. The build enforces them (see **Enforcement**). They are not relaxed for small types.

- **One top-level type per file.**
  - Every class, record, interface, enum and struct is in its own file, named after the type (`BoatId.cs`, `SaveBoatRequest.cs`, `IClock.cs`). Nested private types are the only exception.
  - There are no catch-all files such as `Ports.cs`, `Dtos.cs`, `DomainServices.cs`, `Repositories.cs` or `Documents.cs`.
- **Folders follow areas.** In each project, types sit in an area folder (§6) and the namespace matches the folder.
- **Domain:**
  - An aggregate's file contains only the aggregate. Its typed id, child entities, value objects, enums and repository interface each have their own file in the same folder, e.g. `Domain/Boats/`: `Boat.cs`, `BoatId.cs`, `BoatRate.cs`, `BoatType.cs`, `IBoatRepository.cs`.
  - **Typed ids:** every aggregate has one, a `readonly record struct` wrapping the string value: `BoatId`, `BookingId`, `BookingDraftId`, `CustomerId`, `BoatUnavailabilityId`, `SeasonId`, `BlockedPeriodId` and `AddonId` (for `AddonDefinition`). `EmailTemplate` is keyed by `EmailTemplateKey`, and `BusinessSettings` is a singleton. Domain, Application, ports and repositories use typed ids everywhere. Plain strings appear only in DTOs, routes and Firestore documents.
  - **Value objects** carry every rule about a value's format: `EmailAddress`, `MobileNumber`, `Address`, `BookingReference`, `StayRange`, `NightRange`, `DayMonth`.
    - Each is immutable and built only through a factory that validates, e.g. `EmailAddress.Create(raw)` throws a field error.
    - It is the type of every field it applies to: `Customer.Email`, `BookingDraft.Email` and `CustomerDetails.Email` are `EmailAddress`, and the mobile fields are `MobileNumber`.
    - Static helper classes never stand in for a value object. Money stays `decimal` (§7.1).
  - Each domain service (§7.1) has its own file, and so does each record it returns.
- **Application:**
  - **One application service per controller**, named `<Name>Service`, in its area folder. §8 lists the pairs. A service holds that controller's use cases and nothing else.
  - Services never call each other. Logic that several services need lives in a domain service or in a collaborator class in the area folder (e.g. `CustomerRegistry`, `BookingNotifier`, `ReferenceDataLoader`, `StaffBookingWarnings`). A collaborator is never injected into a controller.
  - **DTOs** (request and response records) sit in the area's `Dtos/` folder, one per file, never in a service's file.
  - **Ports** are interfaces in `Application/Abstractions/`, one per file, and the records they use have their own files there too. Application contains no implementation of a port: `SystemClock` and the Firestore, Stripe, SMTP and Turnstile implementations all live in Infrastructure.
  - Booking request validation is described in §7.1.
- **Infrastructure (Firestore):**
  - `Firestore/Repositories/Firestore<Aggregate>Repository.cs`: exactly one repository per aggregate, implementing that aggregate's interface.
  - `Firestore/Documents/<Aggregate>Document.cs`: one document class per aggregate, plus one per embedded type, each in its own file. Each document holds its own `From(aggregate)` and `ToDomain()` mapping.
  - A shared helper for versioned reads and writes is allowed as an internal collaborator of the repositories. It is never a repository itself, and nothing outside `Firestore/` uses it.
  - The boat lock (`FirestoreBoatScheduleLock`), the booking reference counter and the collection names each have their own file.
- **APIs:**
  - Each API has exactly the controllers listed in §8, one per resource, each in its own file in `Controllers/`. A nested resource with its own noun (`/api/boats/{id}/unavailabilities`, `/api/boats/{slug}/quote`) gets its own controller.
  - **Controllers are thin.** A controller binds the request, calls one method of its application service and maps the result to an HTTP response. Its only constructor parameters are that one service and, optionally, an `ILogger<T>`. It has no repositories, ports or domain logic.
  - Authentication (the Access JWT, the jobs endpoint's OIDC token) and the proxy secret are checked in middleware or filters, never in controller bodies.
- **Tests:**
  - Each test class covers one production class or use case. It is named `<Class>Tests` and sits in the folder matching that class, e.g. `Application.Tests/Bookings/Validation/OnlineBookingValidationStrategyTests.cs`.
  - Fakes are one per file in the test project's `Fakes/` folder.
- **Enforcement:**
  - **Analyzers:** `Directory.Build.props` references `StyleCop.Analyzers` for every project.
    - `.editorconfig` sets **SA1402** (one type per file) and **SA1649** (file name matches the type) to `error`, and every other StyleCop rule to `none`.
    - `stylecop.json` sets `maintainabilityRules.topLevelTypes` to `class`, `interface`, `struct`, `enum` and `delegate`, so records, enums and interfaces all count.
    - With `TreatWarningsAsErrors` on, a violation fails the build.
  - **Architecture tests:** `BergerHarbour.ArchitectureTests` checks the structural rules by plain reflection over the assemblies (§15).

## 8. API

All routes are under `/api`. JSON uses camelCase, dates are `yyyy-MM-dd`, times are `HH:mm`, mobile numbers are E.164, and money is a decimal number in AUD. Validation failures return `400` with ProblemDetails and per-field errors. Overlaps return `409`.

**Controllers.** Each API has exactly these controllers. Each one is thin and paired with one application service (§7.9).

| API | Controller | Routes | Application service |
|---|---|---|---|
| public | `BoatsController` | `GET /api/boats`, `GET /api/boats/{slug}` | `BoatCatalogueService` |
| public | `BoatQuotesController` | `POST /api/boats/{slug}/quote` | `OnlineQuoteService` |
| public | `AvailabilityController` | `GET /api/availability`, `GET /api/availability/period` | `AvailabilityService` |
| public, admin | `AddonAvailabilityController` | `GET /api/addons/{addonId}/availability` | `AddonAvailabilityService` |
| public | `BookingDraftsController` | `/api/booking-drafts` | `BookingDraftService` |
| public | `BookingsController` | `POST /api/bookings`, `…/{bookingId}/release`, `…/{bookingId}/status` | `OnlineBookingService` |
| public | `PaymentsController` | `/api/payments/{token}` | `PaymentLinkService` |
| public | `StripeWebhookController` | `POST /api/stripe/webhook` | `StripeWebhookService` |
| admin | `BookingsController` | `/api/bookings`, incl. search, quote, payments and send-payment-link | `StaffBookingService` |
| admin | `BoatsController` | `/api/boats` | `BoatService` |
| admin | `BoatUnavailabilitiesController` | `/api/boats/{id}/unavailabilities` | `BoatUnavailabilityService` |
| admin | `CustomersController` | `/api/customers` | `CustomerService` |
| admin | `BusinessSettingsController` | `/api/settings/business` | `BusinessSettingsService` |
| admin | `SeasonsController` | `/api/settings/seasons` | `SeasonService` |
| admin | `BlockedPeriodsController` | `/api/settings/blocked-periods` | `BlockedPeriodService` |
| admin | `AddonDefinitionsController` | `/api/settings/addons` | `AddonDefinitionService` |
| admin | `EmailTemplatesController` | `/api/settings/email-templates` | `EmailTemplateService` |
| admin | `JobsController` | `POST /api/jobs/process-notifications` | `NotificationJobService` |

`AddonAvailabilityController` exists in both APIs, and both use the same `AddonAvailabilityService`. Only the admin one accepts `excludeBookingId`.

### 8.1 public-api (customer-facing, no login)
| Method & route | Purpose |
|---|---|
| `GET /api/boats` | Every **active `House`** boat, ordered by berth desc, then name: `{ boats: [{ slug, name, imageUrl, berth, hasSpa, isPetFriendly }], settings }`, where `settings` holds the booking settings (minimum lead time, bookings open until, contact phone/email, hire terms URL). Also the warm-up call. |
| `GET /api/boats/{slug}` | Customer-safe data for one boat, active or not: name, imageUrl, berth, hasSpa, isPetFriendly, adultBeds, kidsBeds, beddingDescription, securityBond, effective depositAmount, isActive, allowed add-ons in admin-defined order (id, name, description, priceOnRequest, quantityApplies, prices by period type, hasCapacity, capacityUnitLabel). Used for the requested boat on load and for the selected boat in later steps. `404` for an unknown slug or a non-House boat. |
| `GET /api/availability?from=&to=` | Fleet availability for every active House boat. Returns only **ranges**, never booking or customer data: `{ enquireOnly: [{firstNight, lastNight}], boats: [{ slug, unavailable: [{start, end}] }] }`. `unavailable` = the boat's blocking bookings (as `[start,end)`) and unavailabilities. `enquireOnly` = blocked periods. Max span 15 months. |
| `GET /api/availability/period?periodType=&startDate=` | For one stay: `{ boats: [{ slug, available, hirePrice }] }` for every active House boat. `available` = no blocking booking or unavailability overlaps the stay; `hirePrice` comes from `PricingService` (§7.4). `400` if the stay fails the period shape, lead time, open-until or blocked-period rules (§7.7 rules 2–5). Returns no booking or customer data. |
| `GET /api/addons/{addonId}/availability?startDate=&endDate=` | Returns `{ remaining }` for a capacity-limited add-on across all boats (§7.5), and nothing else. `404` if the add-on has no capacity. |
| `POST /api/booking-drafts` | Body: `{ slug, periodType, startDate, numberOfAdults, numberOfChildren, firstName, lastName, email, mobile, turnstileToken }`. Verifies Turnstile, validates the details and creates a `BookingDraft`. Returns `{ draftId }`. Never creates a Customer. |
| `GET /api/booking-drafts/{draftId}` | Used when the booking app resumes saved progress (§11.2). Returns only the personal details `{ firstName, lastName, email, mobile }`. `404` if the draft is unknown, converted or deleted. |
| `PUT /api/booking-drafts/{draftId}` | Body: `{ slug, periodType, startDate, numberOfAdults, numberOfChildren, firstName, lastName, email, mobile, addons[{addonId, quantity}], lastStepReached }`. Updates the draft (including its boat) and `LastActivityAt`. `404` if the draft is unknown, converted or deleted. |
| `POST /api/boats/{slug}/quote` | `{ periodType, startDate, numberOfAdults, numberOfChildren, addons[] }` → hire price, season name, add-on prices, amount due at checkout, payment schedule with due dates, rooming warning text if applicable. Runs the online validation rules (except the draft, terms and acceptance) and returns their errors. |
| `POST /api/bookings` | Body: `{ draftId, slug, periodType, startDate, numberOfAdults, numberOfChildren, firstName, lastName, email, mobile, addons[{addonId, quantity}], roomingWarningAccepted, roomingWarningText, termsAccepted }`. First releases any hold from the same draft that is still `PendingPayment` (as `…/release` does), so a customer never collides with their own hold. Then validates, creates a `PendingPayment` hold (atomic) holding a `CustomerDetails` snapshot (**no Customer is created and no reference is assigned yet**), updates the draft, creates a Stripe Embedded Checkout session, and returns `{ bookingId, clientSecret, checkoutExpiresAt }`. The session's `expires_at` (`checkoutExpiresAt`) is now + `max(Booking__PendingHoldDuration, 30 min)`, and `HoldExpiresAt` is that + `Booking__PendingHoldGrace` (§10.2). Checkout therefore always closes **before** the hold lapses, so an Expired hold can never be paid online. |
| `POST /api/bookings/{bookingId}/release` | Body: `{ draftId }`. Used by **Back** on the Payment step. If the booking is still `PendingPayment` and came from that draft, it expires the booking's Stripe Checkout session and sets the booking to `Expired`, freeing the dates. Idempotent: a booking that is already `Expired` returns success. `409` if the booking is already Active (the payment completed). `404` if the draft doesn't match. |
| `GET /api/bookings/{bookingId}/status?session_id=` | Used by the completion screen. Returns the status, the reference once assigned, and a basic summary, only if `session_id` matches the booking. |
| `GET /api/payments/{token}` | Pay page: booking reference, boat, dates, total, paid, owing, amount due now. |
| `POST /api/payments/{token}/checkout` | Creates an Embedded Checkout session for the amount due now and returns `clientSecret`. Returns 400 if nothing is owing. |
| `POST /api/stripe/webhook` | Stripe-signature verified. Exempt from the proxy secret. See §8.3. |

**Abuse protection:**
- Turnstile is required on `POST /api/booking-drafts`. `GET` and `PUT /api/booking-drafts/{draftId}` and `POST /api/bookings` require a valid, unconverted draft id, and `POST /api/bookings/{bookingId}/release` requires the booking's own draft id.
- **Rate limiting** (`public-api` only): a middleware allows **60 requests per client IP per minute** on `/api/*` (sliding window, `System.Threading.RateLimiting`, in memory, idle IP buckets dropped by the framework). The IP is the forwarded `CF-Connecting-IP`. The Stripe webhook is exempt. It runs straight after the proxy-secret check and before any controller or Firestore access, so requests without the secret never use permits. Over the limit it returns `429` with `Retry-After` and a ProblemDetails title ("Too many requests. Please wait a minute and try again.") that the booking app shows as-is. `RATE_LIMIT_PER_MINUTE` overrides the limit (`0` disables; the default is 60 everywhere except `Local`, where it is off). Cloudflare's WAF rate limiting is not used (it needs the domain on Cloudflare and a paid plan). The limit is best-effort: it relies on `public-api` running one instance (§5). It is sized for a customer comparing boats on the "Dates & boat" step, which also caches its availability and price responses (§11.2) so browsing back and forth does not repeat requests.
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
| Bookings | `GET /api/bookings?from=&to=&includeInactive=` (timeline data: bookings incl. customer name (from the Customer or the `CustomerDetails` snapshot), status, isStandby, paymentStatus, reference (empty for holds); unavailabilities; blocked periods. The timeline's boat rows come from `GET /api/boats`, not from this call) · `GET /api/bookings/search?q=` (name, email or reference, including `CustomerDetails` snapshots) · `GET /api/bookings/{id}` (includes the customer's address, comments, `frequentFloater` (§7.8), `holdExpiresAt` for a hold, and the checkout amount for an Expired booking) · `POST /api/bookings` · `PUT /api/bookings/{id}` (boat, dates, period type, adults, children, check-in/check-out times, standby, booking comments, customer comments, hire price, status Active/Cancelled, customer contact details incl. secondary mobile and address, and the full `addonLines` list; includes `version`; rejected for `PendingPayment` and `Expired` bookings, §7.3) · `POST /api/bookings/quote` (body: `{ boatId, periodType, startDate, addonIds[] }` → `{ hirePrice, addonPrices: [{ addonId, unitPrice }] }`, where either price is `null` when staff must enter it; used to pre-fill the hire price and new add-on lines and for "Recalculate from rates"; saves nothing) · `POST /api/bookings/{id}/payments` (manual payment: amount, date, note, `version`. On an Active booking it records the payment. On an Expired booking without payments it is staff confirmation (§7.3). Rejected for every other booking) · `POST /api/bookings/{id}/send-payment-link` (Active non-stand-by bookings only; sends email #4 immediately for the amount due now) · `GET /api/addons/{addonId}/availability?startDate=&endDate=&excludeBookingId=` (remaining capacity) |
| Fleet | `GET /api/boats` (every boat, active or not, with `isActive`, ordered by berth desc, then name; also used for the boat rows of the Bookings timeline (§12.1)) · `GET /api/boats/{id}` · `POST /api/boats` · `PUT /api/boats/{id}` (all editable fields incl. adult beds, kids beds, deposit, image URL, spa, pet friendly, rates, allowed add-ons, active flag) · `GET /api/boats/{id}/unavailabilities` · `POST /api/boats/{id}/unavailabilities` · `PUT /api/boats/{id}/unavailabilities/{uid}` · `DELETE /api/boats/{id}/unavailabilities/{uid}` |
| Customers | `GET /api/customers?q=` · `GET /api/customers/by-email?email=` (used by the booking dialog to pre-fill contact details, address and customer comments; includes `frequentFloater` anchored at now, §7.8) · `PUT /api/customers/{id}/comments` (body: `{ comments, version }`) · `GET /api/customers/{id}/bookings` (all statuses, ordered by StartDate, newest first) |
| Settings | `GET/PUT /api/settings/business` (incl. bank account name, BSB and account number) · CRUD `/api/settings/seasons` · CRUD `/api/settings/blocked-periods` · CRUD `/api/settings/addons` (incl. capacity and capacity unit label, which is required when a capacity is set; delete = deactivate) · `PUT /api/settings/addons/order` (body: the full ordered list of add-on ids; the server rewrites `SortOrder`) · `GET /api/settings/email-templates` · `PUT /api/settings/email-templates/{key}` · `POST /api/settings/email-templates/{key}/test` (sends a sample to the staff alert address) |
| Jobs | `POST /api/jobs/process-notifications`. **Not** behind Access. It validates a Google OIDC token: issuer Google, audience = service URL, email = the Cloud Scheduler service account. |

**Add-on lines on staff bookings:** `POST /api/bookings` and `PUT /api/bookings/{id}` carry the booking's complete `addonLines: [{ id?, addonId, quantity, unitPrice }]`. A line without an `id` is new. Existing lines with an `id` are updated. Existing lines left out are deleted. There are no separate add-on line endpoints; lines change only when the booking is saved.

**Customer fields on staff bookings:** `POST` and `PUT` carry the contact details (first name, last name, email, mobile, secondary mobile), `address` (`{ line1, line2, locality, state, postcode, country }`, or `null` for no address) and `customerComments`. They are saved to the booking's Customer in the same request. Only `PendingPayment` and `Expired` bookings lack a Customer, and those cannot be updated (§7.3).

Staff-created bookings (`POST /api/bookings`):
- Email, first name and last name are required. The email and any mobile numbers must be valid (§7.1), on create and on update. The customer is matched or created by email.
- Status is `Active` immediately. `CreatedBy = SystemUser`. The reference and `ActivatedAt` are set at creation.
- Validated by `InternalBookingValidationStrategy` plus the availability invariant.
- Responses include non-blocking `warnings[]` from `StaffBookingWarnings` (§7.1): guests above berth, guests above beds, add-on capacity exceeded, custom time clashing with a same-day changeover.
- If it is not a stand-by, email #1 is sent.
- If it becomes Active and overlaps stand-bys, they are superseded.

### 8.3 Stripe
- `StripeWebhookController` passes the raw body and the `Stripe-Signature` header to `StripeWebhookService`. The service verifies and parses the event through the `IPaymentGateway` port, so Stripe types stay in Infrastructure.
- Only synchronous methods (card + wallets) are allowed, so `checkout.session.completed` means paid.
- Checkout session `metadata`: `bookingId`, `purpose` (`Deposit` | `Payment`).
- **`checkout.session.completed`:**
  - Record a `Payment`. This is idempotent on PaymentIntent id.
  - If `purpose == Deposit` and the booking is `PendingPayment` or `Expired`, activate it inside `IBoatScheduleLock` when its dates are still free (§7.3: reference, `ActivatedAt`, Customer from `CustomerDetails`, `CustomerId`, draft converted, stand-bys superseded), then send #1 to the customer and #2 to staff. A deposit normally completes while the hold is still `PendingPayment`. It only finds an `Expired` booking when the webhook is delayed past the hold, because Checkout closes before the hold lapses (§8.1).
  - **Paid-but-expired:** if activation is impossible (the hold had expired or been released, and the dates were taken), keep the payment recorded, leave the booking `Expired` but assign it a reference so staff can find it (no Customer is created; staff use the `CustomerDetails` snapshot), and log an **error**. The log-based alert notifies staff, who refund by hand.
  - If the booking is already `Active` because staff confirmed it while the webhook was delayed, keep the payment recorded, leave the booking as it is, send nothing, and log an **error**: a second deposit that staff refund by hand.
  - If `purpose == Payment`, send #5.
- **`checkout.session.expired`:** if the booking is still `PendingPayment` with that session id, set it to `Expired`. A booking already released from the Payment step, or already expired at `HoldExpiresAt`, is left as it is.
- The webhook returns 2xx only after persistence succeeds, so Stripe retries on failure.
- Apple Pay / Google Pay should work inside the WordPress modal (iframe has `allow="payment"`; register the payment method domains in Stripe). Card payment must work regardless.
- Set the Checkout session `locale` to `"auto"`. Stripe only accepts its own fixed list (e.g. `en`, `en-GB`)

## 9. Notifications

**Provider:** Gmail SMTP (`smtp.gmail.com:587`, STARTTLS) using MailKit. The sender is the business mailbox `info@bergerhouseboats.com.au` (a Google Workspace user), authenticated with a Google app password. It is the SMTP login, the From address (display name "Berger Houseboats") and the Reply-To. Customer replies and bounces arrive in that mailbox, and every email sent is kept in its Sent folder; the system does no delivery or open tracking. Workspace allows about 2,000 emails a day through SMTP, far above this system's volume. Sending is behind the `IEmailSender` port; local development uses a fake that writes emails to the console or a folder.

| # | Key | To | Trigger |
|---|---|---|---|
| 1 | `BookingConfirmed` | Customer | Online: deposit paid (booking becomes Active), or staff confirmation of an expired hold (§7.3). Staff: non-stand-by booking created. Includes the deposit receipt, payment schedule, security bond notice, requested add-ons "subject to confirmation", and the hire terms link. If a milestone is **already due at activation**, it states that the balance of `{{AmountDue}}` is now due and includes `{{PaymentLink}}`. |
| 2 | `StaffNewOnlineBooking` | Staff alert address | Online booking becomes Active through a Stripe deposit payment. Not sent on staff confirmation. The default template includes `{{FrequentFloaterNotice}}` so staff see repeat customers (§7.8). |
| 4 | `PaymentDue` | Customer | For each Balance / First / Final milestone with a shortfall: once at `due − PaymentReminderBeforeDue` (7d) and once at `due`. Each is skipped if its trigger instant was at or before the booking's `ActivatedAt`. Also sent immediately from "Send payment link". Contains `{{PaymentLink}}`. |
| 5 | `PaymentReceived` | Customer | Any Stripe payment other than the deposit checkout, and any manual payment except the one that confirms an expired hold (#1 covers it) |
| 6 | `PreHireInstructions` | Customer | `check-in − PreHireInstructionsBeforeHire` (7d). This is the existing "canned message". If the booking is created inside that window, it is sent at the next job run. |
| 7 | `StaffBalanceOverdue` | Staff alert address | Once per milestone, when `now > due` and a shortfall remains. **Never sent for a milestone that was already due at activation.** |
| 8 | `StaffStandbySuperseded` | Staff alert address | A stand-by is superseded |
| 10 | `StaffAbandonedBooking` | Staff alert address | A booking draft is abandoned (§7.3 BookingDraft rules). Includes the customer's name, email and mobile, the boat, dates, adults/children, requested add-ons and the last step reached, so staff can follow up. |

Numbers 3 (add-on confirmed/declined) and 9 (cancellation) are **deliberately not implemented** (decision 10).

**Rules:**
- No email of any kind for stand-by bookings, or for bookings that are PendingPayment, Cancelled, Expired or Superseded (except #8 to staff).
- **Payment emails after a late booking:** #4 and #7 notifications whose trigger instant was at or before the booking's `ActivatedAt` are never sent (decision 23). Example: a booking made 35 days before check-in gets no 7-day reminder but does get the "due" email 30 days before check-in. A booking made 10 days before check-in gets no #4 or #7 for the balance; its #1 includes the payment link instead. A hold that staff confirm days after it was created gets no catch-up reminders for the days in between.
- **Idempotency:** before sending, check `SentNotifications` for a key like `PaymentDue:Balance:Reminder`, `PaymentDue:Balance:Due`, `Overdue:Final`, `PreHire`. Record it after a successful send. Manual "Send payment link" sends are not deduplicated. Abandoned-draft emails are deduplicated with `BookingDraft.AbandonedNotifiedAt`.
- **Templates:**
  - Editable in Settings: subject + HTML body. Defaults are seeded at setup with sensible Australian-English wording.
  - Placeholders: `{{CustomerName}} {{CustomerFirstName}} {{CustomerEmail}} {{CustomerMobile}} {{BookingReference}} {{BoatName}} {{CheckInDate}} {{CheckOutDate}} {{CheckInTime}} {{CheckOutTime}} {{NumberOfGuests}} {{NumberOfAdults}} {{NumberOfChildren}} {{HirePrice}} {{TotalPrice}} {{AmountPaid}} {{AmountOwing}} {{AmountDue}} {{DueDate}} {{PaymentLink}} {{SecurityBond}} {{RequestedAddons}} {{PaymentSchedule}} {{HireTermsUrl}} {{ContactPhone}} {{ContactEmail}} {{LastStepReached}} {{FrequentFloaterNotice}} {{AccountName}} {{BSB}} {{AccountNumber}}`. Placeholders that do not apply to a message (e.g. booking fields in #10) render empty.
  - `{{CustomerName}}` is the first and last name. `{{CustomerFirstName}}` is the first name only (e.g. for "Dear Jane").
  - `{{FrequentFloaterNotice}}` renders e.g. "⭐ Frequent Floater: Jane Smith has 1 other booking within a year of this one." for a Frequent Floater booking (§7.8), and empty otherwise.
  - `{{AccountName}}`, `{{BSB}}` (shown `123-456`) and `{{AccountNumber}}` render the bank details from Settings, or empty when they are not set. The default templates use payment links. Staff may edit templates to ask for a bank transfer instead or as well, quoting `{{BookingReference}}` as the transfer reference.
  - Customer comments and booking comments are never available as placeholders.
  - `{{CheckInTime}}` shows the standard window "11am – 2pm" and `{{CheckOutTime}}` shows "8am", each with the AEST/NSW or AEDT/NSW label for that date. If staff changed a booking's time from its default, the custom time is shown instead.
  - `{{RequestedAddons}}` lists lines in the admin-defined add-on order.
  - Values are HTML-encoded. Dates are `dd/MM/yyyy`. Money is `$1,234.00`.
- **Payment link:** `https://bookings.bergerhouseboats.com.au/pay/?token=<token>`. The token is 32 random bytes, base64url. Only its SHA-256 hash is stored. It is generated with the booking and is stable for the booking's life.

**Scheduled job** `process-notifications`:
- Cloud Scheduler runs it **hourly** (OIDC-authenticated HTTP call to admin-api).
- It is idempotent and safe to run at any frequency. Each run:
  1. Expires `PendingPayment` holds past `HoldExpiresAt` that nothing else has expired yet (§7.3).
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
| `Booking__PendingHoldDuration` | `00:30:00` (Stripe minimum is 30 min; the Checkout session lasts `max(value, 30min)` and the hold that + `Booking__PendingHoldGrace`) |
| `Booking__PendingHoldGrace` | `00:10:00` (lets a payment started just before Checkout closes complete while the dates are still held) |
| `Booking__AbandonedDraftAfter` | `02:00:00` (inactivity before a draft counts as abandoned; longer than the maximum hold) |
| `Booking__DraftRetention` | `30.00:00:00` |

### 10.3 Environments, secrets and other config
`ASPNETCORE_ENVIRONMENT` selects the environment: `Local` (a developer machine against emulators), `Development` (the deployed dev project) or `Production`. Non-secret settings that differ per environment live in `appsettings.{Environment}.json`, committed with the code: the booking base URL, the allowed CORS origin, the email sender, sender name and `EMAIL_SUBJECT_PREFIX` (`[DEV] ` in dev, empty in prod). `PUBLIC_BOOKING_BASE_URL` and `CORS_ALLOWED_ORIGIN` have no defaults in code and are required at startup. `RATE_LIMIT_PER_MINUTE` (public-api only, optional) overrides the per-IP limit (§8). Infrastructure IDs that only exist once a project is provisioned (Firestore project, Access team domain and AUD, Scheduler service account and audience) are passed as env vars by the deploy. Only `Local` relaxes any security check (§14); the services refuse to start if `DEV_ADMIN_EMAIL` or `DEV_JOBS_BYPASS` is set in any other environment.

Secrets are stored in each project's Secret Manager and injected as env vars:
- Stripe secret key and webhook signing secret
- `SMTP_PASSWORD`: the Google app password for the sender mailbox. `EMAIL_SENDER` (`info@bergerhouseboats.com.au`) and `EMAIL_SENDER_NAME` are plain settings. In `Smtp` mode the services refuse to start if the sender or password is missing; `EMAIL_MODE=File` (the development default) writes emails to a folder instead
- Turnstile secret
- `EDGE_PROXY_SECRET`
- `CF_ACCESS_TEAM_DOMAIN`, `CF_ACCESS_AUD`, `ADMIN_ALLOWED_EMAILS` (comma-separated)
- `SCHEDULER_SERVICE_ACCOUNT_EMAIL`

The frontends need the Stripe publishable key and the Turnstile site key at build time.

## 11. Customer booking app (`bookings.bergerhouseboats.com.au`)

### 11.1 Embedding on WordPress
Each boat page gets this HTML block (deliver it as `/wordpress/book-now-block.html` with the slug as the only thing to change):
- A `<button class="bh-book-now" data-boat="pacific-blue">Book Now</button>`.
- An inline script, guarded so it only initialises once per page. On click:
  - **Mobile** (`matchMedia('(max-width: 768px), (pointer: coarse)')`): open `https://bookings.bergerhouseboats.com.au/?boat=<slug>` in a new tab.
  - **Desktop:** create a native `<dialog class="bh-modal">` containing a visible close button and an `<iframe src=".../?boat=<slug>" allow="payment" title="Book <boat>">`, add it to the page and open it with `showModal()`. Each click creates a fresh dialog.
    - `showModal()` puts the dialog in the top layer and makes the rest of the page inert, so there is no hand-written focus trap and no z-index setting.
    - Esc closes it through the dialog's native `cancel` event. There is no keydown handler.
    - The backdrop is the dialog's `::backdrop`; there is no overlay element. Clicking the backdrop closes the dialog: a click whose `e.target` is the dialog itself. `closedby="any"` is not used, because Safari doesn't support it.
    - The page scroll is locked by CSS alone: `html:has(dialog.bh-modal[open]) { overflow: hidden; }`.
    - However it closes (close button, Esc, backdrop or the close message), focus returns to the Book Now button that opened it, and the dialog is removed from the page so the iframe stops running.
  - It listens for `postMessage` `{ type: 'bh-booking:close' }`, accepted **only** from origin `https://bookings.bergerhouseboats.com.au`, and closes the dialog.
- The booking app sends:
  - `Content-Security-Policy: frame-ancestors https://bergerhouseboats.com.au https://www.bergerhouseboats.com.au` (configured in Pages `_headers`)
  - a CSP compatible with Stripe.js and Turnstile, whose `img-src` also allows `https://bergerhouseboats.com.au` and `https://www.bergerhouseboats.com.au` (boat images).

### 11.2 Flow
The app is a single page with steps, in this order: **Dates & boat → Your details → Add-ons → Summary → Payment → Complete**. The progress is visible. The layout is responsive and works at 360px width (16px gutters, no horizontal scroll) and in the ~900×700 modal. Once a booking draft exists, moving forward through each step updates it (`PUT /api/booking-drafts/{draftId}`). Every button that sends a request (e.g. Continue on Your details, Pay, Back on Payment) follows the busy state in §6.1.

**Saved progress** (decision 37):
- **What is kept.** The step state is written to `localStorage` as it changes: period type and dates, adults and children, filters, the requested and selected boat, "Show {Boat}'s availability only", add-on requests, the rooming warning acceptance, the draft id, the furthest step reached and, on the Payment step, the hold's booking id.
- **What is never kept.** The first name, last name, email and mobile are never written to browser storage; they are read back from the draft. The terms tick isn't kept either, so the customer ticks it again on the Summary. Details typed on Your details before its first Continue are lost on a refresh, because no draft exists yet.
- **Resuming.** On load, saved progress for the same requested boat (the same `?boat=`, or none for both) is resumed:
  1. If it holds a booking id from the Payment step, the app releases that hold (`POST /api/bookings/{bookingId}/release`). A `409` (the payment completed meanwhile) goes to Complete.
  2. If it holds a draft id, the app reads the personal details with `GET /api/booking-drafts/{draftId}` and fills Your details.
  3. When the saved progress holds a period, whichever step it resumes on, the app re-checks the period with `GET /api/availability/period`.
     - If the period now fails the online rules (e.g. it has fallen inside the lead time), the period is cleared and the selected boat is kept. The app opens Dates & boat with "Your saved dates can no longer be booked online. Please choose new dates."
     - If the selected boat is no longer free for the period, the period is kept and the boat is deselected. The app opens Dates & boat with "Your saved booking has changed: {Boat} is no longer available for those dates."
  4. Otherwise the app opens the furthest step reached, **at most the Summary** (a customer who was on Payment lands on the Summary).
- **Discarding.** Saved progress is deleted, and the app starts fresh, when:
  - the booking reaches Complete;
  - the draft returns `404` (converted, or deleted after `Booking__DraftRetention`);
  - `?boat=` names a different boat from the saved progress;
  - browser storage is unavailable or unreadable (no error is shown).
- Pay stays the authoritative check: `POST /api/bookings` re-runs every rule and the availability check, however the customer got to the Summary.

1. **Load:** read the optional `?boat=`. Show a skeleton while `GET /api/boats` and, when a boat is requested, `GET /api/boats/{slug}` load. Then resume any saved progress (above).
   - Unknown slug → friendly error.
   - No `?boat=` → there is no requested boat and no boat is selected.
   - Active requested boat → it becomes the selected boat, and "Show {Boat}'s availability only" starts **ticked**.
   - Inactive requested boat → its card is greyed (see Boat cards below) and no boat is selected.
2. **Dates & boat:** the customer chooses the period, guests and filters, and the boat. On desktop and in the modal the controls are in a left column and the boat cards in a right column. At ≤ 768px everything stacks: the left column's controls in order, then the cards.

   **Left column, top to bottom:** period radio buttons · check-in/check-out times line · calendar and legend · "Selected: …" line · "Show {Boat}'s availability only" checkbox · filters (Adults, Children, Spa, Pet friendly) · Base hire rate · **Continue**.

   **Calendar:**
   - Radio buttons: **Mid-week (Mon–Fri)**, **Weekend (Fri–Mon)**, **Week (Mon–Mon or Fri–Fri)**.
   - A classic month calendar with previous/next navigation and a legend. Weeks start on Monday, and the grid shows only the weeks that contain days of the month.
   - **Adjacent-month days:** the cells before the 1st and after the month's last day show the previous and next month's days in muted grey.
     - They are not clickable in any mode and carry no availability styling; the customer uses Prev/Next to reach them.
     - When the selected period extends into them, they show the selection in a muted version of the selection colour.
     - Part of a selection can still be off-screen. For example, Weekend Fri 26/02/2027 → Mon 01/03/2027 viewed in March 2027, which starts on a Monday, has no leading cells and shows only the check-out day.
   - Above the calendar, show **"Check-in 11am – 2pm · Check-out from 8am (AEDT/NSW)"**, with the time zone label (§3) for the selected dates, or for the visible month when nothing is selected. When the check-in and check-out dates fall in different zones (the stay crosses a daylight-saving change), each part carries its own label: "Check-in 11am – 2pm (AEST/NSW) · Check-out from 8am (AEDT/NSW)".
   - Availability for the visible months is fetched with `GET /api/availability`, which covers every active House boat in one call. Filters, the checkbox and card clicks are applied in the browser and send no requests.
   - **Calendar boats:** the selected boat when "Show {Boat}'s availability only" is ticked; otherwise all candidate boats.
   - **Clickable days per mode:**
     - Mid-week: Mon–Fri clickable; Sat and Sun disabled.
     - Weekend: Fri, Sat, Sun, Mon clickable; Tue–Thu disabled.
     - Week: only Mondays and Fridays clickable.
   - **Click mapping:** the period of the selected type that contains the day.
     - Mid-week: Mon–Fri of that week. Clicking Fri selects the Mon→Fri ending that day. Clicking a different day in the same period doesn't change the selection.
     - Weekend: the Fri→Mon containing it. Clicking Mon selects the weekend ending that day.
     - Week: Mon→next Mon or Fri→next Fri, starting on the clicked day.
   - The whole selected period is highlighted. The start shows a check-in marker and the end a check-out marker. **Every day inside the selection uses the active selection colour with normal (not grey) day numbers, even days that are not clickable in the current mode** (e.g. Tue–Thu inside a Week selection). Adjacent-month cells are the exception and use the muted selection colour (see above).
   - The **inner days** of the selection (every day strictly between check-in and check-out, e.g. Tue–Thu of a Mon→Fri stay) use a darker brand tint (`--c-brand-tint-strong`) than the general light tint, so the selected period stands out clearly against the disabled and non-selectable days around it. The difference must be obvious at a glance, not a subtle shade. The check-in and check-out days keep their solid brand colour and markers.
   - Below the calendar, a line states the selection without repeating the times, e.g. **"Selected: check-in Fri 16/10/2026, check-out Fri 23/10/2026."** (weekday + `dd/MM/yyyy`). The same text is announced in the ARIA live region.
   - **Greying is per period, not per day:** a day is disabled in a mode when the period it maps to is unavailable or invalid. For example, a Monday on which a weekend booking checks out is still clickable in Mid-week mode.
   - A period is unavailable if:
     - no calendar boat is free for it (every calendar boat has an `unavailable` range overlapping it, or there are no calendar boats);
     - it starts before today + lead time;
     - it ends after `BookingsOpenUntil`.
   - **Blocked periods:** days inside `enquireOnly` ranges are greyed out with a distinct style. If the period a click maps to overlaps a blocked period, show a pop-up instead of selecting: "Bookings over {period name} must be made by phone or email: {phone} / {email}." Example: Mid-week mode, click Tue 18/12 → Mon 17/12–Fri 21/12 overlaps Christmas → pop-up.
   - Selecting a period calls `GET /api/availability/period` for it, which gives each boat's availability and hire price for that stay.
   - Changing the period type clears the selected period. The selected boat is kept.

   **"Show {Boat}'s availability only" checkbox:**
   - `{Boat}` is the selected boat's name. Ticked, the calendar shows only that boat's availability. Unticked, a period is free when **any** candidate boat is free for it.
   - With no selected boat, the checkbox is unticked and disabled, and reads "Show the selected boat's availability only". When a boat becomes selected, the checkbox is enabled (still unticked).
   - Selecting a different boat card never changes the checkbox. While it is ticked, the calendar follows the newly selected boat.

   **Filters:**
   - **Adults** (`1…N`, default 1) and **Children (Under 12)** (`0…N − adults`, default 0), where N is the largest `Berth` among active House boats. Changing adults clamps children so the total never exceeds N.
   - **Spa** and **Pet friendly** checkboxes, unticked by default. Ticked, only boats with that feature are candidates.
   - Candidate boats (§3) are the active House boats with adults + children ≤ `Berth` and every ticked feature.

   **Boat cards:**
   - The cards are a single-select **radio group**. The whole card is clickable and keyboard-operable (arrow keys, Enter/Space) with visible focus, and a small tick-box indicator in its corner shows the selection. The selected boat's name is announced in the ARIA live region. Greyed cards cannot be selected.
   - Each card shows the boat's image (`imageUrl`, or a neutral placeholder when it is empty or fails to load), its **name**, "**{n} berth**" (e.g. "12 berth"), a **Spa** and/or **Pet friendly** badge when set, and, once a period is selected, the boat's **hire price** for that period.
   - **The requested boat is always the first card**, whatever its state. When it cannot be selected it is greyed, with one short grey reason underneath; the first that applies is shown:
     1. "Not available for online booking" (inactive)
     2. "Not available for selected dates"
     3. "Too small for {n} guests"
     4. "Doesn't match your filters" (Spa or Pet friendly)
   - Below it, a text separator **"Other boats available"** and the other candidate boats, ordered by `Berth` descending, then `Name` (without a requested boat, the separator is not shown). Before a period is selected, every candidate boat is shown. Once a period is selected, only the candidates free for it are shown; when there are none, the list says "No other boats are available for these dates.", or "No boats match your filters for these dates." when no candidate boat at all is free for the period.

   **Base hire rate:** labelled "Base hire rate" with the sub-label "Excludes add-ons". It shows the selected boat's hire price for the selected period (e.g. `$4,060.00`), or "Select dates and a boat" until both are chosen.

   **Keeping the selection valid:**
   - When a change to the period, guests, Spa or Pet friendly makes the selected boat ineligible (not free for the period, too small for the guests, or missing a ticked feature), it is deselected. If "Show {Boat}'s availability only" was ticked, it becomes unticked and disabled, and the calendar shows all candidate boats.
   - Changing a filter never clears the selected period, even when no candidate boat is free for it any more.

   **Continue** is enabled only when a period and a selected boat are chosen. The selected boat's details come from `GET /api/boats/{slug}`.
   - If `adults + children > AdultBeds + KidsBeds` of the selected boat, Continue first shows a modal **rooming warning** with the bedding description, e.g. "This boat has {AdultBeds} adult beds and {KidsBeds} children's beds ({BeddingDescription}). With {n} guests, some guests will need to share beds. Please make sure your group's sleeping arrangements suit this layout." It has an **Accept** button, which continues, and a **Cancel** button, which stays on this step. The booking cannot continue without accepting. The exact text shown is sent with the booking.
   - An acceptance holds while the selected boat and guest numbers stay the same. If either changes, the warning is shown again on the next Continue when it still applies.

   **Caching:** fleet availability per month, period results per (period type, start date) and boat details per slug are cached for the session (TanStack Query, never stale within the session), so browsing back and forth repeats no requests. The availability and period caches are cleared after a `409` at Payment and whenever this step is shown again after the Payment step was reached.
3. **Your details:**
   - First name, last name, email (§7.1) and mobile, all required, each validated when it loses focus (§6.1).
   - The mobile field (`MobileInput`) has a searchable **country code selector** defaulting to **Australia (+61)**. It only accepts a mobile number (§7.1), is validated with `libphonenumber-js` and is sent as E.164. Bookings may come from overseas.
   - The Turnstile widget.
   - A short note: "If you don't finish your booking, our team may contact you to help."
   - On Continue: `POST /api/booking-drafts` the first time (then keep `draftId` in the saved progress), or `PUT` if a draft already exists.
4. **Add-ons:**
   - Checkboxes for the selected boat's allowed add-ons, in the admin-defined order, with a quantity input where `QuantityApplies`.
   - Each shows a price for the selected period type, or "Price on request".
   - If the customer went back and selected a different boat, requested add-ons the new boat does not allow are removed, with the notice "Some add-ons you chose aren't offered on {Boat} and have been removed."
   - **Capacity-limited add-ons:** call `GET /api/addons/{addonId}/availability` for the selected dates **every time the Add-ons step is shown** (never cache it: going back to change the dates, or returning from the Summary, must show current figures). Show "{X} {CapacityUnitLabel} remaining" under the add-on (e.g. "6 car spots remaining"). The quantity input cannot go above X. When X is 0, the add-on is **not shown at all**.
   - Notice: "Add-ons are requests and will be confirmed by our staff. Confirmed add-ons are added to your balance. Add-on prices are subject to change during peak periods."
   - **Booking price** at the bottom of the step:
     - It shows the Base hire rate plus every ticked add-on that has a price (quantity × its price for the selected period type), so the customer sees the total before the Summary.
     - It is calculated in the browser from the prices already loaded, and updates as soon as add-ons are ticked or quantities change.
     - When any ticked add-on is price on request, "Excludes price-on-request add-ons" is shown underneath.
     - The Summary still shows the server quote.
5. **Summary:**
   - Server quote showing boat, a **Check-in** row and a **Check-out** row, each with its date, time and the time zone label (§3) for that date (e.g. "Check-in: Fri 16/10/2026, 11am – 2pm AEDT/NSW" and "Check-out: Fri 23/10/2026, from 8am AEDT/NSW"; across a daylight-saving change each row shows its own label), adults and children, season, hire price, requested add-ons (with price or "price on request"), **amount payable now** (the deposit), the balance schedule with due dates, and the security bond notice ("A ${bond} security bond applies — details will be provided").
   - Terms checkbox: "I accept the [Hire Terms and Procedures]" (link opens in a new tab).
   - Button: **Pay ${amount}**, enabled only once the terms are accepted.
6. **Payment:** `POST /api/bookings` → mount Stripe Embedded Checkout.
   - **Hold countdown:** an info tag above Checkout reads "Dates are guaranteed for the next {n} minutes", where `n` is the whole minutes left until `checkoutExpiresAt`, rounded up and updated as time passes. It says "the next 1 minute" in the last minute.
   - **When the time runs out** (`checkoutExpiresAt` passes), Checkout is replaced by "Your dates are no longer held. Go back to try again." with a button that returns to the Summary, the same as Back. Paying again creates a new hold.
   - **Back** returns to the Summary step, from which the customer can go back further and change anything. It calls `POST /api/bookings/{bookingId}/release`, which frees the hold, and unmounts Checkout. Continuing to Payment again creates a new hold and Checkout session. If the release returns `409` (the payment completed meanwhile), go to Complete instead.
   - On `409`, return to the Dates & boat step with "Sorry, those dates were just taken." The period, guests and filters are kept, availability is fetched again, and the boat (now unavailable) is deselected, so the customer can choose another boat that is free for the same dates.
   - On a capacity `400`, return to the Add-ons step with the message and refreshed remaining figures.
7. **Complete:** delete the saved progress. Poll `GET /api/bookings/{bookingId}/status` until `Active` (show "Confirming…" for up to ~30s, then say an email will follow). Show the reference and "A confirmation email has been sent to {email}". Show a **Close** button:
   - **Inside the WordPress iframe** it posts `bh-booking:close`.
   - **Outside an iframe** (a mobile tab or a direct visit) it calls `window.close()`. Browsers only let scripts close windows they opened, so if the page is still open a moment later, it goes to the requested boat's WordPress page (`https://bergerhouseboats.com.au/houseboats/<slug>/`), or to `https://bergerhouseboats.com.au/` when there is no requested boat.

**Pay page** `/pay/?token=`: shows the booking summary, total, paid, owing and amount due now, with a **Pay** button that mounts Embedded Checkout. If nothing is owing, show a "Nothing to pay" state.

Accessibility: keyboard-operable calendar (arrow keys, Enter) and boat cards (a radio group: arrow keys, Enter/Space), visible focus, ARIA labels and live region for the selected period and boat, contrast AA.

## 12. Admin app (`berger-admin.pages.dev`)

Behind Cloudflare Access. There is a top menu bar: **Bookings · Fleet · Customers · Settings**. The layout is desktop-first and usable on a tablet. All dates, times and timestamps follow the display format in §3. Boats are always listed by berth (largest first), then name. Saving, busy states and field validation follow §6.1: nothing is saved until the form's main button is pressed, buttons are disabled with progress while their request runs, and every field is validated when it loses focus.

### 12.1 Bookings
- **Full-screen timeline:** one row per boat (sticky boat-name column), one column per day, Prev/Next and Today buttons, weekend columns shaded. The rows come from `GET /api/boats` and the bars from `GET /api/bookings` for the displayed range (§8.2).
- **Loading:** the header controls, the day-column headers and the boat names stay visible while a range loads; only the cells wait for data.
  - While a range with no data loaded yet is fetched (first load, Prev/Next, Today, the Week/Month switch, Show inactive), the cell area shows a large `Spinner` instead of empty cells. On the very first load, before the boats arrive, the boat-name column shows the spinner too.
  - A range already loaded shows straight away and refreshes in the background, with no spinner.
  - The controls stay usable while loading. Only the most recently requested range is ever shown.
  - If loading fails, the cell area shows the error with a **Retry** button.
- **Inactive boats** keep their row, with the name muted, so their history stays visible. Their empty cells are not clickable (no pointer, no hover), but their bars still open the booking dialog.
- **Week | Month toggle** (a segmented control next to Today):
  - **Month view:** one column per day of the month, stretched to fill the width, like the week view. It only scrolls horizontally when the columns would be narrower than about 28px (e.g. on a tablet); it then keeps that minimum width. Prev/Next move one month; Today goes to the current month. Title e.g. "October 2026".
  - **Week view:** 7 columns, **Mon → Sun**, stretched to fill the width (no horizontal scroll). Prev/Next move one week; Today goes to the week containing today. Title e.g. "Mon 19/10/2026 – Sun 25/10/2026".
  - Switching Month → Week shows the week containing today if today is in the displayed month, otherwise the week containing the 1st of that month. Switching Week → Month shows the month containing the displayed week's Monday.
  - Month is the default. The last chosen view is remembered per browser (`localStorage`).
  - Everything else (boat rows, blocked-period band, weekend shading, Show inactive, clicking an empty cell, search) works the same in both views.
- **Bars:**
  - Bars are positioned by time of day: each day cell spans 24 hours, and a booking starts at its `CheckInTime` on the check-in day and ends at its `CheckOutTime` on the check-out day, linearly within the cell (e.g. 11am starts ≈46% into the cell, 8am ends ≈33% into it; a custom 11pm check-out ends close to the next day's border). Custom times are reflected. This applies in both views, so changeovers are visible.
  - Bars in a boat row are stacked into lanes by time, not by date: two bars share a lane only if one's check-out time is at or before the other's check-in time. A standard same-day changeover (8am out, 11am in) therefore stays in one lane, while custom times that clash (e.g. an 11pm check-out and an 11am check-in on the same day) put the later booking in a new lane. Bars never cover each other.
  - Bar label: customer name + reference ("Pending payment" for holds, which have no reference yet).
  - Styles: `Active` (solid, tinted by payment status), `PendingPayment` (dashed outline), stand-by (hatched).
  - Unavailabilities are grey blocks positioned with the standard times, from 11am on `FirstNight` to 8am on the day after `LastNight`, so they line up with booking bars. Blocked periods are a labelled column band across all rows, covering whole days.
  - "Show inactive" toggle reveals Cancelled, Expired and Superseded bookings (faded).
- **"+ New" button**, and clicking an empty cell of an active boat, opens the booking dialog for a new booking:
  - The cell pre-fills the boat. "+ New" leaves the boat empty and uses today as the day.
  - The period type and dates come from the day, with the booking app's click mapping (§11.2): **Mon–Thu → Mid-week**, Mon→Fri of that week; **Fri–Sun → Weekend**, the Fri→Mon containing the day. The start date can be earlier than the day clicked (e.g. clicking Thu 15/10/2026 gives Mon 12/10/2026 → Fri 16/10/2026).
  - The period is pre-filled even when it overlaps another booking or an unavailability; saving then reports the conflict (see Behaviour below).
  - This only sets the starting values. Week, LongWeekend and Custom are never chosen automatically. Afterwards, changing the start date keeps the period type and re-derives the end date, and choosing a period type re-derives the end date from the start date.
- Clicking a bar opens the same dialog in edit mode.
- **Search box:** name, email or reference → results list → opens the dialog.

**Booking dialog fields:**
- Boat
- Period type (Midweek/Weekend/Week/LongWeekend/Custom) — selecting one pre-fills the end date
- Start date, end date
- **Number of adults** (≥ 1) and **number of children** (≥ 0). Show a non-blocking warning if the total is above the boat's berth or above `AdultBeds + KidsBeds`.
- **Check-in time** (default 11am) and **check-out time** (default 8am). Staff-only. Show a non-blocking warning when a custom time clashes with a same-day changeover on that boat (this booking's check-in is earlier than the previous booking's check-out on the same day, or its check-out is later than the next booking's check-in).
- **Customer:**
  - **Email** (required, §7.1). On blur it is validated. When it is valid, it looks up an existing customer; when a customer is found, the names, mobiles, address and customer comments are **always** filled from that customer, replacing what was typed. When it is invalid, the error shows and no lookup is made. If the email is changed to another existing customer, those fields reload from that customer. When no customer is found, the fields keep what was typed.
  - **First name** and **Last name** (required).
  - **Mobile** (optional) and **Secondary mobile** (optional, empty by default). Both use the same `MobileInput` (country code selector) as the booking app and only accept mobile numbers (§7.1).
  - **Address**, below Secondary mobile: **Address line 1**, **Address line 2**, **City/Locality**, **State**, **Postcode** and **Country**. None is required (§7.3).
    - **Country** is a dropdown of ISO 3166-1 alpha-2 countries (the same hard-coded list the server validates against, with names from `Intl.DisplayNames`), defaulting to Australia, or the customer's stored address country.
    - For Australia, **State** is a dropdown (NSW, VIC, QLD, SA, WA, TAS, NT, ACT) and **Postcode** must be 4 digits. For other countries both are free text. Switching Country to Australia clears a State that isn't one of these.
- **Frequent Floater badge:** "⭐ Frequent Floater" next to the customer's name when §7.8 applies. A new booking is checked against now, after the email lookup; an existing booking is anchored at its `ActivatedAt`. A tooltip gives the number of other bookings.
- Stand-by checkbox
- **Booking comments** (stored on the booking)
- **Customer comments**, directly below:
  - Stored on the Customer and shared by all of their bookings.
  - Pre-filled from the customer, and saved to the Customer when the booking is saved.
- Hire price: pre-filled and editable, with a "Recalculate from rates" button that re-prices the hire price and add-on lines in the form (§7.4)
- Status (Active/Cancelled editable; other statuses read-only)

**Holds (`PendingPayment` and `Expired`, read-only, decision 36):**
- The whole dialog is read-only: every field (including the customer section and Customer comments), the add-on lines and picker, Recalculate from rates, Save changes and Send payment link. The details entered online are shown read-only (see below).
- **PendingPayment:** Add manual payment is disabled too. A banner reads "Waiting for the customer's online payment. The hold expires at {dd/MM/yyyy h:mma}."
- **Expired, without payments:** only **Add manual payment** is enabled. A banner reads "This hold expired without payment. Record a manual payment of at least {checkout amount} to confirm the booking."
  - The manual payment dialog shows the same minimum as help text on the amount.
  - A confirming payment makes the booking Active (§7.3). The dialog reloads as an ordinary Active booking, editable, with the customer section filled from the new Customer. A toast confirms "Booking {reference} confirmed."
  - A `409` (the dates were taken meanwhile) shows the conflicting references, and nothing is recorded.
- **Expired, with a payment** (paid-but-expired, §8.3): Add manual payment stays disabled. A banner reads "Paid online after the hold expired — refund in Stripe."

**Add-ons section:**
- Lines with name, quantity, unit price, line total and a remove button, plus an add-on picker (add-ons in the admin-defined order) with an **Add** button.
- **Unit price:**
  - A new line's unit price is pre-filled with the add-on's price from Settings for the booking's period type (§7.4).
  - It is left empty for price-on-request add-ons (e.g. the welcome hamper) and for LongWeekend and Custom bookings. Until staff enter one, the line doesn't count toward the total.
- **Line total:** quantity and unit price are editable, and each line's total (`quantity × unit price`, or "Price TBC" when there is no price) is shown under its Total field. It updates as staff type.
- **Nothing is saved until the booking is:**
  - Adding, editing and removing lines only changes the form. Nothing is saved until **Create booking** or **Save changes**, and every other field keeps what was typed.
  - There are no per-line save buttons.
- Staff remove an add-on they decline.
- Capacity-limited add-ons show the remaining capacity for the booking's dates (excluding this booking) and a non-blocking warning when exceeded. On the new-booking form the figure is re-fetched whenever the start date, end date, period type or picked add-on changes (including when choosing a period type rewrites the end date); a slower, older response must never overwrite a newer one.

**Price breakdown** (at the bottom of the dialog, always visible above the Create booking / Save changes buttons):
- It shows Hire price, Add-ons (the sum of priced lines) and **Total**, plus Paid and Owing when editing.
- It is calculated in the browser from the form values as they change, so staff can quote the final price over the phone before saving.
- Unpriced lines are left out, with a note such as "1 add-on not yet priced".
- The server recalculates every amount on save.

**Payments section:**
- A list of payments, the payment status, and the schedule with due dates and amounts.
- The schedule depends on server rules (e.g. extended-schedule blocked periods), so it is calculated by the server and refreshes after saving. While the form has unsaved changes, it shows "Schedule updates when you save."
- **"Add manual payment"** (amount, date, note) and **"Send payment link"** (disabled when nothing is owing; shows a confirmation toast) are actions of their own, separate from Save changes. They are disabled while the form has unsaved changes, with the hint "Save your changes first".
- Add manual payment is available on Active bookings and, as staff confirmation, on Expired bookings without payments (see Holds above). Send payment link is available on Active non-stand-by bookings only.

**Read-only info for online bookings:** created by, created date, terms accepted at, rooming warning accepted at + text, and the customer details entered online.

**Behaviour:**
- Saving checks overlaps. On conflict, show the conflicting booking references.
- Version conflicts show "reload and retry".

### 12.2 Fleet
- A grid of boat cards: image thumbnail (or a placeholder), name, berth, adult beds + kids beds, Spa and Pet friendly badges when set, an active/inactive badge.
- "+ New boat".
- Clicking a card opens a dialog with tabs:
  - **Details:** Name, Slug, **Berth** (must be at least adult beds + kids beds; the error shows on the Berth field), Number of adult beds, Number of kids beds, BeddingDescription, SecurityBond, **Deposit** (optional; help text "Leave empty to use the default deposit of ${default} from Settings"), **Image URL** (optional; help text "Link to an image on bergerhouseboats.com.au", with a small preview of the image underneath), **Spa** and **Pet friendly** checkboxes, Active toggle (activation shows the missing data if the invariant fails).
  - **Rates:** a grid of seasons × {Midweek, Weekend, Week, LongWeekend}.
  - **Add-ons:** checklist of allowed add-ons.
  - **Unavailabilities:** a scrollable table (first night, last night, comments) with edit and delete, and a **"+ New Unavailability"** button.

### 12.3 Customers
- A table: name, email, mobile, secondary mobile, created date. A client-side text filter.
- Each customer row has a **dropdown expander**:
  - At the top, the customer's **Address** (read-only), on several lines: Address line 1, Address line 2, `City/Locality State Postcode`, Country name. Blank parts are skipped. It shows "No address" when there is none.
  - Below it, a **Customer comments** text area with a **Save** button (`PUT /api/customers/{id}/comments`). Version conflicts show "reload and retry".
  - Then all of that customer's bookings, loaded on expand from `GET /api/customers/{id}/bookings`: reference, boat, dates, period type, status and payment status. All statuses are shown, with inactive ones faded, ordered by StartDate, newest first.
  - Clicking a booking opens the **same booking dialog** as clicking its bar on the Bookings timeline.
- Nothing else is editable here. Contact details and the address are updated from the booking dialog.

### 12.4 Settings
Sections:
- **General:** default deposit (used for boats without their own deposit), minimum lead time, bookings open until, staff alert email, contact phone/email, hire terms URL.
  - It also has the bank **Account name**, **BSB** and **Account number**, with the help text "Optional. Available to email templates as {{AccountName}}, {{BSB}} and {{AccountNumber}}, e.g. to ask for a bank transfer instead of a payment link."
- **Seasons:** list + editor of recurring day-month ranges; overlap validation.
- **Blocked periods:** name, first night, last night, extended payment schedule flag. Help text: "Enter each year's Christmas/New Year, Easter and long weekends. Customers must phone to book these."
- **Add-ons:** catalogue editor including capacity and capacity unit label. **▲/▼ buttons** on each add-on change its position; no order number is shown or typed. This order is used in the booking app, the admin add-on picker and emails.
  - In the add-on editor, ticking **Price on request** disables and clears the unit price inputs. An empty **Capacity** disables and clears **Capacity unit label**; once a capacity is entered, the label is required. The server enforces the same rules (§7.3).
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
- **Boats** (all 14 are seeded with beds, bedding, bond, rates and every add-on allowed, taken from each boat's page on bergerhouseboats.com.au; only Pacific Blue is active, the rest stay inactive until staff verify them in Fleet; staff must verify slugs against WordPress URLs). Own deposits: Pacific Blue, Ocean Spirit and Island Dream $1000, Runaway Bear $250; every other boat uses the Settings default. Each boat's `ImageUrl` is the first image on its page (`https://bergerhouseboats.com.au/houseboats/<boat-slug>/`), and `HasSpa` and `IsPetFriendly` are set where the page says so; anything that cannot be confirmed from the page is left empty or `false` for staff to complete in Fleet. Every seeded boat satisfies `Berth >= AdultBeds + KidsBeds`. The full data is in `SeedService.cs`; Pacific Blue is the reference:

| Name | Slug | Berth |
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
  - With 8 beds in total and a berth of 12, 9+ guests (adults + children) triggers the rooming warning.
- **Email templates:** defaults for #1, #2, #4, #5, #6, #7, #8, #10. #2 includes `{{FrequentFloaterNotice}}`. Payment emails use `{{PaymentLink}}`, not bank details.
- **BusinessSettings:** deposit 1000, lead time 30, open-until 30/11/2027, contact phone/email placeholders for staff to fill in, bank details empty.

## 14. Infrastructure and operations

- **Environments:** two GCP projects, **dev** and **prod**, with identical infrastructure and nothing shared except the Artifact Registry. Dev runs with `ASPNETCORE_ENVIRONMENT=Development`, prod with `Production`; both behave identically apart from their `appsettings.{Environment}.json`. Dev uses Stripe test mode, its own Turnstile widget and Access application, and the same Gmail sender as prod with the `[DEV] ` subject prefix and the sender name "Berger Houseboats (DEV)". Dev uses the default timing (§10.2) unless a test needs shorter offsets.
- **GCP (per project):**
  - Firestore Native (`australia-southeast1`)
  - two Cloud Run services (min 0; max instances 1 for public, 2 for admin), each with its own service account and least privilege
  - Secret Manager
  - Cloud Scheduler job (hourly → admin-api `/api/jobs/process-notifications`, OIDC)
  - a Workload Identity pool and `github-deploy` service account. The prod pool only trusts GitHub jobs running in the `production` environment.
- **Artifact Registry:** one Docker repository in the prod project. Dev's deploy identity may push to it (and nothing else in prod); dev's Cloud Run pulls from it. Images are tagged with the commit SHA.
- **Cloud Run ingress:** `all`, because Cloudflare Pages Functions, Stripe and Scheduler must reach it. Protection comes from the proxy secret, Access JWT and OIDC checks in the apps.
- **Cloud Run invoker IAM check: disabled** (`--no-invoker-iam-check`) on both services. None of the callers sends a Google identity token except Scheduler, and the apps authenticate every route themselves. Granting `roles/run.invoker` to `allUsers` (`--allow-unauthenticated`) is not used: a project in a Google Workspace organisation usually inherits the *Domain restricted sharing* org policy, which silently blocks that grant and leaves every call failing with Cloud Run's 403. As a result no service account holds `roles/run.invoker`; the Scheduler service account only identifies the job to admin-api's OIDC check.
- **Cloudflare:**
  - **`bergerhouseboats.com.au` DNS stays at `partnerconsole.net`** with WordPress and Google Workspace mail; it is not moved to Cloudflare. The only change is one CNAME, `bookings` → `berger-booking.pages.dev`, added at that host after the custom domain is added in the Pages project (Cloudflare supports custom subdomains on domains that are not Cloudflare zones; an apex domain would not work). Any CAA records must allow Cloudflare's certificate authorities. The Cloudflare account has no DNS zone: it is used for Pages, Access and Turnstile only. If Google's DKIM, SPF or DMARC records are missing, they are added at the existing DNS host. If the host cannot add a CNAME, fall back to buying a separate domain and adding it to Cloudflare (free plan).
  - Two Pages projects per environment. `berger-booking` has `bookings.bergerhouseboats.com.au` as its custom domain. `berger-admin` has **no custom domain** and is served at `https://berger-admin.pages.dev` (project name placeholder). Dev uses `berger-booking-dev` and `berger-admin-dev`, served only at their `pages.dev` addresses.
  - An Access application covering `berger-admin.pages.dev`, created through the Pages project's "Enable access policy" and edited to cover the production address rather than the preview wildcard. Google Workspace IdP. Policy: include emails in the allow-list, **require** country AU. Customers are never put behind Access. Dev has its own Access application on `berger-admin-dev.pages.dev`, set up the same way.
  - A Turnstile widget for `bookings.bergerhouseboats.com.au`, and a separate one for `berger-booking-dev.pages.dev`.
  - No Cloudflare rate limiting rule (see the in-app limiter in §8, which also runs in dev).
- **Firestore collections:**
  - `boats`, `bookings`, `bookingDrafts`, `customers`, `customerEmails`, `boatUnavailabilities`, `boatLocks`
  - `seasons`, `blockedPeriods`, `addons`, `emailTemplates`, `settings`, `counters` (one document per booking reference prefix, e.g. `counters/ref-SMITH-270115`)
  - Composite indexes go in `firestore.indexes.json` (e.g. bookings by `boatId` + `status` + `startDate`; bookings by `status` + `startDate` (fleet availability); bookings by `addonIds` (array-contains) + `status` + `startDate`; bookings by `customerId` + `status` + `startDate` (Frequent Floater); unavailabilities by `boatId` + `firstNight`; bookingDrafts by `email` + `lastActivityAt`).
- **Logging and alerts:**
  - Structured logging to Cloud Logging. Never log card data or full tokens.
  - Log-based alert policies email the staff alert address on: webhook processing errors, job failures, paid-but-expired bookings, and deposits paid on a booking staff had already confirmed (§8.3).
- **CI/CD (GitHub Actions):**
  - On PR: build (with the §7.9 analyzer rules as errors), unit and architecture tests, integration tests against the Firestore emulator, web typecheck/lint/build.
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
  - Status transitions and illegal transitions, including `Expired → Active` on activation and the rejection of staff updates for `PendingPayment` and `Expired` bookings.
  - Stand-by superseding.
  - Guest rules: adults ≥ 1, adults + children ≤ berth (online), rooming warning trigger (`adults + children > AdultBeds + KidsBeds`).
  - Add-on capacity: busiest-night calculation including the four worked examples in §7.5; stand-by, Cancelled, Expired, Superseded and expired holds don't count; excluding a booking being edited.
  - Boat activation invariant (image URL, spa and pet friendly not required).
  - Boat `Berth >= AdultBeds + KidsBeds` on create and on edit, active or not.
  - Boat `ImageUrl` validation: empty allowed; only absolute `https://` URLs on `bergerhouseboats.com.au` or `www.bergerhouseboats.com.au` accepted.
  - Customer email normalisation; the email rule's accepted and rejected examples (§7.1), including `.com.au` and `.co.uk` accepted and a missing or numeric top-level domain rejected; customer address: parts trimmed, a partial address accepted, an address with only a country stored empty, the length limits, and the Australian state list and 4-digit postcode applied only when the country is `AU`; mobile number E.164 normalisation; an Australian mobile accepted, an Australian landline rejected, a `FIXED_LINE_OR_MOBILE` number (e.g. US) accepted.
  - Booking references:
    - The format comes from the last name and `StartDate` (`SMITH-2701150`).
    - Name clean-up: accents, apostrophes, hyphens, spaces, the 20-character limit and the `GUEST` fallback.
    - The suffix sequence, including past 9.
    - The reference is unchanged when the dates or the last name change later.
  - Frequent Floater: the ±1 year boundaries around the anchor; only other Active non-stand-by bookings count; an existing booking is anchored at `ActivatedAt`.
  - AddonDefinition: price on request clears prices; the capacity unit label is cleared without a capacity and required with one.
  - Booking draft abandonment rules.
- **Organisation:** every test class follows §7.9: one production class or use case per test class, named `<Class>Tests`, in the matching folder.
- **Application tests:**
  - Online and internal validation strategies as pure unit tests, each given a `BookingRequest` and a `BookingValidationContext`, including the online add-on capacity hard limit.
  - `StaffBookingWarnings`: guests above berth, guests above beds, add-on capacity exceeded (a warning, not an error), and changeover time clashes.
  - Notification job, using a fake clock and short offsets: idempotency; which emails at which instants; #4 and #7 skipped when their trigger had passed at activation (including a hold staff confirm days after it was created); #7 never sent for a milestone already due at activation; #1 including the payment link for late bookings; #6 sent for bookings created inside the pre-hire window.
  - Abandoned drafts: threshold, latest draft per email only, suppression after a booking for that email becomes Active, deletion after retention.
  - Webhook handling: idempotent replay, deposit activation creating/updating the Customer and converting the draft, a late deposit activating an Expired booking whose dates are free, expired-hold conflict path creating no Customer, a deposit for a booking staff already confirmed recorded with an error logged and nothing sent, and `checkout.session.expired` expiring a hold.
  - An expired hold never creates or changes a Customer unless staff confirm it.
  - Hold expiry: admin-api returns a hold whose `HoldExpiresAt` has passed as `Expired` (timeline, search, dialog, a customer's bookings) without saving it; staff confirmation of a lapsed hold expires and confirms it in one transaction; the job saves the expiry of the rest.
  - Staff confirmation (`POST /api/bookings/{id}/payments` on an Expired booking):
    - A payment of at least `min(DepositAmount, HirePrice)` records it and activates the booking (reference, `ActivatedAt`, Customer from `CustomerDetails`, draft converted, stand-bys superseded with #8), sending #1 only (no #2, no #5).
    - A smaller amount is rejected with a field error and records nothing.
    - Taken dates return `409` naming the conflicting references and record nothing.
    - It is rejected for an Expired booking that already has a payment, and manual payments and payment links are rejected for `PendingPayment` bookings.
  - Booking drafts: `GET /api/booking-drafts/{draftId}` returns only the first name, last name, email and mobile, and `404` for an unknown, converted or deleted draft.
  - `POST /api/bookings` releases a still-`PendingPayment` hold from the same draft before creating the new one, and returns `checkoutExpiresAt` = the session's `expires_at`, which is before `HoldExpiresAt`.
  - Fleet availability (`GET /api/availability`) and period availability (`GET /api/availability/period`): only active House boats are included; blocking bookings and unavailabilities make a boat unavailable while stand-by, Cancelled, Expired, Superseded bookings and expired holds do not; hire prices match `PricingService`; no booking or customer data is returned; invalid stays (period shape, lead time, open-until, blocked periods) return `400`.
  - Updating a draft with a different boat changes its `BoatId`, and `POST /api/bookings` then accepts it for that boat.
  - References: online bookings get one when the deposit is paid (including paid-but-expired), staff bookings at creation, and confirmed holds on staff confirmation; expired and released holds that staff don't confirm get none and use up no suffix (a staff confirmation failing with `409` may use one up).
  - Release (`POST /api/bookings/{bookingId}/release`): expires the Stripe session and the booking and frees the dates; a wrong draft id is rejected; an already Active booking returns `409`; repeating it is harmless.
  - Staff bookings with `addonLines`: new lines added, edited lines updated, left-out lines deleted; `POST /api/bookings/quote` pre-fill and recalculation prices (null for price on request, LongWeekend and Custom).
  - Customer comments are saved from staff booking create/update and from `PUT /api/customers/{id}/comments`; the address (every part, including a partial address) is saved from staff booking create/update; online activation and staff confirmation never change comments, the secondary mobile or the address.
  - Staff booking create **and update** reject an invalid email or mobile with a field error (e.g. changing an existing booking's email to `jane@gmail`).
  - Email #2 renders `{{FrequentFloaterNotice}}` only for a Frequent Floater booking; the bank placeholders render the Settings values (BSB as `123-456`) or empty.
- **Integration tests (Firestore emulator):**
  - Repositories round-trip.
  - **Concurrent booking race → exactly one winner.**
  - Hold expiry releases dates.
  - Optimistic concurrency 409.
  - Customer email uniqueness under concurrency.
  - Booking reference counter under concurrency: two bookings with the same prefix get distinct suffixes (0 and 1).
  - Add-on capacity queries across boats.
  - Fleet availability query across boats.
  - Rate limiter: the request over the limit gets `429` + `Retry-After`; another IP is unaffected; the Stripe webhook is exempt; requests without the proxy secret are rejected before the limiter and use no permits; `0` disables it; invalid settings stop startup.
- **Architecture tests** (`BergerHarbour.ArchitectureTests`, plain reflection, §7.9):
  - Domain references no other project in the solution. Application references only Domain. Neither API project references the other.
  - Every aggregate (a type deriving from `AggregateRoot`) has a typed id, and an `I<Aggregate>Repository` in its own Domain namespace. Exactly one `Firestore<Aggregate>Repository` in Infrastructure implements it.
  - Every controller has one constructor, whose parameters are exactly one application service (an Application type whose name ends in `Service`) and optionally an `ILogger<T>`. Within one API, no two controllers use the same service.
  - No Application type implements a port from `Application.Abstractions`.
  - Every `IBookingRequestValidationStrategy` implementation has only a parameterless constructor.
- **Frontend:** Vitest + React Testing Library, with MSW mocking the API. Pure rules (period mapping, greying, time-zone labels, lane assignment) are plain unit tests; behaviour is tested through the components. At least:
  - the calendar period mapping, per-period greying logic, and highlighting of non-clickable days inside a selection;
  - adjacent-month cells: muted, not clickable, and showing the muted selection colour when the selection extends into them;
  - the check-in/check-out line, including the AEST/NSW and AEDT/NSW labels and the split label for a stay that crosses a daylight-saving change;
  - the "Selected: …" text (dates only, no times), and the Summary's check-in and check-out rows (each with its own zone label);
  - children dropdown clamping (to the fleet's largest berth), and the capacity "remaining" display and hiding;
  - the Add-ons step's Booking price: updates as add-ons are ticked and quantities change; the price-on-request note;
  - Back from Payment releases the hold and returns to the Summary; a `409` on release goes to Complete;
  - the Payment step's countdown: whole minutes rounded up, "1 minute" in the last minute, and the "no longer held" state with its button back to the Summary once `checkoutExpiresAt` passes;
  - saved progress:
    - `localStorage` never contains the first name, last name, email or mobile, or the terms tick;
    - a reload resumes at the furthest step reached, at most the Summary, with Your details filled from `GET /api/booking-drafts/{draftId}`;
    - a saved hold is released on resume, and a `409` goes to Complete;
    - a period that no longer passes the rules, or a boat no longer free, sends the customer to Dates & boat with the matching message;
    - saved progress is discarded on Complete, on a draft `404`, for a different `?boat=`, and when storage throws;
  - the capacity "remaining" text always uses the unit label;
  - the Close button: posts `bh-booking:close` inside an iframe, and falls back to the WordPress page outside one when the window stays open;
  - busy states in both apps: a button is disabled and labelled while its request runs and can't submit twice; a dialog closes only on success and keeps the user's input on error;
  - Dates & boat step: calendar greying with the checkbox unticked (a period is free when any candidate boat is free, after the guest, Spa and Pet friendly filters) and ticked (selected boat only); the checkbox's enabled, disabled and ticked states, including selecting another card leaving it unchanged; deselecting the selected boat when the period or a filter makes it ineligible, without clearing the period; the requested boat always first; the requested boat's reason precedence (inactive → dates → too small → filters); hiding other boats not free for the period and the two empty-list messages; Base hire rate states; the rooming warning on Continue and re-showing it after the boat or guests change; add-ons removed after changing boat;
  - admin timeline week view date ranges (Mon–Sun, Prev/Next, Today) and the Month ↔ Week switching rules;
  - admin timeline loading: the large spinner in the cell area while a range with no data loads, with the day headers and boat names still shown; a loaded range showing without a spinner; a slower, older response never replacing a newer range; the error state with Retry;
  - admin timeline bar positioning from check-in/check-out times (default and custom), and unavailability block positioning;
  - admin timeline lane assignment by check-in/check-out time (a standard changeover shares a lane; clashing custom times get a new lane);
  - admin new-booking pre-fill for a clicked day of every weekday: Mon–Thu → Mid-week Mon→Fri of that week, Fri–Sun → Weekend Fri→Mon containing it; "+ New" applies the same rule to today with no boat; empty cells of inactive boats are not clickable while their bars still open;
  - field validation on blur: an invalid email or mobile shows its error when the field loses focus, the error clears as the value is fixed, and pressing the main button focuses the first invalid field;
  - admin month view filling the width, and scrolling only below the minimum column width;
  - admin booking dialog:
    - Adding, editing and removing add-on lines sends no request.
    - Other fields (e.g. the mobile) keep their typed values when lines change.
    - New lines are pre-filled with the Settings price.
    - Per-line totals update as quantity and unit price change.
    - The live price breakdown (Total, Paid, Owing, the unpriced-line note) and the "Schedule updates when you save" note.
    - The Frequent Floater badge.
    - Address and customer comments: always filled from the customer on an email lookup that finds one, and kept as typed when none is found.
    - Address fields: none required; Country defaulting to Australia or the stored country; the Australian State dropdown and 4-digit postcode; free text for other countries.
    - No customer lookup for an invalid email.
    - Manual payment and Send payment link are disabled while there are unsaved changes.
    - Holds: a PendingPayment booking fully read-only with its expiry banner; an Expired booking read-only except Add manual payment, with the minimum shown, becoming editable once confirmed; a paid-but-expired booking with Add manual payment disabled.
  - add-on settings: price on request disables and clears the unit prices; an empty capacity disables and clears the unit label; a capacity without a unit label shows "Required";
  - the booking reference shown on Complete, and "Pending payment" bar labels for holds.

## 16. Deliverables

1. The full solution as laid out in §6 and organised as in §7.9, building with the analyzer rules as errors and passing all tests, including the architecture tests.
2. `/wordpress/book-now-block.html`.
3. `/docs/setup-checklist.md`, a step-by-step checklist a non-developer can follow:
   1. **Booking subdomain:** add `bookings.bergerhouseboats.com.au` as a custom domain of the `berger-booking` Pages project, then add one CNAME at the existing DNS host (and check CAA records). The existing DNS and email are otherwise not changed. Fallback if no CNAME is possible: buy a separate domain.
   2. GCP project, APIs, Firestore, service accounts, Secret Manager, Cloud Run, Scheduler, alerts, Workload Identity Federation for GitHub, GitHub Environments. The checklist is run once per environment (dev, then prod), with a table of the values that differ, and marks the steps that apply to only one of them (shared Artifact Registry, Releases, booking subdomain).
   3. Stripe account, webhook endpoint (`checkout.session.completed`, `checkout.session.expired`), payment method domains, switching from test to live keys.
   4. Gmail SMTP: 2-Step Verification and a Google app password for `info@bergerhouseboats.com.au`, and checking SPF, DKIM and DMARC for `bergerhouseboats.com.au` at its existing DNS host.
   5. Cloudflare Pages projects (booking custom domain; admin on `pages.dev` with no custom domain), `_headers`, Access app on the admin `pages.dev` address + Google IdP + policy, Turnstile.
   6. Run the seed. In the admin app, complete every boat (berth, adult beds, kids beds, bedding, bond, optional deposit, image URL, spa, pet friendly, rates, allowed add-ons) and activate it. Set add-on prices, order and car parking capacity. Enter blocked periods (Easter, long weekends) up to "bookings open until". Review email templates. Fill in contact details and, if wanted, bank details.
   7. **Before go-live: staff enter every existing future booking** from paper/Excel as staff bookings, recording payments already received as manual payments. Past bookings are not entered.
   8. End-to-end test in Stripe test mode, then switch to live keys.
   9. Paste the HTML block into each of the 14 WordPress boat pages.
4. A final summary listing anything not implemented and every assumption you made.

**Suggested build order:**
1. Analyzer rules, architecture tests, Domain + unit tests.
2. Firestore infrastructure + integration tests.
3. Application services.
4. APIs.
5. Booking app.
6. Admin app.
7. Notifications job.
8. WordPress block.
9. Docs and CI.
