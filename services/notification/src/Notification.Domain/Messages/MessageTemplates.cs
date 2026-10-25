using System.Text.RegularExpressions;

namespace Notification.Domain.Messages;

/// <summary>
/// Message text per event type. Placeholders like <c>{slotStart}</c> are filled from the request data;
/// <c>{bookingId}</c> is always available. Unknown placeholders are left as-is.
/// </summary>
public static partial class MessageTemplates
{
    private static readonly IReadOnlyDictionary<EventType, string> Templates = new Dictionary<EventType, string>
    {
        [EventType.BookingConfirmed] = "Your booking {bookingId} on {slotStart} is confirmed. Amount paid: {amount}.",
        [EventType.BookingCancelled] = "Your booking {bookingId} has been cancelled. Refund: {refundAmount}.",
        [EventType.BookingExpired] = "Your hold for booking {bookingId} expired before payment was completed.",
        [EventType.PaymentFailed] = "Payment for booking {bookingId} failed. Please try again.",
        [EventType.BookingReminder] = "Reminder: your booking {bookingId} is scheduled for {slotStart}.",
    };

    public static string Render(EventType eventType, Guid bookingId, IReadOnlyDictionary<string, string>? data)
    {
        var values = new Dictionary<string, string>(data ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            ["bookingId"] = bookingId.ToString(),
        };

        return Placeholder().Replace(Templates[eventType], match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
