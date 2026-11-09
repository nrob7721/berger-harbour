namespace BergerHarbour.Infrastructure.Web;

/// <summary>Headers added or forwarded by the Pages Function proxies.</summary>
public static class Headers
{
    public const string EdgeProxySecret = "X-Edge-Proxy-Secret";
    public const string AccessJwt = "Cf-Access-Jwt-Assertion";
    public const string ConnectingIp = "CF-Connecting-IP";
    public const string StripeSignature = "Stripe-Signature";
}
