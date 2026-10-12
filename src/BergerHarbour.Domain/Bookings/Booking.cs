using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

public sealed class Booking : AggregateRoot<BookingId>
{
    private readonly List<AddonLine> _addonLines;
    private readonly List<Payment> _payments;
    private readonly List<SentNotification> _sentNotifications;

    private Booking(BookingId id, int version, BookingReference reference, BookingCreatedBy createdBy, bool isStandby,
        BookingStatus status, CustomerId? customerId, BookingCustomerDetails? customerDetails, BookingDraftId? bookingDraftId,
        BoatId boatId, PeriodType periodType, Stay stay, int numberOfAdults, int numberOfChildren, TimeOnly checkInTime,
        TimeOnly checkOutTime, string? comments, decimal hirePrice, decimal depositAmount, PaymentSchedule paymentSchedule,
        IEnumerable<AddonLine> addonLines, IEnumerable<Payment> payments, DateTimeOffset? holdExpiresAt,
        string? stripeCheckoutSessionId, RoomingWarningAcceptance? roomingWarningAcceptance, DateTimeOffset? termsAcceptedAt,
        string paymentLinkTokenHash, IEnumerable<SentNotification> sentNotifications, DateTimeOffset createdDate,
        DateTimeOffset modifiedDate)
        : base(id, version)
    {
        Reference = reference;
        CreatedBy = createdBy;
        IsStandby = isStandby;
        Status = status;
        CustomerId = customerId;
        CustomerDetails = customerDetails;
        BookingDraftId = bookingDraftId;
        BoatId = boatId;
        PeriodType = periodType;
        Stay = stay;
        NumberOfAdults = numberOfAdults;
        NumberOfChildren = numberOfChildren;
        CheckInTime = checkInTime;
        CheckOutTime = checkOutTime;
        Comments = comments;
        HirePrice = hirePrice;
        DepositAmount = depositAmount;
        PaymentSchedule = paymentSchedule;
        _addonLines = addonLines.ToList();
        _payments = payments.ToList();
        HoldExpiresAt = holdExpiresAt;
        StripeCheckoutSessionId = stripeCheckoutSessionId;
        RoomingWarningAcceptance = roomingWarningAcceptance;
        TermsAcceptedAt = termsAcceptedAt;
        PaymentLinkTokenHash = paymentLinkTokenHash;
        _sentNotifications = sentNotifications.ToList();
        CreatedDate = createdDate;
        ModifiedDate = modifiedDate;
    }

    public BookingReference Reference { get; }

    public BookingCreatedBy CreatedBy { get; }

    public bool IsStandby { get; private set; }

    public BookingStatus Status { get; private set; }

    /// <summary>
    /// Staff bookings always have a customer. Online bookings get one when they become Active (deposit paid); until
    /// then, and for holds that expire, it is null and <see cref="CustomerDetails"/> holds what the customer entered.
    /// </summary>
    public CustomerId? CustomerId { get; private set; }

    /// <summary>Online bookings: the name, email and mobile the customer entered.</summary>
    public BookingCustomerDetails? CustomerDetails { get; }

    /// <summary>Online bookings: the draft this booking came from.</summary>
    public BookingDraftId? BookingDraftId { get; }

    public BoatId BoatId { get; private set; }

    public PeriodType PeriodType { get; private set; }

    public Stay Stay { get; private set; }

    public DateOnly StartDate => Stay.StartDate;

    public DateOnly EndDate => Stay.EndDate;

    public int NumberOfAdults { get; private set; }

    /// <summary>Children are under 12.</summary>
    public int NumberOfChildren { get; private set; }

    /// <summary>Staff-only; never shown in the booking app and never affects availability or capacity.</summary>
    public TimeOnly CheckInTime { get; private set; }

    /// <summary>Staff-only; never shown in the booking app and never affects availability or capacity.</summary>
    public TimeOnly CheckOutTime { get; private set; }

