AI - DO NOT IMPLEMENT YET

# BBQ boats

- BBQ boats will work similarly to House Boats with the exception that it's possible to have multiple of the same bbq boat
- i.e. there are 4x bbq boats with 12 people and 1x houseboat with 8 people
- BBQ boats are only available for half day and full day rentals periods
- The booking calendar the customer sees:
    - No week/middweek/weekend bookings. Instead there is only half day and full day options
    - for a given BBQ boat e.g. "12 Berth BBQ boat", the availability check must factor in that BBQ boats have quantity e.g. for a particular day, while 3 of the 4 12 berth bbq boats might be booked, the last is available so the days should be available
- Would want all bookings to be processed online (contrary to house boats) i.e. ideally, complex pricing variables are accounted for programmatically

# Functional requirements

## Admin Side

1. Bookings
    1. There must be a separate calendar for BBQ boats. 
        1. Toggle at the top next to Week/Month toggle for "House Boats"/"BBQ Boats"
        1. This will be a normal calendar with a 1 week period per line
        1. User will create price availabilities differently i.e. instead of season periods per 
        1. When user clicks a day, there is an option to create an availability or a booking. A booking cannot be created if there is no existing availability for a BBQ boat
1. Fleet
    1. Open decision - should House Boats and BBQ boats have separate menu items under fleet?
    1. Most properties will be shared except BBQ boats will NOT need AdultBeds, KidsBeds, BeddingDescription, TotalBeds and Rates

# Considerations

1. Should the data be stored separately e.g. separate firestore collection for BBQ boats and House Boats? This naturally implies there could be a separate bookings document as well. Inheritance should be avoided
    1. Different validation for BBQ and House Boats
1. Domain logic may need to be encapsulated in domain services with different interfaces e.g. Booking Availability service might need an interface with separate concrete classes for BBQ and House boat implementations
