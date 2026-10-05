Existing

Online system for booking boats

- 5x BBQ boats
    - already on online booking system
- 14x House Boats
    - not on system
- Happy with current functionality - system takes cut of booking. Designed for daily hire
- Allows use of ‘canned messages’ - when they book and pay, they get an auto generated message. X days before day of hire, instructions are sent
- BBQ boats - only see size 8/12
- House Boats - super individual - see each one
- **Staff not keeping up - house boats, customers can only call to find out availability**
- More than one 12berth, the bedding arrangements could be 2 doubles/queens. Workers had to ascertain the mix of people fits the rooms e.g. 6 couple (good), 12 individuals (won´t work).
- There are post booking addons that are manually confirmed
- Bookings taken 1 yr in advance for house boats
- House boats currently written on paper and duplicated on excel


There is already an existing Wordpress website that customers use for information about the house boats. Currently, there is no way to book a house boat without calling the company resulting in inundated employees handling enquiries. We will be adding functionality to the wordpress site via a custom button widget that opens a new window for the user to book a house boat. The button will be added to each of the individual house boat pages

# Functional Requirements

## Customers

### Booking

The customer sees a new dialog through which they can book and pay the deposit. 

Mandatory fields:
- Full name
- Email address
- Mobile number
- Number of guests

They must be able to:

- See availability - a small classic style booking calendar. 
    - StandBy bookings will NOT be displayed
    - Boats marked as unavailable in the internal system will have everything grayed and an info message at the top of the dialog informing them that it is unavailable
    - Booking options:
        - Mid week (Monday - Friday)
        - Weekend (Friday - Monday)
        - Week (Monday - Monday OR Friday - Friday) - when the user selects this option, only Mondays and Fridays will be available to select
    - When a booking option is selected via radio button, the irrelevant days should be grayed out e.g. 
        - if the user selects "Mid-week", the weekend Saturdays and Sundays should be disabled.
        - If the user selects "Week", only the Mondays and Fridays should be displayed, this will allow distinction between a Mon -> Mon booking and Fri -> Fri booking
    - When a customer selects a particular day, the whole period should be highlighted e.g.
        - If the user selects "Mid-week" and then selects a Tuesday, the mid week period Mon - Friday should be selected. If they clicked on the Wednesday in the same week, the selection should not change
        - If the user selects "Week" and then selects a Monday, the period from the selected Monday until the next Monday should be selected.
    - Holiday period (20/12 - 05/01)
        - Customers cannot book over the holiday period.
        - These calendar days will be grayed out.
        - When a booking period is selected in this range, a pop-up will notify the user that they must enquire via email or telephone. E.g.
            - If the customer selects "Mid-week" and chooses a Tuesday that is 18/12, they will see this pop-up because the mid week period (Mon 17/12 - Friday 21/12) falls inside the holiday period
- Pay deposits / balance - normal checkout system most likely using Square or Stripe payments. Deposit is always $1000
- Customers must be warned of rooming configuration e.g. a house boat may have 8 individual rooms and some have queen beds so it can fit 12 people in total. However, if a customer isn´t aware of the individual rooms, they may assume it's 12 individual rooms. If the customer selects 9+ guests, they must be warned with a pop-up and click "Accept" to ensure they understand the room configuration.
- See and select available add-ons
    - Yabby pumps
    - Ice
    - Bait
    - Pizza oven
    - Fishing gear
    - Welcome hamper (tick boxes).
    - Outboard motor
    - Car parking
    - Grocery service
    - On mooring jetty stays
    - Skipper
    - Outdoor heater

### Post booking

- Receive email notifications
    - Normal bookings - Full balance payment 30 days before hire date
    - Holiday period bookings:
        - 50% balance payment 120 days before hire date
        - 100% balance payment 90 days before hire date

## Business

The company will have an internal system that allows them to manage bookings, the fleet details and customer details.

The system will have a menu bar at the top with the following menu items

### 1. Bookings