    public string? Comments { get; private set; }

    /// <summary>Snapshot of the rate at creation; staff may override.</summary>
    public decimal HirePrice { get; private set; }

    /// <summary>Snapshot of the boat's effective deposit at creation.</summary>
    public decimal DepositAmount { get; }

    public PaymentSchedule PaymentSchedule { get; private set; }

    public IReadOnlyList<AddonLine> AddonLines => _addonLines;

    public IReadOnlyList<Payment> Payments => _payments;

    public DateTimeOffset? HoldExpiresAt { get; private set; }

    public string? StripeCheckoutSessionId { get; private set; }

    public RoomingWarningAcceptance? RoomingWarningAcceptance { get; }

    public DateTimeOffset? TermsAcceptedAt { get; }

    public string PaymentLinkTokenHash { get; }

    public IReadOnlyList<SentNotification> SentNotifications => _sentNotifications;

    public DateTimeOffset CreatedDate { get; }

    public DateTimeOffset ModifiedDate { get; private set; }

    // ---- Derived values -------------------------------------------------------------------------------------------

    public int NumberOfGuests => NumberOfAdults + NumberOfChildren;

    /// <summary>True when staff changed the check-in or check-out time from the standard one.</summary>
    public bool HasCustomTimes => CheckInTime != Stay.CheckInTime || CheckOutTime != Stay.CheckOutTime;

    /// <summary>Hire price plus every add-on line with a price.</summary>
    public decimal TotalPrice => HirePrice + _addonLines.Sum(l => l.LineTotal ?? 0m);

    public decimal AmountPaid => _payments.Sum(p => p.Amount);

    public decimal AmountOwing => Math.Max(0m, TotalPrice - AmountPaid);

    /// <summary>Outstanding, then FullyPaid, then DepositPaid (exactly the deposit), otherwise PartPaid.</summary>
    public PaymentStatus PaymentStatus
    {
        get
        {
            var paid = AmountPaid;
            if (paid == 0)
            {
                return PaymentStatus.Outstanding;
            }

            if (paid >= TotalPrice)
            {
                return PaymentStatus.FullyPaid;
            }

            return paid == DepositAmount ? PaymentStatus.DepositPaid : PaymentStatus.PartPaid;
        }
    }

    /// <summary>The add-ons on this booking (denormalised for capacity queries).</summary>
    public IReadOnlyList<AddonId> AddonIds => _addonLines.Select(l => l.AddonId).Distinct().ToList();

    /// <summary>The total quantity of an add-on across the booking's lines.</summary>
    public int QuantityOf(AddonId addonId) => _addonLines.Where(l => l.AddonId == addonId).Sum(l => l.Quantity);

    /// <summary>
    /// Non-stand-by Active bookings, and non-stand-by PendingPayment holds that have not expired, block the boat.
    /// </summary>
    public bool BlocksAvailability(DateTimeOffset now) =>
        !IsStandby && (Status == BookingStatus.Active ||
                       (Status == BookingStatus.PendingPayment && HoldExpiresAt is { } expires && expires > now));

    /// <summary>Only Active, non-stand-by bookings ever get customer emails.</summary>
    public bool ReceivesCustomerEmails => Status == BookingStatus.Active && !IsStandby;

    /// <summary>Whether the booking was ever Active (it may since have been cancelled or superseded).</summary>
    public bool HasBeenActive => Status is BookingStatus.Active or BookingStatus.Cancelled or BookingStatus.Superseded;

    // ---- Creation -------------------------------------------------------------------------------------------------

