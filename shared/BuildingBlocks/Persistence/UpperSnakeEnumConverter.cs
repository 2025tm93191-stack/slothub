using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BuildingBlocks.Persistence;

/// <summary>
/// Stores enums as UPPER_SNAKE_CASE text (e.g. <c>PartiallyRefunded</c> → <c>PARTIALLY_REFUNDED</c>),
/// matching the JSON representation so database rows read the same as API responses.
/// </summary>
public sealed class UpperSnakeEnumConverter<TEnum>() : ValueConverter<TEnum, string>(value => ToProvider(value), value => FromProvider(value))
    where TEnum : struct, Enum
{
    private static string ToProvider(TEnum value) => JsonNamingPolicy.SnakeCaseUpper.ConvertName(value.ToString());

    private static TEnum FromProvider(string value) => Enum.Parse<TEnum>(value.Replace("_", string.Empty), ignoreCase: true);
}