- This page will display a full screen calendar with all bookings
- A "New" button with a plus symbol will open a dialog for the user to enter the following information:
    - Full Name - mandatory
    - Phone Number - optional
    - Email - Optional
    - StandBy booking - checkbox to indicate
    - Comments - optional
    - Payment Status - mandatory dropdown
- Clicking an existing booking will bring up the same dialog for the user to edit details

### 2. Fleet

- This page will display a list of existing boat cards
- Users can select a boat card and a pop-up dialog will allow them to:
    - Edit details of a boat
        - Name
    - View unavailabilities - a simple scrollable table displaying the date periods the boat is unavailable
    - Create unavailabilities - a "New Unavailability" with a plus button will all users to create a date period for which the boat is unavailable

Internal users of the system must be able to:

- Edit/adjust availabilities
- Create “stand-by” bookings - these are like store credit bookings which
    - Are not visible to customers when making new bookings
    - Will be overridden by any new customers

# Implementation Details

## Infrastructure

### Backend

Infrastructure will be hosted on GCP. To discuss: Google Cloud Run for affordability?

C# ASP.NET application
- DDD
    - Validation logic should be encapsulated within the domain objects i.e. in constructors, appropriately named methods
    - Aggregate Root - To Start, we will have all persisted domain objects (Boats, Customers, Bookings) as separate aggregates. If a natural root surfaces down the line, we will join them. Follow Vaughn Vernon's advocacy of small aggregates with Id references instead of object references
    - create domain services for things like booking validation
- Repository pattern. Give all aggregates a separate repository and interface. Interfaces should be defined alongside the objects they deal with.
- Atomic Transactions when making a booking to handle race conditions

#### Endpoints

- Controllers:
    - FleetController - for all fleet adjustments
        - GET all boat details - for Fleet page
        - POST boat updates
        - POST a new boat unavailability
        - GET specific boat details - for when a user clicks on a boat to edit details/availabilities.
    - BookingController - for all booking creations/amendments
        - GET specific boat details - limited only to necessary boat data for the customer booking
        - POST a new booking - booking dates from the customer facing UI must have validation around Week/Mid-week/Weekend logic. Bookings from admin/internal use must not do so. This can be implemented via dependency injection with a validation strategy pattern
- To discuss: endpoint to wakeup GCP when a customer clicks the house boat widget button?

### Frontend

Static GCP bucket

Implemented with separate HTML and CSS files. Should be implemented with best practice to ensure readability

Will a basic static HTML/CSS page be sufficient?

### Authentication

CloudFlare access paired with Tunnel to reduce auth responsibility and workload. Strict tunnel configuration for all admin endpoints i.e. creating internal bookings via the internal system, editing fleet details etc
    - Policies should always include location verification from NSW
To discuss: authentication for customer facing endpoints e.g. to make a booking, check availability etc. Cannot put auth around these endpoints. Should there be separate endpoints for these? i.e. different controllers

## Data

- We will start with a Firestore implementation and later, we may move to relational DB if necessary. Discuss options and impact to system

- Customer
    - Id
    - CreatedDate
    - ModifiedDate
    - Email
    - MobileNumber

- Boat
    - Id
    - Name
    - Type - for future functionality. Enum of
        - House
        - BBQ
    - MaxNoOfGuests
    - NoOfRooms

- BoatUnavailable
    - Id
    - BoatId
    - CreatedDate
    - ModifiedDate
    - StartDate
    - EndDate
    - Comments

- Booking
    - Id
    - CreatedDate
    - ModifiedDate
    - CreatedBy - enum indicating via website (Customer) or the Business (SystemUser)
    - StandbyBooking - boolean
    - CustomerId - reference to Customer table
    - BoatId
    - StartDate
    - EndDate
    - Comments
    - PaymentStatus - enum of
        - Outstanding
        - DepositPaid
        - FiftyPercentDepositPaid
        - OneHundredPercentDepositPaid
        - FullyPaid
    - Addons - individual boolean flags for each addon mentioned above.

- Notification system must be configurable via environment variables so that we can test under shorter periods