    /// <summary>
    /// A customer started checkout: a PendingPayment hold that blocks the dates until it expires. No customer is
    /// created yet; the details entered are kept on the booking.
    /// </summary>
    public static Booking CreateOnlineHold(BookingReference reference, BookingCustomerDetails customerDetails,
        BookingDraftId bookingDraftId, BoatId boatId, PeriodType periodType, Stay stay, int numberOfAdults,
        int numberOfChildren, decimal hirePrice, decimal depositAmount, PaymentSchedule paymentSchedule,
        IEnumerable<RequestedAddon> addons, DateTimeOffset holdExpiresAt, RoomingWarningAcceptance? roomingWarningAcceptance,
        DateTimeOffset termsAcceptedAt, string paymentLinkTokenHash, DateTimeOffset now, BookingId? id = null)
    {
        if (holdExpiresAt <= now)
        {
            throw new DomainValidationException("holdExpiresAt", "A hold must expire in the future.");
        }

        var (adults, children) = ValidateGuests(numberOfAdults, numberOfChildren);
        var booking = new Booking(id ?? BookingId.New(), 0, reference, BookingCreatedBy.Customer, false,
            BookingStatus.PendingPayment, null, customerDetails, bookingDraftId, boatId, periodType, stay, adults, children,
            Stay.CheckInTime, Stay.CheckOutTime, null, Guard.Money(hirePrice, "hirePrice", "Hire price"),
            Guard.Money(depositAmount, "depositAmount", "Deposit"), paymentSchedule, [], [], holdExpiresAt, null,
            roomingWarningAcceptance, termsAcceptedAt, Guard.Required(paymentLinkTokenHash, "paymentLinkTokenHash", "Payment link"),
            [], now, now);
        foreach (var addon in addons)
        {
            booking._addonLines.Add(AddonLine.Create(addon.AddonId, addon.Name, addon.Quantity, addon.UnitPrice));
        }

        return booking;
    }

    /// <summary>A staff-created booking starts Active (no hold).</summary>
    public static Booking CreateByStaff(BookingReference reference, CustomerId customerId, BoatId boatId,
        PeriodType periodType, Stay stay, int numberOfAdults, int numberOfChildren, TimeOnly checkInTime,
        TimeOnly checkOutTime, bool isStandby, string? comments, decimal hirePrice, decimal depositAmount,
        PaymentSchedule paymentSchedule, string paymentLinkTokenHash, DateTimeOffset now, BookingId? id = null)
    {
        var (adults, children) = ValidateGuests(numberOfAdults, numberOfChildren);
        return new Booking(id ?? BookingId.New(), 0, reference, BookingCreatedBy.SystemUser, isStandby, BookingStatus.Active,
            customerId, null, null, boatId, periodType, stay, adults, children, checkInTime, checkOutTime,
            Guard.Optional(comments, "comments", "Comments"), Guard.Money(hirePrice, "hirePrice", "Hire price"),
            Guard.Money(depositAmount, "depositAmount", "Deposit"), paymentSchedule, [], [], null, null, null, null,
            Guard.Required(paymentLinkTokenHash, "paymentLinkTokenHash", "Payment link"), [], now, now);
    }

    // ---- Status transitions ---------------------------------------------------------------------------------------

    public void AttachCheckoutSession(string sessionId, DateTimeOffset now)
    {
        EnsureStatus(BookingStatus.PendingPayment, "attach a checkout session to");
        StripeCheckoutSessionId = Guard.Required(sessionId, "sessionId", "Checkout session", 500);
        ModifiedDate = now;
    }

    /// <summary>
    /// PendingPayment → Active when the deposit payment succeeds. A deposit that arrives after the hold was already
    /// marked Expired may still activate the booking (Expired → Active) once that payment is recorded, provided
    /// the caller has checked the dates are still free.
    /// </summary>
    public void Activate(DateTimeOffset now)
    {
        var lateDeposit = Status == BookingStatus.Expired && CreatedBy == BookingCreatedBy.Customer && _payments.Count > 0;
        if (!lateDeposit)
        {
            EnsureStatus(BookingStatus.PendingPayment, "activate");
        }

        Status = BookingStatus.Active;
        HoldExpiresAt = null;
        ModifiedDate = now;
    }

