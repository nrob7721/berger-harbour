using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Notifications;

/// <summary>The default wording of every email, seeded at setup and editable in Settings. Payment emails use payment links.</summary>
public static class DefaultEmailTemplates
{
    private const string Signature =
        "<p>Kind regards,<br>The team at Berger Houseboats<br>{{ContactPhone}} · <a href=\"mailto:{{ContactEmail}}\">{{ContactEmail}}</a></p>";

    private const string BookingSummary =
        "<table cellpadding=\"4\" style=\"border-collapse:collapse\">" +
        "<tr><td><strong>Booking reference</strong></td><td>{{BookingReference}}</td></tr>" +
        "<tr><td><strong>Boat</strong></td><td>{{BoatName}}</td></tr>" +
        "<tr><td><strong>Check-in</strong></td><td>{{CheckInDate}}, {{CheckInTime}}</td></tr>" +
        "<tr><td><strong>Check-out</strong></td><td>{{CheckOutDate}}, from {{CheckOutTime}}</td></tr>" +
        "<tr><td><strong>Guests</strong></td><td>{{NumberOfAdults}} adults, {{NumberOfChildren}} children</td></tr>" +
        "</table>";

    public static IReadOnlyList<(EmailTemplateKey Key, string Subject, string HtmlBody)> All { get; } =
    [
        (EmailTemplateKey.BookingConfirmed,
            "Your Berger Houseboats booking {{BookingReference}} is confirmed",
            "<p>Dear {{CustomerFirstName}},</p>" +
            "<p>Thank you for booking with Berger Houseboats. Your booking is confirmed.</p>" + BookingSummary +
            "<h3>Payments</h3>" +
            "<p>Total: {{TotalPrice}} · Paid: {{AmountPaid}} · Owing: {{AmountOwing}}</p>" +
            "{{PaymentSchedule}}" +
            "<p>You can pay your balance at any time using your secure payment link: <a href=\"{{PaymentLink}}\">{{PaymentLink}}</a></p>" +
            "<h3>Add-ons</h3>" +
            "<p>The add-ons you requested are subject to confirmation by our staff. Confirmed add-ons are added to your balance.</p>" +
            "{{RequestedAddons}}" +
            "<h3>Security bond</h3>" +
            "<p>A {{SecurityBond}} security bond applies. Details will be provided before your hire.</p>" +
            "<p>Please read our <a href=\"{{HireTermsUrl}}\">Hire Terms and Procedures</a>.</p>" +
            Signature),
        (EmailTemplateKey.StaffNewOnlineBooking,
            "New online booking {{BookingReference}}: {{BoatName}} {{CheckInDate}}",
            "<p>A new online booking has been paid.</p>" +
            "<p>{{FrequentFloaterNotice}}</p>" +
            "<p><strong>{{CustomerName}}</strong><br>{{CustomerEmail}}<br>{{CustomerMobile}}</p>" + BookingSummary +
            "<p>Total: {{TotalPrice}} · Paid: {{AmountPaid}} · Owing: {{AmountOwing}}</p>" +
            "<h3>Requested add-ons (to confirm)</h3>{{RequestedAddons}}"),
        (EmailTemplateKey.PaymentDue,
            "Payment due for your Berger Houseboats booking {{BookingReference}}",
            "<p>Dear {{CustomerFirstName}},</p>" +
            "<p>A payment of <strong>{{AmountDue}}</strong> is due by <strong>{{DueDate}}</strong> for your booking.</p>" + BookingSummary +
            "<p>Total: {{TotalPrice}} · Paid: {{AmountPaid}} · Owing: {{AmountOwing}}</p>" +
            "<p><a href=\"{{PaymentLink}}\">Pay securely online</a></p>" +
            "<p>If you have already paid, thank you, and please disregard this email.</p>" +
            Signature),
        (EmailTemplateKey.PaymentReceived,
            "Payment received for booking {{BookingReference}}",
            "<p>Dear {{CustomerFirstName}},</p>" +
            "<p>Thank you, we have received your payment of <strong>{{AmountDue}}</strong>.</p>" + BookingSummary +
            "<p>Total: {{TotalPrice}} · Paid: {{AmountPaid}} · Owing: {{AmountOwing}}</p>" +
            "{{PaymentSchedule}}" +
            Signature),
        (EmailTemplateKey.PreHireInstructions,
            "Getting ready for your houseboat holiday on {{BoatName}}",
            "<p>Dear {{CustomerFirstName}},</p>" +
            "<p>Your houseboat holiday is almost here! Here is everything you need to know before you arrive.</p>" + BookingSummary +
            "<ul>" +
            "<li>Check-in is {{CheckInTime}} on {{CheckInDate}}. Please allow time for your handover and driving instruction.</li>" +
            "<li>Check-out is from {{CheckOutTime}} on {{CheckOutDate}}.</li>" +
            "<li>A {{SecurityBond}} security bond is required at check-in.</li>" +
            "<li>Bring bedding linen, towels, food and drinks. Pack in soft bags where possible.</li>" +
            "</ul>" +
            "<p>Owing on your booking: {{AmountOwing}}. <a href=\"{{PaymentLink}}\">Pay online</a></p>" +
            "<p>Please read our <a href=\"{{HireTermsUrl}}\">Hire Terms and Procedures</a> before you arrive.</p>" +
            Signature),
        (EmailTemplateKey.StaffBalanceOverdue,
            "Overdue payment: {{BookingReference}} ({{CustomerName}})",
            "<p>A payment is overdue. The booking has not been cancelled: please follow up with the customer.</p>" +
            "<p><strong>{{CustomerName}}</strong><br>{{CustomerEmail}}<br>{{CustomerMobile}}</p>" + BookingSummary +
            "<p>Amount overdue: <strong>{{AmountDue}}</strong> (due {{DueDate}})<br>Total: {{TotalPrice}} · Paid: {{AmountPaid}} · Owing: {{AmountOwing}}</p>"),
        (EmailTemplateKey.StaffStandbySuperseded,
            "Stand-by booking superseded: {{BoatName}} {{CheckInDate}}",
            "<p>A stand-by booking has been superseded by a confirmed booking for the same boat and dates.</p>" +
            "<p><strong>{{CustomerName}}</strong><br>{{CustomerEmail}}<br>{{CustomerMobile}}</p>" + BookingSummary +
            "<p>Please contact the customer about other dates.</p>"),
        (EmailTemplateKey.StaffAbandonedBooking,
            "Unfinished online booking: {{CustomerName}}",
            "<p>A customer started an online booking but didn't finish it. You may like to contact them to help.</p>" +
            "<p><strong>{{CustomerName}}</strong><br>{{CustomerEmail}}<br>{{CustomerMobile}}</p>" +
            "<table cellpadding=\"4\" style=\"border-collapse:collapse\">" +
            "<tr><td><strong>Boat</strong></td><td>{{BoatName}}</td></tr>" +
            "<tr><td><strong>Dates</strong></td><td>{{CheckInDate}} → {{CheckOutDate}}</td></tr>" +
            "<tr><td><strong>Guests</strong></td><td>{{NumberOfAdults}} adults, {{NumberOfChildren}} children</td></tr>" +
            "<tr><td><strong>Last step reached</strong></td><td>{{LastStepReached}}</td></tr>" +
            "</table>" +
            "<h3>Requested add-ons</h3>{{RequestedAddons}}"),
    ];
}
