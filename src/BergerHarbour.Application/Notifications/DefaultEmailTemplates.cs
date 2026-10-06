using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Notifications;

/// <summary>The templates seeded at setup. Staff can edit them in Settings → Email templates.</summary>
public static class DefaultEmailTemplates
{
    private const string Footer = """
        <p>Kind regards,<br>The team at Berger Houseboats<br>{{ContactPhone}} · <a href="mailto:{{ContactEmail}}">{{ContactEmail}}</a></p>
        """;

    private const string BookingSummary = """
        <table cellpadding="4" style="border-collapse:collapse">
          <tr><td><strong>Booking reference</strong></td><td>{{BookingReference}}</td></tr>
          <tr><td><strong>Boat</strong></td><td>{{BoatName}}</td></tr>
          <tr><td><strong>Check-in</strong></td><td>{{CheckInDate}} from {{CheckInTime}}</td></tr>
          <tr><td><strong>Check-out</strong></td><td>{{CheckOutDate}} by {{CheckOutTime}}</td></tr>
          <tr><td><strong>Guests</strong></td><td>{{NumberOfGuests}}</td></tr>
        </table>
        """;

    public static IReadOnlyList<EmailTemplate> All() =>
    [
        EmailTemplate.Create(EmailTemplateKey.BookingConfirmed,
            "Your Berger Houseboats booking {{BookingReference}} is confirmed",
            $$$"""
            <p>Hi {{CustomerName}},</p>
            <p>Thank you for booking with Berger Houseboats. Your booking is confirmed.</p>
            {{{BookingSummary}}}
            <h3>Payments</h3>
            <p>Hire price: {{HirePrice}}<br>Total: {{TotalPrice}}<br>Paid so far: {{AmountPaid}}<br>Still owing: {{AmountOwing}}</p>
            <p>{{PaymentSchedule}}</p>
            <p>We will email you a payment link before each payment is due.</p>
            <h3>Requested add-ons</h3>
            <p>{{RequestedAddons}}</p>
            <p>Add-ons are requests and will be confirmed by our staff. Confirmed add-ons are added to your balance.</p>
            <h3>Security bond</h3>
            <p>A {{SecurityBond}} security bond applies to this hire. We will send you the details before your trip.</p>
            <p>Please read our <a href="{{HireTermsUrl}}">Hire Terms and Procedures</a>.</p>
            {{{Footer}}}
            """),
        EmailTemplate.Create(EmailTemplateKey.StaffNewOnlineBooking,
            "New online booking {{BookingReference}} — {{BoatName}} {{CheckInDate}}",
            $$$"""
            <p>A new online booking has been paid and confirmed.</p>
            <p><strong>Customer:</strong> {{CustomerName}}</p>
            {{{BookingSummary}}}
            <p>Total: {{TotalPrice}} · Paid: {{AmountPaid}} · Owing: {{AmountOwing}}</p>
            <p><strong>Requested add-ons (to confirm or decline):</strong><br>{{RequestedAddons}}</p>
            """),
        EmailTemplate.Create(EmailTemplateKey.PaymentDue,
            "Payment due for your Berger Houseboats booking {{BookingReference}}",
            $$$"""
            <p>Hi {{CustomerName}},</p>
            <p>A payment of <strong>{{AmountDue}}</strong> is due by <strong>{{DueDate}}</strong> for your booking on {{BoatName}}.</p>
            {{{BookingSummary}}}
            <p>Total: {{TotalPrice}} · Paid so far: {{AmountPaid}} · Still owing: {{AmountOwing}}</p>
            <p>{{PaymentSchedule}}</p>
            <p><a href="{{PaymentLink}}" style="display:inline-block;padding:10px 18px;background:#0b5c7a;color:#ffffff;text-decoration:none;border-radius:4px">Pay now</a></p>
            <p>If the button does not work, copy this link into your browser:<br>{{PaymentLink}}</p>
            {{{Footer}}}
            """),
        EmailTemplate.Create(EmailTemplateKey.PaymentReceived,
            "Payment received for booking {{BookingReference}}",
            $$$"""
            <p>Hi {{CustomerName}},</p>
            <p>Thank you — we have received your payment for booking {{BookingReference}} on {{BoatName}}.</p>
            <p>Total: {{TotalPrice}}<br>Paid so far: {{AmountPaid}}<br>Still owing: {{AmountOwing}}</p>
            <p>{{PaymentSchedule}}</p>
            {{{Footer}}}
            """),
        EmailTemplate.Create(EmailTemplateKey.PreHireInstructions,
            "Getting ready for your houseboat holiday — {{BookingReference}}",
            $$$"""
            <p>Hi {{CustomerName}},</p>
            <p>Your houseboat holiday on <strong>{{BoatName}}</strong> is coming up soon.</p>
            {{{BookingSummary}}}
            <h3>Before you arrive</h3>
            <ul>
              <li>Check-in is from {{CheckInTime}} on {{CheckInDate}}. Please allow time for the boat handover and driving instructions.</li>
              <li>Check-out is by {{CheckOutTime}} on {{CheckOutDate}}.</li>
              <li>A {{SecurityBond}} security bond is payable on arrival.</li>
              <li>Bring food, drinks, linen and towels unless arranged otherwise, plus warm clothing and sun protection.</li>
              <li>The licensed driver must bring their driver's licence.</li>
            </ul>
            <p>Amount still owing: {{AmountOwing}}.</p>
            <p>Please re-read our <a href="{{HireTermsUrl}}">Hire Terms and Procedures</a> before your trip.</p>
            {{{Footer}}}
            """),
        EmailTemplate.Create(EmailTemplateKey.StaffBalanceOverdue,
            "Overdue balance: {{BookingReference}} — {{CustomerName}}",
            $$$"""
            <p>A payment for this booking is overdue. The booking has <strong>not</strong> been cancelled; please decide what to do.</p>
            <p><strong>Customer:</strong> {{CustomerName}}</p>
            {{{BookingSummary}}}
            <p>Due date: {{DueDate}} · Amount due: {{AmountDue}}</p>
            <p>Total: {{TotalPrice}} · Paid: {{AmountPaid}} · Owing: {{AmountOwing}}</p>
            """),
        EmailTemplate.Create(EmailTemplateKey.StaffStandbySuperseded,
            "Stand-by booking {{BookingReference}} superseded",
            $$$"""
            <p>The stand-by booking below has been superseded by a new booking on the same boat and dates.</p>
            <p><strong>Customer:</strong> {{CustomerName}}</p>
            {{{BookingSummary}}}
            <p>Paid so far: {{AmountPaid}}. Please contact the customer to arrange new dates.</p>
            """),
    ];
}
