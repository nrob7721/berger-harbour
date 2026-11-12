namespace BergerHarbour.Domain.Bookings;

/// <summary>A payment milestone: the cumulative amount that must be paid by <see cref="DueAt"/>.</summary>
public sealed record PaymentMilestone(
    MilestoneKind Kind,
    string Label,
    decimal RequiredCumulative,
    DateTimeOffset DueAt,
    DateOnly DueDate,
    bool DueAtActivation)
{
    public decimal ShortfallFor(decimal amountPaid) => Math.Max(0m, RequiredCumulative - amountPaid);
}
