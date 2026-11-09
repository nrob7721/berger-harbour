namespace BergerHarbour.Application.Abstractions;

/// <summary>The checkout session's metadata purpose: the online deposit, or a later payment from a payment link.</summary>
public enum CheckoutPurpose
{
    Deposit,
    Payment,
}
