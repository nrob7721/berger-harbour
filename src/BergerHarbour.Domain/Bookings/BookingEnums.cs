namespace BergerHarbour.Domain.Bookings;

public enum BookingStatus
{
    PendingPayment,
    Active,
    Superseded,
    Cancelled,
    Expired,
}

public enum BookingCreatedBy
{
    /// <summary>Booked online through the website.</summary>
    Customer,

    /// <summary>Entered by staff in the admin app.</summary>
    SystemUser,
}

public enum PaymentSchedule
{
    /// <summary>100% due 30 days before check-in.</summary>
    Standard,

    /// <summary>50% due 120 days before check-in and 100% by 90 days before (Christmas/New Year).</summary>
    Extended,
}

/// <summary>Derived from the payments; never stored as the source of truth.</summary>
public enum PaymentStatus
{
    Outstanding,
    DepositPaid,
    PartPaid,
    FullyPaid,
}

public enum AddonLineStatus
{
    Requested,
    Confirmed,
    Declined,
}

public enum PaymentMethod
{
    Stripe,
    Manual,
}

public enum PaymentRecordedBy
{
    Customer,
    SystemUser,
}
