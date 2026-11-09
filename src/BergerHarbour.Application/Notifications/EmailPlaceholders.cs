namespace BergerHarbour.Application.Notifications;

/// <summary>The placeholders email templates may use (§9). Comments are deliberately not available.</summary>
public static class EmailPlaceholders
{
    public static readonly IReadOnlyList<string> All =
    [
        "CustomerName", "CustomerFirstName", "CustomerEmail", "CustomerMobile", "BookingReference", "BoatName", "CheckInDate", "CheckOutDate",
        "CheckInTime", "CheckOutTime", "NumberOfGuests", "NumberOfAdults", "NumberOfChildren", "HirePrice", "TotalPrice", "AmountPaid",
        "AmountOwing", "AmountDue", "DueDate", "PaymentLink", "SecurityBond", "RequestedAddons", "PaymentSchedule", "HireTermsUrl",
        "ContactPhone", "ContactEmail", "LastStepReached", "FrequentFloaterNotice", "AccountName", "BSB", "AccountNumber",
    ];

    public static readonly IReadOnlySet<string> Known = new HashSet<string>(All, StringComparer.Ordinal);
}
