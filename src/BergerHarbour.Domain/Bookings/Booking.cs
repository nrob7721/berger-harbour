using BergerHarbour.Domain.BookingDrafts;
using BergerHarbour.Domain.Boats;
using BergerHarbour.Domain.Customers;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>A house boat booking: an online hold that becomes Active when its deposit is paid, or a staff booking.</summary>
public sealed class Booking : AggregateRoot
{
    private List<AddonLine> addonLines = [];
    private List<Payment> payments = [];
    private List<SentNotification> sentNotifications = [];

    private Booking(BookingId id, BoatId boatId, DateTimeOffset createdDate, string paymentLinkTokenHash)
    {
        Id = id;
        BoatId = boatId;
        CreatedDate = createdDate;
        ModifiedDate = createdDate;
        PaymentLinkTokenHash = paymentLinkTokenHash;
        CheckInTime = Sydney.StandardCheckIn;
        CheckOutTime = Sydney.StandardCheckOut;
    }

    public BookingId Id { get; }

    public BookingReference? Reference { get; private set; }

    public BookingCreator CreatedBy { get; private set; }

    public bool IsStandby { get; private set; }

    public BookingStatus Status { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public CustomerId? CustomerId { get; private set; }

    public CustomerDetails? CustomerDetails { get; private set; }

    public BookingDraftId? BookingDraftId { get; private set; }

    public BoatId BoatId { get; private set; }

    public PeriodType PeriodType { get; private set; }

    public StayRange Stay { get; private set; }

    public int NumberOfAdults { get; private set; }

    public int NumberOfChildren { get; private set; }

    public TimeOnly CheckInTime { get; private set; }

    public TimeOnly CheckOutTime { get; private set; }

    public string? BookingComments { get; private set; }

    public decimal HirePrice { get; private set; }

    public decimal DepositAmount { get; private set; }

    public PaymentScheduleType PaymentSchedule { get; private set; }

    public IReadOnlyList<AddonLine> AddonLines => addonLines;

    public IReadOnlyList<Payment> Payments => payments;

    public DateTimeOffset? HoldExpiresAt { get; private set; }

    public string? StripeCheckoutSessionId { get; private set; }

    public RoomingWarningAcceptance? RoomingWarningAcceptance { get; private set; }

    public DateTimeOffset? TermsAcceptedAt { get; private set; }

    /// <summary>SHA-256 of the payment link token.</summary>
    public string PaymentLinkTokenHash { get; }

    public IReadOnlyList<SentNotification> SentNotifications => sentNotifications;

    public DateTimeOffset CreatedDate { get; }

    public DateTimeOffset ModifiedDate { get; private set; }

    public DateOnly StartDate => Stay.Start;

    public DateOnly EndDate => Stay.End;

    public int NumberOfGuests => NumberOfAdults + NumberOfChildren;

    /// <summary>Hire price plus every add-on line that has a price; unpriced lines are excluded until staff price them.</summary>
    public decimal TotalPrice => HirePrice + addonLines.Sum(l => l.LineTotal ?? 0m);

    public decimal AmountPaid => payments.Sum(p => p.Amount);

    public decimal AmountOwing => Math.Max(0m, TotalPrice - AmountPaid);

    public PaymentStatus PaymentStatus =>
        AmountPaid == 0m ? PaymentStatus.Outstanding
        : AmountPaid >= TotalPrice ? PaymentStatus.FullyPaid
        : AmountPaid == DepositAmount ? PaymentStatus.DepositPaid
        : PaymentStatus.PartPaid;

    /// <summary>Is the booking one that sends customer emails (Active and not a stand-by)?</summary>
    public bool IsNotifiable => Status == BookingStatus.Active && !IsStandby;

    /// <summary>A PendingPayment hold that the customer has started paying for.</summary>
    public bool IsHold => Status == BookingStatus.PendingPayment;

    /// <summary>Starts a PendingPayment hold. No customer is created and no reference is assigned yet.</summary>
    public static Booking CreateOnlineHold(BookingId id, OnlineHold hold, DateTimeOffset now)
    {
        var booking = new Booking(id, hold.BoatId, now, hold.PaymentLinkTokenHash)
        {
            CreatedBy = BookingCreator.Customer,
            Status = BookingStatus.PendingPayment,
            BookingDraftId = hold.DraftId,
            CustomerDetails = hold.CustomerDetails,
            PeriodType = hold.PeriodType,
            Stay = hold.Stay,
            HirePrice = hold.HirePrice,
            DepositAmount = hold.DepositAmount,
            PaymentSchedule = hold.PaymentSchedule,
            RoomingWarningAcceptance = hold.RoomingWarningAcceptance,
            TermsAcceptedAt = hold.TermsAcceptedAt,
            HoldExpiresAt = hold.HoldExpiresAt,
            addonLines = hold.AddonLines.ToList(),
        };
        booking.SetGuests(hold.NumberOfAdults, hold.NumberOfChildren);
        return booking;
    }

    /// <summary>A staff booking: Active immediately, with its reference and ActivatedAt set at creation.</summary>
    public static Booking CreateByStaff(
        BookingId id, BookingReference reference, CustomerId customerId, StaffBookingDetails details, decimal depositAmount,
        string paymentLinkTokenHash, DateTimeOffset now)
    {
        var booking = new Booking(id, details.BoatId, now, paymentLinkTokenHash)
        {
            CreatedBy = BookingCreator.SystemUser,
            Status = BookingStatus.Active,
            Reference = reference,
            ActivatedAt = now,
            CustomerId = customerId,
            DepositAmount = depositAmount,
        };
        booking.Apply(details);
        return booking;
    }

    public static Booking Rehydrate(BookingState s)
    {
        var booking = new Booking(s.Id, s.BoatId, s.CreatedDate, s.PaymentLinkTokenHash)
        {
            Reference = s.Reference,
            CreatedBy = s.CreatedBy,
            IsStandby = s.IsStandby,
            Status = s.Status,
            ActivatedAt = s.ActivatedAt,
            CustomerId = s.CustomerId,
            CustomerDetails = s.CustomerDetails,
            BookingDraftId = s.BookingDraftId,
            PeriodType = s.PeriodType,
            Stay = s.Stay,
            NumberOfAdults = s.NumberOfAdults,
            NumberOfChildren = s.NumberOfChildren,
            CheckInTime = s.CheckInTime,
            CheckOutTime = s.CheckOutTime,
            BookingComments = s.BookingComments,
            HirePrice = s.HirePrice,
            DepositAmount = s.DepositAmount,
            PaymentSchedule = s.PaymentSchedule,
            addonLines = s.AddonLines.ToList(),
            payments = s.Payments.ToList(),
            HoldExpiresAt = s.HoldExpiresAt,
            StripeCheckoutSessionId = s.StripeCheckoutSessionId,
            RoomingWarningAcceptance = s.RoomingWarningAcceptance,
            TermsAcceptedAt = s.TermsAcceptedAt,
            sentNotifications = s.SentNotifications.ToList(),
            ModifiedDate = s.ModifiedDate,
        };
        booking.SetPersistedVersion(s.Version);
        return booking;
    }

    /// <summary>
    /// Does this booking block its boat's dates? Non-stand-by Active bookings, and non-stand-by holds that haven't expired.
    /// </summary>
    public bool BlocksAvailability(DateTimeOffset now) =>
        !IsStandby && (Status == BookingStatus.Active || (Status == BookingStatus.PendingPayment && HoldExpiresAt > now));

    public bool HasSentNotification(string key) => sentNotifications.Any(n => n.Key == key);

    public void RecordNotificationSent(string key, DateTimeOffset at)
    {
        if (!HasSentNotification(key))
        {
            sentNotifications.Add(new SentNotification(key, at));
        }
    }

    public void AttachCheckoutSession(string sessionId)
    {
        StripeCheckoutSessionId = sessionId;
    }

    /// <summary>Records a payment. Idempotent on the Stripe PaymentIntent id: returns false if it was already recorded.</summary>
    public bool RecordPayment(Payment payment, DateTimeOffset now)
    {
        if (payment.Amount <= 0)
        {
            throw new DomainValidationException("amount", "The amount must be more than $0.");
        }

        if (payment.StripePaymentIntentId is not null && payments.Any(p => p.StripePaymentIntentId == payment.StripePaymentIntentId))
        {
            return false;
        }

        payments.Add(payment);
        ModifiedDate = now;
        return true;
    }

    /// <summary>Assigns the reference once; it never changes afterwards.</summary>
    public void AssignReference(BookingReference reference)
    {
        Reference ??= reference;
    }

    /// <summary>
    /// The deposit was paid: PendingPayment (or a hold that lapsed while its dates stayed free) → Active. Sets ActivatedAt.
    /// </summary>
    public void ActivateFromDeposit(DateTimeOffset now)
    {
        if (Status is not (BookingStatus.PendingPayment or BookingStatus.Expired))
        {
            throw new InvalidOperationException($"A {Status} booking can't be activated from a deposit.");
        }

        if (Reference is null)
        {
            throw new InvalidOperationException("A booking needs its reference before it becomes Active.");
        }

        Status = BookingStatus.Active;
        ActivatedAt ??= now;
        HoldExpiresAt = null;
        ModifiedDate = now;
    }

    /// <summary>Links the customer created or updated from <see cref="CustomerDetails"/> on activation.</summary>
    public void AttachCustomer(CustomerId customerId, DateTimeOffset now)
    {
        CustomerId = customerId;
        ModifiedDate = now;
    }

    /// <summary>PendingPayment → Expired (hold lapsed, Stripe session expired, or released from the Payment step).</summary>
    public void Expire(DateTimeOffset now)
    {
        if (Status == BookingStatus.Expired)
        {
            return;
        }

        if (Status != BookingStatus.PendingPayment)
        {
            throw new InvalidOperationException($"A {Status} booking can't expire.");
        }

        Status = BookingStatus.Expired;
        ModifiedDate = now;
    }

    /// <summary>Active → Cancelled (staff). Refunds are made in the Stripe dashboard.</summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status != BookingStatus.Active)
        {
            throw new DomainValidationException("status", "Only an Active booking can be cancelled.");
        }

        Status = BookingStatus.Cancelled;
        ModifiedDate = now;
    }