    /// <summary>PendingPayment → Expired when the hold lapses or the checkout session expires.</summary>
    public void Expire(DateTimeOffset now)
    {
        EnsureStatus(BookingStatus.PendingPayment, "expire");
        Status = BookingStatus.Expired;
        HoldExpiresAt = null;
        ModifiedDate = now;
    }

    /// <summary>Active → Cancelled (staff). Refunds are made in the Stripe dashboard.</summary>
    public void Cancel(DateTimeOffset now)
    {
        EnsureStatus(BookingStatus.Active, "cancel");
        Status = BookingStatus.Cancelled;
        ModifiedDate = now;
    }

    /// <summary>Cancelled → Active (staff). The caller must run the availability check.</summary>
    public void Reinstate(DateTimeOffset now)
    {
        EnsureStatus(BookingStatus.Cancelled, "reinstate");
        Status = BookingStatus.Active;
        ModifiedDate = now;
    }

    /// <summary>Active stand-by → Superseded, when an Active non-stand-by booking overlaps it.</summary>
    public void Supersede(DateTimeOffset now)
    {
        if (!IsStandby)
        {
            throw new InvalidStateTransitionException("Only stand-by bookings can be superseded.");
        }

        EnsureStatus(BookingStatus.Active, "supersede");
        Status = BookingStatus.Superseded;
        ModifiedDate = now;
    }

    // ---- Staff edits ----------------------------------------------------------------------------------------------

    public bool IsEditableByStaff => Status is BookingStatus.Active or BookingStatus.Cancelled;

    /// <summary>
    /// Staff change the boat, dates, guests, times, stand-by flag or hire price. Changing dates does not re-price;
    /// the payment schedule is re-derived by the caller and passed in.
    /// </summary>
    public void ChangeDetails(BoatId boatId, PeriodType periodType, Stay stay, int numberOfAdults, int numberOfChildren,
        TimeOnly checkInTime, TimeOnly checkOutTime, bool isStandby, string? comments, decimal hirePrice,
        PaymentSchedule paymentSchedule, DateTimeOffset now)
    {
        if (!IsEditableByStaff)
        {
            throw new InvalidStateTransitionException($"A {Status} booking cannot be edited.");
        }

        (NumberOfAdults, NumberOfChildren) = ValidateGuests(numberOfAdults, numberOfChildren);
        BoatId = boatId;
        PeriodType = periodType;
        Stay = stay;
        CheckInTime = checkInTime;
        CheckOutTime = checkOutTime;
        IsStandby = isStandby;
        Comments = Guard.Optional(comments, "comments", "Comments");
        HirePrice = Guard.Money(hirePrice, "hirePrice", "Hire price");
        PaymentSchedule = paymentSchedule;
        ModifiedDate = now;
    }

    public void UpdateComments(string? comments, DateTimeOffset now)
    {
        Comments = Guard.Optional(comments, "comments", "Comments");
        ModifiedDate = now;
    }

    public void SetHirePrice(decimal hirePrice, DateTimeOffset now)
    {
        if (!IsEditableByStaff)
        {
            throw new InvalidStateTransitionException($"A {Status} booking cannot be re-priced.");
        }

        HirePrice = Guard.Money(hirePrice, "hirePrice", "Hire price");
        ModifiedDate = now;
    }

    /// <summary>Links the booking to a customer: by staff, or when an online booking becomes Active.</summary>
    public void ChangeCustomer(CustomerId customerId, DateTimeOffset now)
    {
        if (CustomerId == customerId)
        {
            return;
        }

        CustomerId = customerId;
        ModifiedDate = now;
    }

    // ---- Add-ons --------------------------------------------------------------------------------------------------

    public AddonLine AddAddon(RequestedAddon addon, DateTimeOffset now)
    {
        var line = AddonLine.Create(addon.AddonId, addon.Name, addon.Quantity, addon.UnitPrice);
        _addonLines.Add(line);
        ModifiedDate = now;
        return line;
    }

