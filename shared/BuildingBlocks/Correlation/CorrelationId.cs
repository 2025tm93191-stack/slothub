using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace BuildingBlocks.Correlation;

public static class CorrelationId
{
    public const string HeaderName = "X-Correlation-Id";
    internal const string ItemKey = "CorrelationId";
    private const int MaxLength = 100;

    public static string? Get(HttpContext context) => context.Items[ItemKey] as string;

    internal static string Resolve(string? incoming) =>
        string.IsNullOrWhiteSpace(incoming) || incoming.Length > MaxLength ? Guid.NewGuid().ToString("N") : incoming;
}

/// <summary>
/// Reads <c>X-Correlation-Id</c> from the request (or generates one), echoes it on the response
/// and pushes it into the log context so every log line of the request carries it.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = CorrelationId.Resolve(context.Request.Headers[CorrelationId.HeaderName].FirstOrDefault());
        context.Items[CorrelationId.ItemKey] = correlationId;
        context.Response.Headers[CorrelationId.HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}

/// <summary>Adds the current request's correlation ID to every outgoing <see cref="HttpClient"/> call.</summary>
public sealed class CorrelationIdHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = accessor.HttpContext is { } context ? CorrelationId.Get(context) : null;
        if (correlationId is not null && !request.Headers.Contains(CorrelationId.HeaderName))
        {
            request.Headers.Add(CorrelationId.HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