    /// <summary>Cancelled → Active (staff). The caller checks availability first.</summary>
    public void Reactivate(DateTimeOffset now)
    {
        if (Status != BookingStatus.Cancelled)
        {
            throw new DomainValidationException("status", "Only a Cancelled booking can be made Active again.");
        }

        Status = BookingStatus.Active;
        ModifiedDate = now;
    }

    /// <summary>Applies a status chosen by staff: only Active ↔ Cancelled can be chosen.</summary>
    public void ChangeStatusByStaff(BookingStatus status, DateTimeOffset now)
    {
        if (status == Status)
        {
            return;
        }

        switch (status)
        {
            case BookingStatus.Cancelled:
                Cancel(now);
                break;
            case BookingStatus.Active:
                Reactivate(now);
                break;
            default:
                throw new DomainValidationException("status", "Staff can only set a booking to Active or Cancelled.");
        }
    }

    /// <summary>Active stand-by → Superseded, when an Active non-stand-by booking overlaps it.</summary>
    public void Supersede(DateTimeOffset now)
    {
        if (!IsStandby || Status != BookingStatus.Active)
        {
            throw new InvalidOperationException("Only an Active stand-by booking can be superseded.");
        }

        Status = BookingStatus.Superseded;
        ModifiedDate = now;
    }

