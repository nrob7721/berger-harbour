# Quick frontend changes (not yet in prompt.md)

Each entry is a frontend-only change made without updating `prompt.md`. A spec-sync pass reads the entries with `Status: pending`, applies them to `prompt.md` (or decides to revert them), then sets `Status: synced` and notes the date. `Flag` entries are experiments: ask the user whether to make the flag permanent or remove the code before writing them into the spec.

Backfilled from the session that preceded the `quick-frontend-update` skill. All changes are in `web/booking/src/features/dates-and-boat/components/` (plus `app/Shell*`, `app/BookingApp.tsx`). Base for QC-1 to QC-6: `6fa3a73` (the "reference point" commit of the prompt.md implementation).

## QC-1: Expanded boat card gets mini tabs
- **Date:** 2026-11-26
- **Status:** pending
- **Request:** Add mini sub tabs to the More details expander for the tags, prices and bedding description.
- **Behaviour now:** The expanded card's details area is a tablist: Features (the boat's tags), Prices (seasons table, "booked by phone" note, deposit and bond) and Bedding (only when the boat has a bedding description). Clicking a tab never selects the boat; Left/Right arrows move between tabs. The first available tab is selected on open.
- **Replaces:** Bedding text, prices table and notes shown stacked together.
- **Spec sections likely affected:** §11.2 (boat cards, More details)
- **Files:** BoatDetails.tsx, BoatDetails.module.css, BoatCard.tsx, DatesAndBoatStep.test.tsx
- **Base / commit:** 6fa3a73 → b0bcddc
- **Flag:** none
- **Backend impact:** none
- **Stopgaps:** none
- **Tests:** updated prices test to click through tabs

## QC-2: Closed detailed cards: 5-tag limit and photo reel
- **Date:** 2026-11-26
- **Status:** pending
- **Request:** Non-expanded cards show only the top 5 tags then "+X more" (as on mobile); replace the static thumbnail with the image reel; rename "Tags" tab to "Features".
- **Behaviour now:** Detailed cards (Boats view and small-screen expander) show the first 5 tags then "+N more", on every screen size. Once `GET /api/boat-details` has arrived, the closed card shows the photo reel (prev/next, swipe, counter; no caption until opened) instead of the thumbnail; the thumbnail shows until then or on error. Opening More details keeps the reel on its current photo. The tab is named "Features".
- **Replaces:** The 5-tag limit applying on small screens only; the thumbnail on closed detailed cards; the "Tags" tab name.
- **Spec sections likely affected:** §11.2
- **Files:** BoatCard.tsx, BoatCard.module.css, BoatCards.tsx, BoatDetails.tsx, PhotoReel.tsx, DatesAndBoatStep.tsx, DatesAndBoatStep.test.tsx
- **Base / commit:** 6fa3a73 → bbe7842
- **Flag:** none
- **Backend impact:** none
- **Stopgaps:** none
- **Tests:** updated two, added closed-card reel test

## QC-3: "+N more" opens the Features tab; darker tag border
- **Date:** 2026-11-26
- **Status:** pending
- **Request:** "+N more" opens the More details expander to the Features tab instead of expanding in place; feature tags get a slightly darker border.
- **Behaviour now:** "+N more" opens More details (Features tab). While the details are open but not ready, all tags show in the card. Tag pills use the strong border colour.
- **Replaces:** In-place expansion of the tag list.
- **Spec sections likely affected:** §11.2
- **Files:** BoatCard.tsx, BoatCard.module.css, BoatDetails.module.css, DatesAndBoatStep.test.tsx
- **Base / commit:** 6fa3a73 → f129427
- **Flag:** none
- **Backend impact:** none
- **Stopgaps:** none
- **Tests:** updated small-screen "+N more" test

## QC-4: Desktop Dates & boat: one mode card, sticky menu, independent scrolling
- **Date:** 2026-11-26
- **Status:** pending
- **Request:** Wrap both mode columns into one card with a centre border that moves with the active mode; sticky menu holding the selected dates, Base hire rate and Continue; page doesn't scroll, each column scrolls independently (review doc v1.5 To Consider #3).
- **Behaviour now:** On desktop (wider than 768px and taller than 600px) the page is exactly the viewport tall and the step fills it. The two columns share one card whose single divider slides when the mode changes; each column scrolls on its own. Below the card a menu bar of the same width shows the selected dates line, the Base hire rate and Continue (and the choose-dates-and-boat error). On a short viewport the page scrolls normally with the menu sticky at the bottom. Small screens are unchanged (sticky bar, expander, dates line in the left column).
- **Replaces:** The active-column border; the dates line, rate and Continue at the bottom of the left column.
- **Spec sections likely affected:** §11.2 (layout of Dates & boat), decision 43
- **Files:** DatesAndBoatStep.tsx, DatesAndBoatStep.module.css, app/Shell.tsx, app/Shell.module.css, app/BookingApp.tsx
- **Base / commit:** 6fa3a73 → f129427
- **Flag:** none
- **Backend impact:** none
- **Stopgaps:** none
- **Tests:** none changed

## QC-5: Mode headers; feature tags hidden (experiment); boat website link
- **Date:** 2026-11-26
- **Status:** pending
- **Request:** Clickable headers at the top of each mode; as an experiment hide feature tags on Boats-mode cards and the Features tab (don't delete code); link each boat to its WordPress page.
- **Behaviour now:** Each desktop mode column has a sticky header ("Booking details", "Boats"); the active one is marked current, the inactive one is a button that switches mode (with a "View boats ›" / "‹ Back to dates" cue). Clicking the inactive column's background still switches. Feature tags are not shown on cards and there is no Features tab while `SHOW_FEATURE_TAGS` is false. Each detailed card has a "View on our website ↗" link (new tab) to `https://bergerhouseboats.com.au/houseboats/<slug>/`.
- **Replaces:** Background-click as the only mode switch; tags on detailed cards.
- **Spec sections likely affected:** §11.2, decision 43
- **Files:** DatesAndBoatStep.tsx, DatesAndBoatStep.module.css, BoatCard.tsx, BoatCard.module.css, BoatDetails.tsx, DatesAndBoatStep.test.tsx
- **Base / commit:** 6fa3a73 → 820ebf2
- **Flag:** `SHOW_FEATURE_TAGS` in BoatDetails.tsx (false)
- **Backend impact:** none (tags are still loaded and edited in Fleet)
- **Stopgaps:** website link built from the slug with `boatPageUrl`, not a stored URL
- **Tests:** two tag-only tests skipped with `it.skipIf(!SHOW_FEATURE_TAGS)`; added header and link tests

## QC-6: Remove mode-card border and inactive-column highlight
- **Date:** 2026-11-26
- **Status:** pending
- **Request:** Remove the border around the modes; remove the hover background and inactive column highlight.
- **Behaviour now:** No border around the pair of columns (only the centre divider); the inactive column has no shade or hover background. The inactive column still switches on click and keeps a pointer cursor; the header button is the visible cue.
- **Replaces:** The grey inactive shade and its hover darkening (amends QC-4/QC-5).
- **Spec sections likely affected:** §11.2, decision 43
- **Files:** DatesAndBoatStep.module.css
- **Base / commit:** 6fa3a73 → uncommitted
- **Flag:** none
- **Backend impact:** none
- **Stopgaps:** none
- **Tests:** none changed
