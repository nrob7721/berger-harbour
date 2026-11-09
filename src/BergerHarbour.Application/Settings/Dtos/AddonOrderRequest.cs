namespace BergerHarbour.Application.Settings.Dtos;

/// <summary>The full ordered list of add-on ids.</summary>
public sealed record AddonOrderRequest(
    IReadOnlyList<string> AddonIds);
