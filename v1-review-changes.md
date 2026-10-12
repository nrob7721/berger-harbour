# To Investigate

1. 

# To Confirm

## Me

1. Display priority of booking payment status
1. Alignment issues
    1. Admin Booking -> Stand-by booking and Status
    1. Admin Booking -> Number of guests and warning


## Angus

1. Pet Friendly checkbox for each boat - how does this affect booking workflow e.g. 


# To Change

1. Customer booking workflow changes:
    1. Move the "Your Details" section to step 2. Order should be:
        1. Dates
        1. Your details
        1. Guests
        1. Add-ons
        1. Summary
        1. Payment
    1. Dates section
        1. In the Dates section, we want to display a Checkin period instead of set time. Display "11 - 2pm" instead of "1pm". Also add a timezone indicator i.e. AEST
        1. Currently when the "Week" radio button is selected, only Mondays and Fridays are available for selection, other days are grayed out. When a date is selected, the days between the IN and OUT are still grayed out which appears ambiguous, they must be an active colour with the number text active instead of gray
    1. Guests section
        1. Remove "Is your group an under-30s..." question. It is not necessary
        1. Split "Number of guests" into "Number of adults" and "Number of kids".
            1. Minimum one adult requirement
            1. (Admin app) - Each boat needs "kids beds" and "Adult beds" configuration (see below for details)
            1. Keep warning the same i.e. sum no of kids + adults selected. If it is greater than the boat's No of kids beds plus the no of adult beds, show the warning
            1. Additional check - ensure sum of adults and kids does not exceed a boat's 'max guests' setting
    1. Addons section
        1. Addons may have special holiday period prices. Add "Addon prices subject to change during peak periods" to the existing static message "Add-ons are requests and will be confirmed..."
        1. Car parking - spaces remaining functionality. Car parking will be a permanent special add on that is always quantity based. There are 11 car spots in total shared across all bookings. When the user is booking, there must be a "X car spots remaining message" under the  where the X is calculated by the backend as 11 - (sum of other booking car sports). When there are 0 car spots available, the add on should not appear at all.ç
    1. Your details section:
        1. Mobile number - MUST have a country code selection as it's possible to get bookings from overseas
        1. Currently when a customer starts the booking process and does not complete it, if they have entered their details, it will be added to the customer data. Instead, an email should be sent to staff to inform them so they can follow up.
1. Admin system changes:
    1. Bookings
        1. Order boats by berth/maxNoOfGuests
        1. Admin bookings extra fields (not customer facing)
            1. Secondary phone number - defaults empty when customer created
            1. Custom checkin/checkout times - defaults to 11am checkin and 8am checkout when customer created
        1. New bookings will need to allow the admin user to specify number of adults and kids
    1. Fleet
        1. Deposit PER boat functionality. We will keep the standard deposit in the general settings but each boat will have it's own Deposit field under Fleet settings. When this is empty, the default deposit under general settings applies.
        2. Each boat will need a "Number of kids beds" and "Number of adult beds" settings
    1. Settings
        1. Addons
            1. Order functionality - The admin user should be able to change the order of addons in the settings. This order is reflected in the customer facing addon section of the booking workflow
    1. Customers
        1. For each customer, there should be a dropdown expander that displays all bookings of a particular customer. 
            1. Clicking on the booking itself should bring up the details of that booking exactly the same as if it were selected from the calendar in the Bookings section
            1. Next to each booking, there should be a "Re-book" functionality that opens the new booking dialog will all details prepopulated from the existing booking. The dates should be the same but for the coming year e.g. if the historical booking was for 23/10/2021 - 28/10/2021 and the current date is 10/03/2023, the prepopulated dates should be 23/10/2023 - 28/10/2023.

1. General System Changes:
    1. Remove individual add-on booking status i.e. we don't need a "Declined" option. If it is declined, the staff will simply remove it from the booking. If they confirm, they will leave it there.
    1. Datetime format - australian with day, month, then year. NO american style dates
    1. Payment status doesn't make sense Prompt.md (lines 235 - 239) - else `DepositPaid` if `AmountPaid <= DepositAmount`. should be DepositPaid if AmountPaid is equal to DepositAmount.

1. Need to confirm:
    1. Deposit - Confirm behaviour: Even if a customer is booking a house book with minimal lead time, only the deposit should be requested, not the full amount.
    1. Automated emails - confirm behaviour: For short-term bookings, does the system know NOT to send reminders? e.g. if the user books with 10 days lead time, they should not be sent an email for payment warnings e.g. Notifications__BalanceDueBeforeHire as it is already past the 30 day warning period.
    
