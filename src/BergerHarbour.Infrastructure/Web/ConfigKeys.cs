namespace BergerHarbour.Infrastructure.Web;

/// <summary>Configuration keys (environment variables or <c>appsettings.{Environment}.json</c>).</summary>
public static class ConfigKeys
{
    public const string PublicBookingBaseUrl = "PUBLIC_BOOKING_BASE_URL";
    public const string CorsAllowedOrigin = "CORS_ALLOWED_ORIGIN";
    public const string EdgeProxySecret = "EDGE_PROXY_SECRET";
    public const string FirestoreProjectId = "FIRESTORE_PROJECT_ID";
    public const string PaymentLinkSecret = "PAYMENT_LINK_SECRET";
    public const string StripeSecretKey = "STRIPE_SECRET_KEY";
    public const string StripeWebhookSecret = "STRIPE_WEBHOOK_SECRET";
    public const string TurnstileSecret = "TURNSTILE_SECRET";
    public const string EmailMode = "EMAIL_MODE";
    public const string EmailSender = "EMAIL_SENDER";
    public const string EmailSenderName = "EMAIL_SENDER_NAME";
    public const string EmailSubjectPrefix = "EMAIL_SUBJECT_PREFIX";
    public const string SmtpPassword = "SMTP_PASSWORD";
    public const string EmailOutputDir = "EMAIL_OUTPUT_DIR";
    public const string RateLimitPerMinute = "RATE_LIMIT_PER_MINUTE";
    public const string CfAccessTeamDomain = "CF_ACCESS_TEAM_DOMAIN";
    public const string CfAccessAud = "CF_ACCESS_AUD";
    public const string AdminAllowedEmails = "ADMIN_ALLOWED_EMAILS";
    public const string SchedulerServiceAccountEmail = "SCHEDULER_SERVICE_ACCOUNT_EMAIL";
    public const string SchedulerAudience = "SCHEDULER_AUDIENCE";
    public const string DevAdminEmail = "DEV_ADMIN_EMAIL";
    public const string DevJobsBypass = "DEV_JOBS_BYPASS";
    public const string SeedStaffAlertEmail = "SEED_STAFF_ALERT_EMAIL";
    public const string SeedContactPhone = "SEED_CONTACT_PHONE";
    public const string SeedContactEmail = "SEED_CONTACT_EMAIL";

    /// <summary>The only environment that relaxes security checks (a developer machine against emulators).</summary>
    public const string LocalEnvironment = "Local";
}
