using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BuildingBlocks.Persistence;

public static class PostgresErrors
{
    public static bool IsUniqueViolation(Exception exception, string? constraintName = null) =>
        Matches(exception, PostgresErrorCodes.UniqueViolation, constraintName);

    public static bool IsExclusionViolation(Exception exception, string? constraintName = null) =>
        Matches(exception, PostgresErrorCodes.ExclusionViolation, constraintName);

    private static bool Matches(Exception exception, string sqlState, string? constraintName)
    {
        var postgres = exception as PostgresException ?? (exception as DbUpdateException)?.InnerException as PostgresException;
        return postgres is not null
            && postgres.SqlState == sqlState
            && (constraintName is null || postgres.ConstraintName == constraintName);
    }
}
