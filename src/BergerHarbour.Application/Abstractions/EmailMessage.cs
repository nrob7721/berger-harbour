namespace BergerHarbour.Application.Abstractions;

/// <summary>An email to send. The sender adds the From/Reply-To and the environment's subject prefix.</summary>
public sealed record EmailMessage(string To, string Subject, string HtmlBody);
