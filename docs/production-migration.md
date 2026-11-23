# Production booking migration (diary → system)

Plan and analysis for seeding production from the backup `Diary 2024-25.xlsx` (9 Aug 2024 → Nov 2028). The analysis was run on 23 Nov 2026 against a copy of the file. The spreadsheet and the repo were not changed.

The counts are produced by a heuristic parser, because the diary is a free-text calendar grid, not a table. Treat them as accurate to within a few percent. The first import dry run replaces them with exact numbers.

## 1. Decisions

| Topic | Decision |
|---|---|
| Scope | Bookings starting on or after 23 Nov 2025 (one year before the import, so Frequent Floater is correct), plus every booking ahead of today. Earlier bookings are not imported. This departs from `prompt.md` (historical import is a non-goal; go-live step 7 enters future bookings only), so update the spec. |
| Past bookings with missing mandatory data | Import with dummy values (below). |
| Upcoming bookings with missing or ambiguous mandatory data | Not imported. They are flagged for staff to enter manually. |
| Dummy email | Unique per booking: `unknown+<ledgerId>@couldntbedetermined.com.au`. A shared address would collapse them into one Customer, overwrite names and make them all Frequent Floaters of each other. A `+` in the local part passes `EmailAddress`. |
| Dummy mobile | `+61415555555` (valid for `MobileNumber`; mobiles aren't unique). |
| Dummy names | `Unknown` for a missing first or last name. |
| Payment dates | The real date is recorded in the payment note (manual payments can only be dated today). The year is the **latest year for which the date falls on or before the stay's start date**. A payment with no date says "date unknown" in the note. |
| Addresses | Attempt to parse into street, locality, state, postcode and country. If there is no postcode or `Address.Create` rejects the result, fall back to the unparsed text in `Line1` (section 4.1). |
| Emails | No email may be sent. The import runs with `EmailMode=File`, and each imported upcoming booking has its notification keys pre-recorded as sent (section 7). |
| Moved or upgraded bookings | No duplicates need removing (section 5). |

## 2. How the diary is laid out

- One sheet per ~25 weeks. Of the 9 data sheets, the last 3 are almost empty. Sheets named "Blank" and "15 blank one week" are templates.
- Each day is a column. Weekends use 3 columns (Fri, Sat, Sun nights) and midweeks use 4 (Mon–Thu nights), with narrow spacer columns between. The date row is formulas, so cached values are read. Weekday labels have typos (`Frid`, `Sun␣␣`).
- Each boat is a band of 5–6 rows: `Runaway Bear`, `Wanderer`, `Wavebreak`, `Misty Blue`, `Marie Claire`, `JFS Rhyanna`, `Paradise II`, `Image Anne`, `Gypsea Belle`, `Blue Bayou`, `Kalinda`, `Island Dream`, `Ocean Spirit`, `Pacific Blue`. The sheets also label a 15th boat, **Patricia**, which has no bookings and isn't in the system.
- A booking is a "card" in the band, starting at the column of its first night:

| Row | First column | Middle column | Last column |
|---|---|---|---|
| 0 | `d/m Name` (booked date, name) | notes | hire price |
| 1 | address | notes | extras (`t/m $95`, `1 x car $45`) |
| 2 | email | notes | extras |
| 3–5 | payments (`3/5 $500 d/d`) | emails sent, arrival time | phones (`0412 042 010 Kyle`) |

- Fills: green means paid in full, pink means deposit paid, yellow means arrival time. Fills carry no extent information.
- A stay longer than one block continues in the next block as `Name cont` cards, but some continuations are unlabelled.
- The diary has no booking references, no guest counts and no payment years.

## 3. Extraction rules

1. A card is anchored by a row-0 cell matching `d/m Name`. Name-only cells (no booked date) anchor the leftmost row-0 cell of a block.
2. Anchors that are payment or email-sent notes (`3/12 Ins Em`, `8/7 Bal Em`) are ignored. A second anchor in the column directly after another anchor is a note inside the first card.
3. `cont` cards, unlabelled continuation cells and leftover cells in the next block are attached to the previous card on that boat. Two orphan `cont` cards were dropped.
4. 14 non-booking entries are excluded (owner use, `Ben – on marina`, stray notes).
5. Stand-by (`S/B name`) lines are not parsed as bookings.
6. **Stay dates** are inferred from the block span:
   - The stay starts on the card's first column.
   - It ends at the end of the last block in the chain: Monday for a weekend block, Friday for a midweek block.
   - If another booking on the same boat starts inside the span, the stay ends there (same-day changeover).
   - Block-span inference left no same-boat overlaps in the window.
7. **Period type:** Fri→Mon (3 nights) is Weekend, Mon→Fri (4) is Midweek, and Fri→Fri or Mon→Mon (7) is Week. Everything else is Custom.

## 4. Field mapping

| System field | Rule |
|---|---|
| Boat | The band label. |
| Adults / children | 1 / 0, because the diary has no guest counts. |
| First / last name | `A B Surname` gives `A B` / `Surname`. `A & B Surname` gives `A & B` / `Surname`. Particles (van, de, von…) stay with the surname. A company (`Name – Company`) gives the person's first name and the company as last name. A name that is only a company repeats the company as the first name. |
| Email | The first valid email. Further valid emails go to `BookingComments` as `Extra email: <address>`. An invalid address also goes to comments. |
| Mobile | A valid mobile (per `MobileNumber`) whose label matches the booker's first name, else the first listed (the booker is first listed in 91% of checkable cases). |
| Secondary mobile | The next distinct valid mobile. |
| Extra mobiles | `Extra mobile X: <number>` in `BookingComments` (X = 3, 4…). |
| Landline, invalid, garbled numbers | `BookingComments`, plus the dummy mobile if no valid one remains. |
| Hire price | The diary figure. For a composite (`($1590 b/c + $300 cash) $1890`) or several figures, the last one written. If absent, the boat's default for the period type and start-date season. |
| Payments | Each `$amount` becomes a manual payment dated today. The note carries the real date (per section 1), the method (d/d, c/c, cash, b/c, eft, Q) and any credit or gift-voucher reference (`GV #1329`, `credit`, `s/b cr`). |
| Add-ons | Section 6. |
| Arrival time | `CheckInTime` when parseable. Otherwise the system default. |
| Address | Parse, then fall back (section 4.1). |
| Notes | Free text goes to `BookingComments`. |
| Status | Active. A booking with no recorded payment is Active with payment status Outstanding. `PendingPayment` is an expiring online hold that staff can't edit, so it is not used. |
| Stand-by | Not imported. |

### 4.1 Addresses

Addresses are free text on row 1 of the card (`6 Blanch St, Manly West 4179`, `79 Hereford Dr CASINO   NSW   2470`). The import attempts a parse and falls back to the unparsed text if the parse fails or `Address.Create` rejects it.

1. **Parse.** Normalise whitespace and commas, then read from the right:
   - A trailing 4-digit **postcode**, optionally preceded by (or followed by) a state abbreviation, gives `Postcode`.
   - `State` is the abbreviation if present, otherwise derived from the postcode range (2xxx NSW, 3xxx VIC, 4xxx QLD, 5xxx SA, 6xxx WA, 7xxx TAS, 08xx NT, 26xx/29xx ACT).
   - The text up to and including the first street-type word (St, Rd, Dr, Ave, Ct, Court, Cres, Pl, Way, Lane, Pde, Hwy, Cl, Tce, Esp…) is `Line1`. Unit prefixes such as `4/23` stay in `Line1`.
   - The text between the street type and the postcode is `Locality`, title-cased when it is all capitals (`DARLING HEIGHTS` → `Darling Heights`).
   - `Country` is `AU`.
2. **Partial parse.** With a postcode but no street-type word (`PO Box 624 TWEED HEADS 2485`, `105 Dakota Parkway Cooranbong 2265`), `Line1` keeps all the text before the postcode and `Locality` stays empty. The state and postcode are still set, and `Country` is `AU`.
3. **Validate** with the same rules as `Address.Create`: every part within its length limit, a state from the Australian list and a 4-digit postcode for `AU`.
4. **Fall back.** If there is no postcode, or validation fails, the whole original text goes to `Line1` with no state, postcode or country. Any text that can't be parsed is never dropped.
5. **Overseas addresses** (for example New Zealand, `… Wellington 5012 NZ`) are not parsed and go to `Line1` unchanged.
6. A booking with no address text imports with no address.

The parse result is recorded in the staging file as `full`, `partial` or `unparsed`, so staff can review the fall-backs.

## 5. Moves, upgrades and duplicates

43 in-window bookings carry move, upgrade or postponement notes (`upgraded from IA`, `moved from 27/4`). For each, I searched other boats for an overlapping stay sharing the same phone or email. None was found: the diary keeps only the new card, so there is nothing to merge. The cost is that "upgraded from X" cards often lack a name or contact details (these are among the dummy-value bookings).

Several customers booked two boats for the same dates (for example Phil Newton on Kalinda and Pacific Blue). These are genuine separate bookings.

## 6. Add-ons

Extras text maps to the seeded add-ons as follows:

| Diary text | Add-on |
|---|---|
| `t/m $95` (about 450 bookings) | Outboard motor |
| `1 x car`, `3 x cars`, `CP` | Car parking |
| `yabbie pump`, `YP`, `yab` | Yabby pumps |
| `grocery order` | Grocery service |
| `rods` | Fishing gear |
| `heater` | Outdoor heater |

**Add-ons that don't exist and must be created before the import** (booking counts in the import window):

| Add-on | Bookings | Notes |
|---|---|---|
| Trailer parking | 27 | $40–$45 |
| Crab pots | 19 | $20 each, sometimes no charge |
| Towels | 15 | about $5 each |
| Bed linen / sheets (single and double) | 8 | `SB`/`DB` sheets, $10–$40 |
| Pet fee | 8 | $100 or $200 |
| Grazing platter | 1 | |
| Sunday return fee | 1 | `$50`. This could be a price adjustment instead. |

Kayaks and drop lines occur only in 2024 and are out of scope. Unit prices vary per booking, so imported lines keep the price typed in the diary. `SB` also means Security Bond ("paid SB via DD") and Stand-by in other cells, so those cells are not read as add-ons.

## 7. Import mechanism

**Pipeline:** extract → staging file (one row per booking, keyed by diary cell) → review → import.

**Implementation:**
- Add an `import-bookings` mode to the AdminApi next to `seed` (`Program.cs`), calling `StaffBookingService.CreateAsync` in-process. This keeps every staff-booking rule: validation, customer matching by email, reference generation, the availability lock and stand-by superseding.
- A `--dry-run` flag validates and reports without saving.
- Process bookings in start-date order, non-stand-by only.
- The extractor and mapping scripts used for this analysis were scratch files. Put the real extractor under `scripts/migrations/` (standalone, outside the solution's build rules).

**Emails.** `EmailMode=File` for the import process is configuration only. It is not enough on its own: the live service's scheduled job later emails imported upcoming bookings (pre-hire instructions, payment-due reminders), because their activation time is the import time. For each imported upcoming booking the import must therefore record these keys as sent via `RecordNotificationSent`:

- `BookingConfirmed`
- `PreHire`
- `PaymentDue:<kind>:Reminder`
- `PaymentDue:<kind>:Due`
- `Overdue:<kind>`

Past bookings aren't selected by the job. This is the one required code addition.

**Timestamps.** `CreatedDate` and `ActivatedAt` are the import time. The deposit follows the boat. Staff create ignores lead-time rules.

**Idempotency and the ledger.**
- The **source key** is the diary cell that anchors the booking, for example `16 Jan 26 - 9 Jul 26!D54`. It is deterministic: the same file gives the same keys.
- The **ledger** (CSV or JSON in the repo's migration folder, not committed with customer data) maps source key → ledger id → booking id → reference → import time → result.
- The import skips keys already in the ledger and writes the ledger after each booking.
- A crash between the Firestore save and the ledger write could leave an orphan booking. Re-creating a non-stand-by booking on the same boat and dates returns `409`, so a duplicate cannot be created.
- The ledger id (a sequential number) also feeds the unique dummy email.

**Booking references.** The diary has no references (no cell matches `LASTNAME-YYMMDDX`), so there is nothing to preserve. References are system-generated from the last name, start date and a counter at `counters/ref-<prefix>`. Editing them in Firestore is possible but leaves the counters out of step, so don't. Importing in start-date order makes the suffixes stable between runs. Dummy-name bookings get `UNKNOWN-…` references.

**Safety.** Rehearse on dev or the emulator, then take a Firestore export of production before the real run.

## 8. Results

### Scope and totals

| | Count |
|---|---|
| Booking cards in the whole diary | 1,436 |
| In the window (start ≥ 23 Nov 2025, or still running) | 669 |
| **Imported** | **660** (530 past-window, 130 upcoming) |
| **Manual entry (upcoming)** | **9** |

Imported by period type: Weekend 242, Midweek 189, Week 130, **Custom 99**. Upcoming imports: Weekend 38, Midweek 38, Week 30, Custom 24.

### Dummy values and comments

- **Past bookings using dummies: 26.** Of these, 14 have a dummy email, 16 a dummy mobile and 4 a dummy name.
- **Booking comments:**
  - 17 bookings with extra mobiles.
  - 8 with landline or garbled numbers.
  - 23 with extra emails.
  - 1 with a bad email.
- **Names:** 194 `A & B` splits and 7 company last names.
- **Money:**
  - 26 hire prices from the boat default.
  - 9 past bookings with conflicting prices use the last figure written.
  - 6 past Custom stays have no price at all and import with a `$0` placeholder.
- **Payments:** 1,113 manual payments across the imported bookings. 41 bookings (14 upcoming) have no payment and import Outstanding.
- **Addresses (prototype parser, all 669 window bookings):** 622 parse fully (street, locality, state, postcode), 13 parse partially (no street-type word), 14 fall back to `Line1` (no trailing postcode, a postcode before the state as in `4213 QLD`, or an overseas address) and 20 have no address. The `4213 QLD` form is an easy fix, so expect the fall-backs to drop below 14.
- **Customers:** about 596 distinct customers, plus one unique dummy Customer per dummy-email booking.

### Manual entry (9 upcoming bookings)

| Boat | Start | Customer | Reason |
|---|---|---|---|
| Wanderer | 27 Nov 2026 | Stuart Bell | No email |
| Paradise II | 23 Nov 2026 | Kayla Kissell | Two prices ($695 and $3,465) |
| Pacific Blue | 21 Dec 2026 | Carey Archer | Two prices ($8,800 and $8,900) |
| Kalinda | 29 Dec 2026 | Glenda & Trevor Newman | Custom stay, no price |
| Paradise II | 30 Dec 2026 | Robin Furlong | No mobile |
| Paradise II | 4 Jan 2027 | Karen Prior | Only an invalid number (`0 916 0294`) |
| Runaway Bear | 5 Mar 2027 | Leeann Lawrence | Only a landline |
| Kalinda | 7 Jun 2027 | Lisa Halliday | No mobile |
| Pacific Blue | 28 Sep 2027 | Beth Roberts | No mobile, Custom stay with no price |

### Not imported by design

- 5 stand-by lines in the window: Andrew Mulligan (Wavebreak, 22 Jan 2026), Donna Maric (Paradise II, 12 Feb 2026), Grima (Pacific Blue, 14 May 2026), Janelle Belford (Runaway Bear, 29 Aug 2026) and Blundell (Marie Claire, 15 Oct 2026). All are dated in the past, so staff decide what to do with any credit.
- 14 non-booking entries (owner use, notes).

## 9. Known limits

- Stay ends are inferred from block spans, so 24 upcoming and 99 total Custom stays should be spot-checked, along with the 21 upcoming stays that start mid-block.
- Every imported booking has 1 adult and 0 children. 22 diary cells mention a guest count in free text. These are copied into the booking comments.
- Past bookings with dummy values will show `UNKNOWN` references and placeholder customers in the admin.
- Cancelled bookings: none were found in the window, and the two "cancelled" mentions concern other parties.

## 10. Order of work

1. Update `prompt.md` (scope, non-goals and go-live step 7).
2. Create the 7 add-ons in the admin.
3. Build the extractor, the ledger and `import-bookings`, with the notification pre-marking.
4. Dry run on dev, review the staging file and the 9 manual cases, then a full rehearsal.
5. Export production Firestore, run the import, and verify counts against section 8.
6. Staff enter the 9 manual bookings, then re-run the check for the Frequent Floater flags.