    /// <summary>Staff edits from the booking dialog. Prices are accepted as typed; totals are derived.</summary>
    public void UpdateByStaff(StaffBookingDetails details, DateTimeOffset now)
    {
        Apply(details);
        ModifiedDate = now;
    }

    private void Apply(StaffBookingDetails details)
    {
        if (details.HirePrice < 0)
        {
            throw new DomainValidationException("hirePrice", "Enter the hire price.");
        }

        SetGuests(details.NumberOfAdults, details.NumberOfChildren);
        BoatId = details.BoatId;
        PeriodType = details.PeriodType;
        Stay = details.Stay;
        CheckInTime = details.CheckInTime;
        CheckOutTime = details.CheckOutTime;
        IsStandby = details.IsStandby;
        BookingComments = string.IsNullOrWhiteSpace(details.BookingComments) ? null : details.BookingComments.Trim();
        HirePrice = details.HirePrice;
        PaymentSchedule = details.PaymentSchedule;
        addonLines = details.AddonLines.ToList();
    }

    private void SetGuests(int adults, int children)
    {
        var errors = new ValidationErrors();
        if (adults < 1)
        {
            errors.Add("numberOfAdults", "At least 1 adult is required.");
        }

        if (children < 0)
        {
            errors.Add("numberOfChildren", "Children can't be negative.");
        }

        errors.ThrowIfAny();
        NumberOfAdults = adults;
        NumberOfChildren = children;
    }
}
