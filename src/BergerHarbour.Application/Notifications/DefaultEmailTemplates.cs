using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Notifications;

/// <summary>Seeded defaults. Staff edit them in Settings.</summary>
public static class DefaultEmailTemplates
{
    private const string Sign = "<p>Kind regards,<br>The team at Berger Houseboats<br>{{ContactPhone}} · {{ContactEmail}}</p>";

    public static readonly IReadOnlyDictionary<EmailTemplateKey, (string Subject, string Body)> All = new Dictionary<EmailTemplateKey, (string, string)>
    {
        [EmailTemplateKey.BookingConfirmed] = (
            "Your house boat booking {{BookingReference}} is confirmed",
            """
            <p>Dear {{CustomerFirstName}},</p>
            <p>Thank you for booking with Berger Houseboats. Your booking is confirmed.</p>
            <table cellpadding="4">
              <tr><td><strong>Booking reference</strong></td><td>{{BookingReference}}</td></tr>
              <tr><td><strong>Boat</strong></td><td>{{BoatName}}</td></tr>
              <tr><td><strong>Check-in</strong></td><td>{{CheckInDate}}, {{CheckInTime}}</td></tr>
              <tr><td><strong>Check-out</strong></td><td>{{CheckOutDate}}, from {{CheckOutTime}}</td></tr>
              <tr><td><strong>Guests</strong></td><td>{{NumberOfAdults}} adults, {{NumberOfChildren}} children</td></tr>
              <tr><td><strong>Hire price</strong></td><td>{{HirePrice}}</td></tr>
              <tr><td><strong>Total so far</strong></td><td>{{TotalPrice}}</td></tr>
              <tr><td><strong>Paid</strong></td><td>{{AmountPaid}}</td></tr>
            </table>
            <h3>Payment schedule</h3>
            {{PaymentSchedule}}
            <p>You can pay your balance at any time here: <a href="{{PaymentLink}}">{{PaymentLink}}</a>. We'll also send you a reminder before each payment is due.</p>
            <h3>Requested add-ons</h3>
            {{RequestedAddons}}
            <p>Add-ons are requests and are subject to confirmation by our staff. Confirmed add-ons are added to your balance.</p>
            <p>A security bond of {{SecurityBond}} applies to your hire — we'll send you the details before your trip.</p>
            <p>Please read our <a href="{{HireTermsUrl}}">Hire Terms and Procedures</a>.</p>
            <p>If you need to change or cancel your booking, please call or email us.</p>
            """ + Sign),

        [EmailTemplateKey.StaffNewOnlineBooking] = (
            "New online booking {{BookingReference}} – {{BoatName}} {{CheckInDate}}",
            """
            <p>A new online booking has been paid.</p>
            <p>{{FrequentFloaterNotice}}</p>
            <table cellpadding="4">
              <tr><td><strong>Reference</strong></td><td>{{BookingReference}}</td></tr>
              <tr><td><strong>Customer</strong></td><td>{{CustomerName}} · {{CustomerEmail}} · {{CustomerMobile}}</td></tr>
              <tr><td><strong>Boat</strong></td><td>{{BoatName}}</td></tr>
              <tr><td><strong>Dates</strong></td><td>{{CheckInDate}} → {{CheckOutDate}}</td></tr>
              <tr><td><strong>Guests</strong></td><td>{{NumberOfAdults}} adults, {{NumberOfChildren}} children</td></tr>
              <tr><td><strong>Total</strong></td><td>{{TotalPrice}} (paid {{AmountPaid}})</td></tr>
            </table>
            <h3>Requested add-ons</h3>
            {{RequestedAddons}}
            <p>Please confirm or remove the requested add-ons in the admin app, and contact the customer if needed.</p>
            """),

        [EmailTemplateKey.PaymentDue] = (
            "Payment due for your house boat booking {{BookingReference}}",
            """
            <p>Dear {{CustomerFirstName}},</p>
            <p>A payment of <strong>{{AmountDue}}</strong> is due by <strong>{{DueDate}}</strong> for your booking of {{BoatName}} from {{CheckInDate}} to {{CheckOutDate}}.</p>
            <p><a href="{{PaymentLink}}">Pay now</a></p>
            <p>Total: {{TotalPrice}} · Paid: {{AmountPaid}} · Owing: {{AmountOwing}}</p>
            {{PaymentSchedule}}
            <p>If you've already paid, thank you — please ignore this email.</p>
            """ + Sign),

        [EmailTemplateKey.PaymentReceived] = (
            "Payment received – booking {{BookingReference}}",
            """
            <p>Dear {{CustomerFirstName}},</p>
            <p>Thank you, we've received your payment for booking {{BookingReference}} ({{BoatName}}, {{CheckInDate}} to {{CheckOutDate}}).</p>
            <p>Total: {{TotalPrice}} · Paid: {{AmountPaid}} · Owing: {{AmountOwing}}</p>
            <p>You can pay any remaining balance here: <a href="{{PaymentLink}}">{{PaymentLink}}</a>.</p>
            """ + Sign),

        [EmailTemplateKey.PreHireInstructions] = (
            "Getting ready for your house boat trip on {{CheckInDate}}",
            """
            <p>Dear {{CustomerFirstName}},</p>
            <p>Your trip on {{BoatName}} is almost here!</p>
            <ul>
              <li><strong>Check-in:</strong> {{CheckInDate}}, {{CheckInTime}}</li>
              <li><strong>Check-out:</strong> {{CheckOutDate}}, from {{CheckOutTime}}</li>
              <li><strong>Security bond:</strong> {{SecurityBond}}</li>
            </ul>
            <p>Please bring photo ID for the skipper, your own linen and towels (unless arranged), food and drinks. Allow time for a short handover and safety briefing on arrival.</p>
            <p>Balance owing: {{AmountOwing}}. <a href="{{PaymentLink}}">Pay now</a></p>
            <p>Please read our <a href="{{HireTermsUrl}}">Hire Terms and Procedures</a> before you arrive.</p>
            """ + Sign),

        [EmailTemplateKey.StaffBalanceOverdue] = (
            "Overdue payment – {{BookingReference}} ({{CustomerName}})",
            """
            <p>A payment is overdue.</p>
            <ul>
              <li><strong>Reference:</strong> {{BookingReference}}</li>
              <li><strong>Customer:</strong> {{CustomerName}} · {{CustomerEmail}} · {{CustomerMobile}}</li>
              <li><strong>Boat:</strong> {{BoatName}}, {{CheckInDate}} → {{CheckOutDate}}</li>
              <li><strong>Due:</strong> {{AmountDue}} by {{DueDate}}</li>
              <li><strong>Total / paid / owing:</strong> {{TotalPrice}} / {{AmountPaid}} / {{AmountOwing}}</li>
            </ul>
            <p>The booking has not been cancelled. Please follow up with the customer and decide what to do.</p>
            """),

        [EmailTemplateKey.StaffStandbySuperseded] = (
            "Stand-by booking superseded – {{BookingReference}}",
            """
            <p>The stand-by booking <strong>{{BookingReference}}</strong> for {{CustomerName}} ({{CustomerEmail}}, {{CustomerMobile}}) on {{BoatName}}, {{CheckInDate}} → {{CheckOutDate}}, has been superseded by another booking for the same dates.</p>
            <p>Please contact the customer to arrange other dates.</p>
            """),

        [EmailTemplateKey.StaffAbandonedBooking] = (
            "Unfinished online booking – {{CustomerName}}",
            """
            <p>A customer started an online booking but didn't finish it. They may need help.</p>
            <ul>
              <li><strong>Customer:</strong> {{CustomerName}}</li>
              <li><strong>Email:</strong> {{CustomerEmail}}</li>
              <li><strong>Mobile:</strong> {{CustomerMobile}}</li>
              <li><strong>Boat:</strong> {{BoatName}}</li>
              <li><strong>Dates:</strong> {{CheckInDate}} → {{CheckOutDate}}</li>
              <li><strong>Guests:</strong> {{NumberOfAdults}} adults, {{NumberOfChildren}} children</li>
              <li><strong>Last step reached:</strong> {{LastStepReached}}</li>
            </ul>
            <h3>Requested add-ons</h3>
            {{RequestedAddons}}
            """),
    };
}