    public AddonLine UpdateAddonLine(string lineId, int quantity, decimal? unitPrice, DateTimeOffset now)
    {
        var line = FindAddonLine(lineId);
        line.Update(quantity, unitPrice);
        ModifiedDate = now;
        return line;
    }

    /// <summary>Staff decline an add-on by removing its line.</summary>
    public void RemoveAddonLine(string lineId, DateTimeOffset now)
    {
        _addonLines.Remove(FindAddonLine(lineId));
        ModifiedDate = now;
    }

    // ---- Payments -------------------------------------------------------------------------------------------------

    /// <summary>Records a Stripe payment. Idempotent on the PaymentIntent id; returns null for a replay.</summary>
    public Payment? RecordStripePayment(decimal amount, string paymentIntentId, DateTimeOffset paidAt, DateTimeOffset now)
    {
        var intentId = Guard.Required(paymentIntentId, "paymentIntentId", "PaymentIntent", 500);
        if (_payments.Any(p => p.StripePaymentIntentId == intentId))
        {
            return null;
        }

        var payment = new Payment(IdGenerator.NewId(), amount, PaymentMethod.Stripe, intentId, paidAt, null,
            PaymentRecordedBy.Customer);
        _payments.Add(payment);
        ModifiedDate = now;
        return payment;
    }

    /// <summary>Records a payment staff received by other means (bank transfer, cash, phone card payment).</summary>
    public Payment RecordManualPayment(decimal amount, DateTimeOffset paidAt, string? note, DateTimeOffset now)
    {
        var payment = new Payment(IdGenerator.NewId(), amount, PaymentMethod.Manual, null, paidAt, note,
            PaymentRecordedBy.SystemUser);
        _payments.Add(payment);
        ModifiedDate = now;
        return payment;
    }

    // ---- Notifications --------------------------------------------------------------------------------------------

    public bool HasSentNotification(string key) => _sentNotifications.Any(n => n.Key == key);

    public void RecordNotificationSent(string key, DateTimeOffset sentAt)
    {
        if (!HasSentNotification(key))
        {
            _sentNotifications.Add(new SentNotification(key, sentAt));
        }
    }

    // ---- Persistence ----------------------------------------------------------------------------------------------

    public BookingSnapshot ToSnapshot() => new(
        Id.Value, Version, Reference.Value, CreatedBy, IsStandby, Status, CustomerId?.Value,
        CustomerDetails is { } d ? new BookingCustomerDetailsSnapshot(d.FullName, d.Email, d.MobileNumber) : null,
        BookingDraftId?.Value, BoatId.Value, PeriodType, StartDate, EndDate, NumberOfAdults, NumberOfChildren, CheckInTime,
        CheckOutTime, Comments, HirePrice, DepositAmount, PaymentSchedule,
        _addonLines.Select(l => new AddonLineSnapshot(l.Id, l.AddonId.Value, l.NameSnapshot, l.Quantity, l.UnitPrice)).ToList(),
        _payments.Select(p => new PaymentSnapshot(p.Id, p.Amount, p.Method, p.StripePaymentIntentId, p.PaidAt, p.Note, p.RecordedBy)).ToList(),
        HoldExpiresAt, StripeCheckoutSessionId, RoomingWarningAcceptance?.AcceptedAt,
        RoomingWarningAcceptance?.WarningTextShown, TermsAcceptedAt, PaymentLinkTokenHash, _sentNotifications.ToList(),
        CreatedDate, ModifiedDate);

