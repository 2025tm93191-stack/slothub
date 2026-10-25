using BuildingBlocks.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Notification.Domain.Messages;

namespace Notification.Api.Endpoints;

public sealed record SendNotificationRequest(
    Guid BookingId,
    EventType EventType,
    Channel Channel,
    string Recipient,
    Dictionary<string, string>? Data);

public sealed record NotificationSearchParameters([FromQuery] Guid? BookingId, [FromQuery] int? Page, [FromQuery] int? PageSize);

public sealed record DeliveryAttemptResponse(int AttemptNumber, bool Success, string? Error, DateTimeOffset At);

public sealed record NotificationResponse(
    Guid Id,
    Guid BookingId,
    EventType EventType,
    Channel Channel,
    string Recipient,
    string Body,
    MessageStatus Status,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    IReadOnlyList<DeliveryAttemptResponse> DeliveryAttempts)
{
    public static NotificationResponse From(Message m) =>
        new(m.Id, m.BookingId, m.EventType, m.Channel, m.Recipient, m.Body, m.Status, m.Attempts, m.NextAttemptAt, m.LastError, m.CreatedAt, m.SentAt,
            m.DeliveryAttempts.OrderBy(a => a.AttemptNumber).Select(a => new DeliveryAttemptResponse(a.AttemptNumber, a.Success, a.Error, a.At)).ToList());
}

public sealed class SendNotificationRequestValidator : AbstractValidator<SendNotificationRequest>
{
    public SendNotificationRequestValidator()
    {
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.EventType).IsInEnum();
        RuleFor(x => x.Channel).IsInEnum();
        RuleFor(x => x.Recipient).NotEmpty().MaximumLength(320);
        RuleFor(x => x.Recipient).EmailAddress().When(x => x.Channel == Channel.Email);
        RuleFor(x => x.Recipient).Matches(@"^\+[1-9]\d{7,14}$").WithMessage("SMS recipient must be an E.164 phone number, e.g. +919800000001.")
            .When(x => x.Channel == Channel.Sms);
        RuleFor(x => x.Data).Must(d => d is null || d.Count <= 20).WithMessage("At most 20 data entries.");
    }
}

public sealed class NotificationSearchParametersValidator : AbstractValidator<NotificationSearchParameters>
{
    public NotificationSearchParametersValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public static class NotificationEndpoints
{
    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        var notifications = api.MapGroup("/notifications").WithTags("Notifications");

        notifications.MapPost("/", async (SendNotificationRequest request, NotificationService service, CancellationToken ct) =>
            {
                var (message, _) = await service.AcceptAsync(
                    new NewNotification(request.BookingId, request.EventType, request.Channel, request.Recipient, request.Data),
                    ct);
                // 202 for both new and duplicate requests: the caller only needs to know it will be delivered once.
                return TypedResults.Accepted($"/api/v1/notifications/{message.Id}", NotificationResponse.From(message));
            })
            .Validate<SendNotificationRequest>()
            .WithName("SendNotification")
            .WithSummary("Queue a customer notification. Duplicates per (booking, event, channel) return the existing message.");

        notifications.MapGet("/{notificationId:guid}", async (Guid notificationId, NotificationService service, CancellationToken ct) =>
                TypedResults.Ok(NotificationResponse.From(await service.GetAsync(notificationId, ct))))
            .WithName("GetNotification")
            .WithSummary("Get a notification and its delivery attempts")
            .ProducesProblem(StatusCodes.Status404NotFound);

        notifications.MapGet("/", async ([AsParameters] NotificationSearchParameters query, NotificationService service, CancellationToken ct) =>
                TypedResults.Ok((await service.ListAsync(query.BookingId, query.Page ?? 1, query.PageSize ?? 20, ct)).Map(NotificationResponse.From)))
            .Validate<NotificationSearchParameters>()
            .WithName("ListNotifications")
            .WithSummary("List notifications, optionally for one booking");

        return api;
    }
}
