using BuildingBlocks.Domain;
using Notification.Domain.Messages;

namespace Notification.UnitTests;

public class MessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly RetryPolicy Policy = new(3, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60));

    private static Message NewMessage() =>
        Message.Create(Guid.NewGuid(), EventType.BookingConfirmed, Channel.Sms, "+919800000001", "hello", Now);

    [Fact]
    public void NewMessage_IsPending_AndDueImmediately()
    {
        var message = NewMessage();

        Assert.Equal(MessageStatus.Pending, message.Status);
        Assert.Equal(Now, message.NextAttemptAt);
        Assert.Equal(0, message.Attempts);
    }

    [Fact]
    public void Success_MarksSent_AndRecordsAttempt()
    {
        var message = NewMessage();

        message.RecordSuccess(Now);

        Assert.Equal(MessageStatus.Sent, message.Status);
        Assert.Equal(Now, message.SentAt);
        Assert.Single(message.DeliveryAttempts, a => a.Success);
    }

    [Fact]
    public void Failure_SchedulesRetryWithBackoff()
    {
        var message = NewMessage();

        message.RecordFailure("provider down", Policy, Now);

        Assert.Equal(MessageStatus.FailedRetrying, message.Status);
        Assert.Equal(Now.AddSeconds(2), message.NextAttemptAt);
        Assert.Equal("provider down", message.LastError);
    }

    [Fact]
    public void Failure_AfterMaxAttempts_IsDead()
    {
        var message = NewMessage();

        message.RecordFailure("1", Policy, Now);
        message.RecordFailure("2", Policy, Now);
        message.RecordFailure("3", Policy, Now);

        Assert.Equal(MessageStatus.Dead, message.Status);
        Assert.Equal(3, message.DeliveryAttempts.Count);
    }

    [Fact]
    public void SentMessage_IsNeverDeliveredAgain()
    {
        var message = NewMessage();
        message.RecordSuccess(Now);

        Assert.Throws<InvalidStateTransitionException>(() => message.RecordSuccess(Now));
        Assert.Throws<InvalidStateTransitionException>(() => message.RecordFailure("x", Policy, Now));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(10, 60)] // capped
    public void RetryPolicy_DoublesDelay_UpToCap(int attempt, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), Policy.DelayAfter(attempt));
    }

    [Fact]
    public void Template_FillsKnownPlaceholders_AndKeepsUnknown()
    {
        var bookingId = Guid.NewGuid();

        var body = MessageTemplates.Render(EventType.BookingConfirmed, bookingId, new Dictionary<string, string> { ["slotStart"] = "Mon 10:00" });

        Assert.Equal($"Your booking {bookingId} on Mon 10:00 is confirmed. Amount paid: {{amount}}.", body);
    }

    [Fact]
    public void Template_ExistsForEveryEventType()
    {
        foreach (var eventType in Enum.GetValues<EventType>())
        {
            Assert.Contains(Guid.Empty.ToString(), MessageTemplates.Render(eventType, Guid.Empty, null));
        }
    }
}
