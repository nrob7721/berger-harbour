using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

public sealed class Booking : AggregateRoot
{
    public string Id { get; private set; } = "";
    /// <summary>LASTNAME-YYMMDDX. Empty until assigned; never changes afterwards.</summary>
    public string? Reference { get; private set; }
    public BookingCreator CreatedBy { get; private set; }
    public bool IsStandby { get; private set; }
    public BookingStatus Status { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public string? CustomerId { get; private set; }
    public CustomerDetails? CustomerDetails { get; private set; }
    public string? BookingDraftId { get; private set; }
    public BoatId BoatId { get; private set; }
    public PeriodType PeriodType { get; private set; }
    public StayRange Stay { get; private set; }
    public GuestNumbers Guests { get; private set; } = new(1, 0);
    public TimeOnly CheckInTime { get; private set; } = SydneyTime.StandardCheckIn;
    public TimeOnly CheckOutTime { get; private set; } = SydneyTime.StandardCheckOut;
    public string? BookingComments { get; private set; }
    public decimal HirePrice { get; private set; }
    public decimal DepositAmount { get; private set; }
    public PaymentScheduleType PaymentSchedule { get; private set; }
    public IReadOnlyList<AddonLine> AddonLines { get; private set; } = [];
    public IReadOnlyList<Payment> Payments { get; private set; } = [];
    public DateTimeOffset? HoldExpiresAt { get; private set; }
    public string? StripeCheckoutSessionId { get; private set; }
    public RoomingWarningAcceptance? RoomingWarningAcceptance { get; private set; }
    public DateTimeOffset? TermsAcceptedAt { get; private set; }
    public string PaymentLinkTokenHash { get; private set; } = "";
    public IReadOnlyList<SentNotification> SentNotifications { get; private set; } = [];

    private Booking() { }

    public DateOnly StartDate => Stay.Start;
    public DateOnly EndDate => Stay.End;

    // ----- Derived values -----
    public int NumberOfGuests => Guests.Total;
    /// <summary>Hire price + every add-on line that has a price.</summary>
    public decimal TotalPrice => HirePrice + AddonLines.Sum(l => l.LineTotal ?? 0m);
    public decimal AmountPaid => Payments.Sum(p => p.Amount);
    public decimal AmountOwing => Math.Max(0m, TotalPrice - AmountPaid);
    public int UnpricedAddonLineCount => AddonLines.Count(l => l.UnitPrice is null);

    public PaymentStatus PaymentStatus =>
        AmountPaid == 0m ? PaymentStatus.Outstanding
        : AmountPaid >= TotalPrice ? PaymentStatus.FullyPaid
        : AmountPaid == DepositAmount ? PaymentStatus.DepositPaid
        : PaymentStatus.PartPaid;

    public IReadOnlyList<string> AddonIds => AddonLines.Select(l => l.AddonId).Distinct().ToList();

    /// <summary>Non-stand-by Active bookings, and non-stand-by holds that haven't lapsed.</summary>
    public bool BlocksAvailability(DateTimeOffset now) =>
        !IsStandby && (Status == BookingStatus.Active || (Status == BookingStatus.PendingPayment && HoldExpiresAt > now));

    public bool IsHoldLapsed(DateTimeOffset now) => Status == BookingStatus.PendingPayment && HoldExpiresAt <= now;

    /// <summary>"FirstName LastName" from the snapshot (callers use the Customer's name when there is one).</summary>
    public string SnapshotName => CustomerDetails?.FullName ?? "";

    // ----- Creation -----

    public static Booking CreateOnlineHold(
        BoatId boatId, PeriodType periodType, StayRange stay, GuestNumbers guests, CustomerDetails details, string draftId,
        decimal hirePrice, decimal depositAmount, PaymentScheduleType schedule, IEnumerable<AddonLine> addonLines,
        RoomingWarningAcceptance? roomingAcceptance, DateTimeOffset termsAcceptedAt, DateTimeOffset holdExpiresAt,
        string bookingId, string paymentLinkTokenHash, DateTimeOffset now)
    {
        var b = new Booking
        {
            Id = bookingId, CreatedBy = BookingCreator.Customer, Status = BookingStatus.PendingPayment,
            CustomerDetails = details, BookingDraftId = draftId, BoatId = boatId, PeriodType = periodType, Stay = stay, Guests = guests,
            HirePrice = Money.Round(hirePrice), DepositAmount = Money.Round(depositAmount), PaymentSchedule = schedule,
            AddonLines = addonLines.ToList(), RoomingWarningAcceptance = roomingAcceptance, TermsAcceptedAt = termsAcceptedAt,
            HoldExpiresAt = holdExpiresAt, PaymentLinkTokenHash = paymentLinkTokenHash,
        };
        b.InitialiseAudit(now);
        return b;
    }

    public static Booking CreateByStaff(
        string bookingId, string reference, BoatId boatId, PeriodType periodType, StayRange stay, GuestNumbers guests,
        TimeOnly checkInTime, TimeOnly checkOutTime, bool isStandby, string customerId, string? bookingComments,
        decimal hirePrice, decimal depositAmount, PaymentScheduleType schedule, IEnumerable<AddonLine> addonLines,
        string paymentLinkTokenHash, DateTimeOffset now)
    {
        if (hirePrice < 0) throw new DomainValidationException("hirePrice", "Hire price can't be negative.");
        var b = new Booking
        {
            Id = bookingId, Reference = reference, CreatedBy = BookingCreator.SystemUser, Status = BookingStatus.Active, ActivatedAt = now,
            IsStandby = isStandby, CustomerId = customerId, BoatId = boatId, PeriodType = periodType, Stay = stay, Guests = guests,
            CheckInTime = checkInTime, CheckOutTime = checkOutTime,
            BookingComments = Text.Optional(bookingComments, "bookingComments", "Booking comments"),
            HirePrice = Money.Round(hirePrice), DepositAmount = Money.Round(depositAmount), PaymentSchedule = schedule,
            AddonLines = addonLines.ToList(), PaymentLinkTokenHash = paymentLinkTokenHash,
        };
        b.InitialiseAudit(now);
        return b;
    }

    public sealed record State(
        string Id, string? Reference, BookingCreator CreatedBy, bool IsStandby, BookingStatus Status, DateTimeOffset? ActivatedAt,
        string? CustomerId, CustomerDetails? CustomerDetails, string? BookingDraftId, BoatId BoatId, PeriodType PeriodType, StayRange Stay,
        GuestNumbers Guests, TimeOnly CheckInTime, TimeOnly CheckOutTime, string? BookingComments, decimal HirePrice, decimal DepositAmount,
        PaymentScheduleType PaymentSchedule, IReadOnlyList<AddonLine> AddonLines, IReadOnlyList<Payment> Payments, DateTimeOffset? HoldExpiresAt,
        string? StripeCheckoutSessionId, RoomingWarningAcceptance? RoomingWarningAcceptance, DateTimeOffset? TermsAcceptedAt,
        string PaymentLinkTokenHash, IReadOnlyList<SentNotification> SentNotifications, int Version, DateTimeOffset CreatedDate, DateTimeOffset ModifiedDate);

    public static Booking Rehydrate(State s)
    {
        var b = new Booking
        {
            Id = s.Id, Reference = s.Reference, CreatedBy = s.CreatedBy, IsStandby = s.IsStandby, Status = s.Status, ActivatedAt = s.ActivatedAt,
            CustomerId = s.CustomerId, CustomerDetails = s.CustomerDetails, BookingDraftId = s.BookingDraftId, BoatId = s.BoatId,
            PeriodType = s.PeriodType, Stay = s.Stay, Guests = s.Guests, CheckInTime = s.CheckInTime, CheckOutTime = s.CheckOutTime,
            BookingComments = s.BookingComments, HirePrice = s.HirePrice, DepositAmount = s.DepositAmount, PaymentSchedule = s.PaymentSchedule,
            AddonLines = s.AddonLines.ToList(), Payments = s.Payments.ToList(), HoldExpiresAt = s.HoldExpiresAt,
            StripeCheckoutSessionId = s.StripeCheckoutSessionId, RoomingWarningAcceptance = s.RoomingWarningAcceptance,
            TermsAcceptedAt = s.TermsAcceptedAt, PaymentLinkTokenHash = s.PaymentLinkTokenHash, SentNotifications = s.SentNotifications.ToList(),
        };
        b.RestoreAudit(s.Version, s.CreatedDate, s.ModifiedDate);
        return b;
    }

    // ----- Online payment lifecycle -----

    public void AttachCheckoutSession(string sessionId)
    {
        if (Status != BookingStatus.PendingPayment) throw new ConflictException("This booking is no longer waiting for payment.");
        StripeCheckoutSessionId = sessionId;
    }

    /// <summary>
    /// PendingPayment → Active when the deposit is paid. Assigns the reference and the activation instant.
    /// A hold that lapsed (Expired) before the payment arrived is also activated, as long as the caller found the dates still free.
    /// </summary>
    public void ActivateAfterDeposit(string reference, DateTimeOffset now)
    {
        if (Status is not (BookingStatus.PendingPayment or BookingStatus.Expired) || CreatedBy != BookingCreator.Customer)
            throw new ConflictException($"A {Status} booking can't be activated by a deposit.");
        Status = BookingStatus.Active;
        Reference ??= reference;
        ActivatedAt ??= now;
        HoldExpiresAt = null;
    }

    /// <summary>The deposit was paid but the dates could not be kept: the booking stays Expired but gets a reference so staff can find it.</summary>
    public void AssignReference(string reference) => Reference ??= reference;

    public void LinkCustomer(string customerId) => CustomerId = customerId;

    /// <summary>PendingPayment → Expired (hold lapsed, Stripe session expired, or released from the Payment step). Idempotent.</summary>
    public void Expire()
    {
        if (Status == BookingStatus.Expired) return;
        if (Status != BookingStatus.PendingPayment) throw new ConflictException("This booking has already been paid.");
        Status = BookingStatus.Expired;
        HoldExpiresAt = null;
    }

    // ----- Staff lifecycle -----

    public void Cancel()
    {
        if (Status != BookingStatus.Active) throw new ConflictException($"Only an Active booking can be cancelled (this one is {Status}).");
        Status = BookingStatus.Cancelled;
    }

    /// <summary>Cancelled → Active. The caller runs the availability check.</summary>
    public void Reactivate()
    {
        if (Status != BookingStatus.Cancelled) throw new ConflictException($"Only a Cancelled booking can be made Active again (this one is {Status}).");
        Status = BookingStatus.Active;
    }

    /// <summary>Active stand-by → Superseded, when an Active non-stand-by booking overlaps it.</summary>
    public void Supersede()
    {
        if (!IsStandby || Status != BookingStatus.Active) throw new ConflictException("Only an Active stand-by booking can be superseded.");
        Status = BookingStatus.Superseded;
    }

    public void UpdateByStaff(
        BoatId boatId, PeriodType periodType, StayRange stay, GuestNumbers guests, TimeOnly checkInTime, TimeOnly checkOutTime,
        bool isStandby, string? bookingComments, decimal hirePrice, PaymentScheduleType schedule)
    {
        if (Status == BookingStatus.PendingPayment)
            throw new ConflictException("This booking is waiting for the customer's payment and can't be edited yet.");
        var errors = new ValidationErrors();
        errors.AddIf(hirePrice < 0, "hirePrice", "Hire price can't be negative.");
        var comments = errors.Capture(() => Text.Optional(bookingComments, "bookingComments", "Booking comments"));
        errors.ThrowIfAny();
        BoatId = boatId;
        PeriodType = periodType;
        Stay = stay;
        Guests = guests;
        CheckInTime = checkInTime;
        CheckOutTime = checkOutTime;
        IsStandby = isStandby;
        BookingComments = comments;
        HirePrice = Money.Round(hirePrice);
        PaymentSchedule = schedule;
    }

    /// <summary>Replaces the add-on lines with the full list staff saved.</summary>
    public void ReplaceAddonLines(IEnumerable<AddonLine> lines) => AddonLines = lines.ToList();

    public void ChangeCustomer(string customerId) => CustomerId = customerId;

    // ----- Payments -----

    /// <summary>Records a payment. Stripe payments are idempotent on their PaymentIntent id. Returns false for a replay.</summary>
    public bool RecordPayment(Payment payment)
    {
        if (payment.Amount <= 0) throw new DomainValidationException("amount", "The amount must be more than $0.");
        if (payment.StripePaymentIntentId is { } pi && Payments.Any(p => p.StripePaymentIntentId == pi)) return false;
        Payments = [.. Payments, payment with { Amount = Money.Round(payment.Amount), Note = Text.Optional(payment.Note, "note", "Note", 500) }];
        return true;
    }

    public bool HasPaymentIntent(string paymentIntentId) => Payments.Any(p => p.StripePaymentIntentId == paymentIntentId);

    // ----- Notifications -----

    public bool HasSentNotification(string key) => SentNotifications.Any(n => n.Key == key);

    public void MarkNotificationSent(string key, DateTimeOffset at)
    {
        if (!HasSentNotification(key)) SentNotifications = [.. SentNotifications, new SentNotification(key, at)];
    }
}
