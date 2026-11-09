namespace BergerHarbour.Domain.BookingDrafts;

/// <summary>The last booking app step a draft reached.</summary>
public enum DraftStep
{
    Details,
    Addons,
    Summary,
    Payment,
}