    public static Booking FromSnapshot(BookingSnapshot s) => new(
        new BookingId(s.Id), s.Version, BookingReference.Parse(s.Reference), s.CreatedBy, s.IsStandby, s.Status,
        string.IsNullOrEmpty(s.CustomerId) ? null : new CustomerId(s.CustomerId),
        s.CustomerDetails is { } d ? BookingCustomerDetails.Restore(d.FullName, d.Email, d.MobileNumber) : null,
        string.IsNullOrEmpty(s.BookingDraftId) ? null : new BookingDraftId(s.BookingDraftId),
        new BoatId(s.BoatId), s.PeriodType, new Stay(s.StartDate, s.EndDate), s.NumberOfAdults, s.NumberOfChildren,
        s.CheckInTime, s.CheckOutTime, s.Comments, s.HirePrice, s.DepositAmount, s.PaymentSchedule,
        s.AddonLines.Select(l => new AddonLine(l.Id, new AddonId(l.AddonId), l.NameSnapshot, l.Quantity, l.UnitPrice)),
        s.Payments.Select(p => new Payment(p.Id, p.Amount, p.Method, p.StripePaymentIntentId, p.PaidAt, p.Note, p.RecordedBy)),
        s.HoldExpiresAt, s.StripeCheckoutSessionId,
        s.RoomingWarningAcceptedAt is { } at && s.RoomingWarningText is { } text ? new RoomingWarningAcceptance(at, text) : null,
        s.TermsAcceptedAt, s.PaymentLinkTokenHash, s.SentNotifications, s.CreatedDate, s.ModifiedDate);

    private AddonLine FindAddonLine(string lineId) =>
        _addonLines.FirstOrDefault(l => l.Id == lineId)
        ?? throw new DomainValidationException("lineId", "That add-on line does not exist on this booking.");

    private void EnsureStatus(BookingStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new InvalidStateTransitionException($"Cannot {action} a booking that is {Status}.");
        }
    }

    private static (int Adults, int Children) ValidateGuests(int numberOfAdults, int numberOfChildren)
    {
        if (numberOfAdults < 1)
        {
            throw new DomainValidationException("numberOfAdults", "There must be at least one adult.");
        }

        if (numberOfChildren < 0)
        {
            throw new DomainValidationException("numberOfChildren", "Number of children cannot be negative.");
        }

        return (numberOfAdults, numberOfChildren);
    }
}

/// <summary>An add-on requested at booking time, with its unit price (null when staff set it later).</summary>
public sealed record RequestedAddon(AddonId AddonId, string Name, int Quantity, decimal? UnitPrice);

public sealed record AddonLineSnapshot(
    string Id,
    string AddonId,
    string NameSnapshot,
    int Quantity,
    decimal? UnitPrice);

public sealed record PaymentSnapshot(
    string Id,
    decimal Amount,
    PaymentMethod Method,
    string? StripePaymentIntentId,
    DateTimeOffset PaidAt,
    string? Note,
    PaymentRecordedBy RecordedBy);

public sealed record BookingCustomerDetailsSnapshot(string FullName, string Email, string MobileNumber);

public sealed record BookingSnapshot(
    string Id,
    int Version,
    string Reference,
    BookingCreatedBy CreatedBy,
    bool IsStandby,
    BookingStatus Status,
    string? CustomerId,
    BookingCustomerDetailsSnapshot? CustomerDetails,
    string? BookingDraftId,
    string BoatId,
    PeriodType PeriodType,
    DateOnly StartDate,
    DateOnly EndDate,
    int NumberOfAdults,
    int NumberOfChildren,
    TimeOnly CheckInTime,
    TimeOnly CheckOutTime,
    string? Comments,
    decimal HirePrice,
    decimal DepositAmount,
    PaymentSchedule PaymentSchedule,
    IReadOnlyList<AddonLineSnapshot> AddonLines,
    IReadOnlyList<PaymentSnapshot> Payments,
    DateTimeOffset? HoldExpiresAt,
    string? StripeCheckoutSessionId,
    DateTimeOffset? RoomingWarningAcceptedAt,
    string? RoomingWarningText,
    DateTimeOffset? TermsAcceptedAt,
    string PaymentLinkTokenHash,
    IReadOnlyList<SentNotification> SentNotifications,
    DateTimeOffset CreatedDate,
    DateTimeOffset ModifiedDate);
