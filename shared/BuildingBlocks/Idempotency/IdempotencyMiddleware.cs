using BuildingBlocks.Domain;
using BuildingBlocks.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Idempotency;

/// <summary>Endpoint metadata: this endpoint has side effects and requires an <c>Idempotency-Key</c> header.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute;

public sealed record IdempotencyOptions(TimeSpan Retention, TimeSpan StaleLockAfter, TimeSpan CleanupInterval);

/// <summary>
/// BR4 — Challenge 2. For endpoints marked <see cref="IdempotentAttribute"/>:
/// <list type="number">
/// <item>Fingerprint the request (method + path + canonical body).</item>
/// <item>INSERT the key as IN_PROGRESS. The unique (key, scope) constraint is the lock, so of two identical
/// requests arriving at the same moment exactly one proceeds.</item>
/// <item>Loser with a COMPLETED record: replay the stored status and body (<c>Idempotency-Replayed: true</c>).
/// Same key, different payload: 422. Still IN_PROGRESS: 409 with <c>Retry-After</c>.</item>
/// <item>Winner: run the endpoint, store the response (2xx and 4xx). 5xx and exceptions release the key so the
/// client may retry.</item>
/// </list>
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotency-Replayed";
    public const int MaxKeyLength = 100;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>() is null)
        {
            await next(context);
            return;
        }

        var key = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, ErrorCodes.IdempotencyKeyRequired,
                $"Header {HeaderName} is required (1-{MaxKeyLength} characters) for this operation.");
            return;
        }

        var store = context.RequestServices.GetRequiredService<IIdempotencyStore>();
        var options = context.RequestServices.GetRequiredService<IdempotencyOptions>();
        var clock = context.RequestServices.GetRequiredService<TimeProvider>();

        context.Request.EnableBuffering();
        var body = await new StreamReader(context.Request.Body).ReadToEndAsync(context.RequestAborted);
        context.Request.Body.Position = 0;

        var path = context.Request.Path.Value ?? "/";
        var scope = $"{context.Request.Method} {path.ToLowerInvariant()}";
        var hash = RequestFingerprint.Compute(context.Request.Method, path, body);
        var now = clock.GetUtcNow();

        if (!await store.TryClaimAsync(key, scope, hash, now, now + options.Retention))
        {
            var existing = await store.FindAsync(key, scope);
            if (existing is null)
            {
                // The holder released it (5xx) between our insert and read: treat as in progress, client retries.
                await WriteInProgressAsync(context);
                return;
            }

            if (existing.RequestHash != hash)
            {
                logger.LogWarning("Idempotency key {IdempotencyKey} reused with a different payload on {Scope}", key, scope);
                await WriteProblemAsync(context, StatusCodes.Status422UnprocessableEntity, ErrorCodes.IdempotencyKeyMismatch,
                    $"Idempotency key '{key}' was already used with a different request payload.");
                return;
            }

            if (existing.State == IdempotencyRecord.Completed)
            {
                logger.LogInformation("Idempotency replay for key {IdempotencyKey} on {Scope}: returning stored {StatusCode}",
                    key, scope, existing.ResponseStatus);
                await ReplayAsync(context, existing);
                return;
            }

            if (!await store.TryReclaimStaleAsync(key, scope, now - options.StaleLockAfter, now))
            {
                logger.LogInformation("Idempotency key {IdempotencyKey} on {Scope} is still being processed", key, scope);
                await WriteInProgressAsync(context);
                return;
            }

            logger.LogWarning("Idempotency key {IdempotencyKey} on {Scope} was abandoned; reprocessing", key, scope);
        }

        await ExecuteAndStoreAsync(context, store, key, scope);
    }

    private async Task ExecuteAndStoreAsync(HttpContext context, IIdempotencyStore store, string key, string scope)
    {
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        // Once claimed, the operation runs to completion even if the caller disconnects (e.g. its timeout fired):
        // the full response must be stored so the caller's retry gets it replayed instead of an empty body.
        context.RequestAborted = CancellationToken.None;

        try
        {
            await next(context);
        }
        catch
        {
            context.Response.Body = originalBody;
            await store.ReleaseAsync(key, scope);
            throw;
        }

        context.Response.Body = originalBody;
        var status = context.Response.StatusCode;
        buffer.Position = 0;
        var responseBody = await new StreamReader(buffer).ReadToEndAsync(CancellationToken.None);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            await store.ReleaseAsync(key, scope);
            logger.LogInformation("Idempotency key {IdempotencyKey} released after {StatusCode}; the request may be retried", key, status);
        }
        else
        {
            await store.CompleteAsync(key, scope, status, responseBody, context.Response.ContentType, context.Response.Headers.Location.FirstOrDefault());
            logger.LogInformation("Idempotency key {IdempotencyKey} stored with {StatusCode} for {Scope}", key, status, scope);
        }

        try
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // Client went away; the stored result will be replayed when it retries.
        }
    }

    private static async Task ReplayAsync(HttpContext context, IdempotencyRecord record)
    {
        context.Response.StatusCode = record.ResponseStatus ?? StatusCodes.Status200OK;
        context.Response.Headers[ReplayedHeader] = "true";
        if (record.ResponseLocation is not null)
        {
            context.Response.Headers.Location = record.ResponseLocation;
        }

        if (!string.IsNullOrEmpty(record.ResponseBody))
        {
            context.Response.ContentType = record.ResponseContentType ?? "application/json";
            await context.Response.WriteAsync(record.ResponseBody);
        }
    }

    private static Task WriteInProgressAsync(HttpContext context)
    {
        context.Response.Headers.RetryAfter = "1";
        return WriteProblemAsync(context, StatusCodes.Status409Conflict, ErrorCodes.RequestInProgress,
            "A request with this idempotency key is still being processed. Retry shortly.");
    }

    private static async Task WriteProblemAsync(HttpContext context, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        var problems = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = ProblemDetailsSetup.Humanize(code),
                Detail = detail,
                Extensions = { ["code"] = code },
            },
        });
    }
}

public static class IdempotencyExtensions
{
    /// <summary>
    /// Registers the idempotency store backed by the service's own <typeparamref name="TContext"/>.
    /// The context must call <see cref="IdempotencyModel.MapIdempotencyRecords"/>.
    /// Env: <c>IDEMPOTENCY_TTL_HOURS</c> (default 24), <c>IDEMPOTENCY_LOCK_TIMEOUT_SECONDS</c> (default 60),
    /// <c>IDEMPOTENCY_CLEANUP_INTERVAL_MINUTES</c> (default 60).
    /// </summary>
    public static IServiceCollection AddIdempotency<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : DbContext
    {
        services.AddSingleton(new IdempotencyOptions(
            TimeSpan.FromHours(configuration.GetValue("IDEMPOTENCY_TTL_HOURS", 24)),
            TimeSpan.FromSeconds(configuration.GetValue("IDEMPOTENCY_LOCK_TIMEOUT_SECONDS", 60)),
            TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("IDEMPOTENCY_CLEANUP_INTERVAL_MINUTES", 60)))));
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore<TContext>>();
        services.AddHostedService<IdempotencyCleanupWorker<TContext>>();
        return services;
    }

    /// <summary>Marks the endpoint as unsafe to repeat: requires and enforces <c>Idempotency-Key</c>.</summary>
    public static RouteHandlerBuilder RequireIdempotencyKey(this RouteHandlerBuilder builder) =>
        builder.WithMetadata(new IdempotentAttribute())
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
}
