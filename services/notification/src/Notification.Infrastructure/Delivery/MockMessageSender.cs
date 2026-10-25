using BuildingBlocks.Chaos;
using Microsoft.Extensions.Logging;
using Notification.Domain.Messages;

namespace Notification.Infrastructure.Delivery;

/// <summary>Stand-in for an SMS / email provider: "sends" by logging. Latency and failures come from <see cref="ChaosState"/>.</summary>
internal sealed class MockMessageSender(ChaosState chaos, ILogger<MockMessageSender> logger) : IMessageSender
{
    public const string ErrorMode = "ERROR";

    public async Task SendAsync(Message message, CancellationToken ct)
    {
        await chaos.DelayAsync(ct);

        if (chaos.ShouldFail())
        {
            throw new InvalidOperationException($"{message.Channel} provider error (simulated)");
        }

        logger.LogInformation("{Channel} sent to {Recipient}: {Body}", message.Channel, message.Recipient, message.Body);
    }
}
