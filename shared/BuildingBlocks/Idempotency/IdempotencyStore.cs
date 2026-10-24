using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Idempotency;

/// <summary>One row per (Idempotency-Key, scope). Lives in each service's own database.</summary>
public sealed class IdempotencyRecord
{
    public const string InProgress = "IN_PROGRESS";
    public const string Completed = "COMPLETED";

    public string Key { get; set; } = null!;

    /// <summary>"METHOD /path" of the request the key was first used for.</summary>
    public string Scope { get; set; } = null!;

    public string RequestHash { get; set; } = null!;

    public string State { get; set; } = null!;

    public int? ResponseStatus { get; set; }

    public string? ResponseBody { get; set; }

    public string? ResponseContentType { get; set; }

    public string? ResponseLocation { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

public interface IIdempotencyStore
{
    /// <summary>Atomically inserts an IN_PROGRESS record. Only one concurrent caller can win.</summary>
    Task<bool> TryClaimAsync(string key, string scope, string requestHash, DateTimeOffset now, DateTimeOffset expiresAt);

    Task<IdempotencyRecord?> FindAsync(string key, string scope);

    /// <summary>Takes over an IN_PROGRESS record abandoned before <paramref name="staleBefore"/> (e.g. the process crashed).</summary>
    Task<bool> TryReclaimStaleAsync(string key, string scope, DateTimeOffset staleBefore, DateTimeOffset now);

    Task CompleteAsync(string key, string scope, int status, string? body, string? contentType, string? location);

    /// <summary>Deletes an IN_PROGRESS record so the request can be retried (used for 5xx and exceptions).</summary>
    Task ReleaseAsync(string key, string scope);
}

public static class IdempotencyModel
{
    public const string TableName = "idempotency_keys";

    /// <summary>Maps <see cref="IdempotencyRecord"/> into a service's DbContext (call from OnModelCreating).</summary>
    public static ModelBuilder MapIdempotencyRecords(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable(TableName);
            e.HasKey(x => new { x.Key, x.Scope });
            e.Property(x => x.Key).HasMaxLength(IdempotencyMiddleware.MaxKeyLength);
            e.Property(x => x.Scope).HasMaxLength(300);
            e.Property(x => x.RequestHash).HasMaxLength(64);
            e.Property(x => x.State).HasMaxLength(16);
            e.Property(x => x.ResponseContentType).HasMaxLength(100);
            e.Property(x => x.ResponseLocation).HasMaxLength(300);
            e.HasIndex(x => x.ExpiresAt);
        });
        return modelBuilder;
    }
}

/// <summary>
/// EF Core / PostgreSQL implementation. Uses set-based SQL (INSERT … ON CONFLICT DO NOTHING) so it never
/// interferes with entities tracked by the request's business logic on the same DbContext.
/// </summary>
internal sealed class EfIdempotencyStore<TContext>(TContext db) : IIdempotencyStore
    where TContext : DbContext
{
    private DbSet<IdempotencyRecord> Records => db.Set<IdempotencyRecord>();

    public async Task<bool> TryClaimAsync(string key, string scope, string requestHash, DateTimeOffset now, DateTimeOffset expiresAt) =>
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO idempotency_keys (key, scope, request_hash, state, created_at, expires_at)
            VALUES ({key}, {scope}, {requestHash}, {IdempotencyRecord.InProgress}, {now}, {expiresAt})
            ON CONFLICT (key, scope) DO NOTHING
            """) == 1;

    public Task<IdempotencyRecord?> FindAsync(string key, string scope) =>
        Records.AsNoTracking().SingleOrDefaultAsync(x => x.Key == key && x.Scope == scope);

    public async Task<bool> TryReclaimStaleAsync(string key, string scope, DateTimeOffset staleBefore, DateTimeOffset now) =>
        await Records
            .Where(x => x.Key == key && x.Scope == scope && x.State == IdempotencyRecord.InProgress && x.CreatedAt < staleBefore)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, now)) == 1;

    public Task CompleteAsync(string key, string scope, int status, string? body, string? contentType, string? location) =>
        Records
            .Where(x => x.Key == key && x.Scope == scope)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.State, IdempotencyRecord.Completed)
                .SetProperty(x => x.ResponseStatus, status)
                .SetProperty(x => x.ResponseBody, body)
                .SetProperty(x => x.ResponseContentType, contentType)
                .SetProperty(x => x.ResponseLocation, location));

    public Task ReleaseAsync(string key, string scope) =>
        Records
            .Where(x => x.Key == key && x.Scope == scope && x.State == IdempotencyRecord.InProgress)
            .ExecuteDeleteAsync();
}
