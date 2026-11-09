namespace BergerHarbour.Application.Notifications.Dtos;

/// <summary>What one run of the scheduled job did.</summary>
public sealed record NotificationJobResult(int HoldsExpired, int EmailsSent, int AbandonedDraftsReported, int DraftsDeleted, int Failures);
