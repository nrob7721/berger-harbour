namespace BergerHarbour.Application.Seed;

/// <summary>Values the seed can't know: the staff alert address and the contact details shown to customers.</summary>
public sealed record SeedOptions(string? StaffAlertEmail, string? ContactPhone, string? ContactEmail);
