namespace BuildingBlocks.Domain;

/// <summary>Category of a business error. The API layer maps each kind to an HTTP status code.</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Unprocessable,
    PaymentRequired,
    Unavailable,
    Timeout,
}

/// <summary>
/// A business rule or precondition was violated. Carries a stable machine-readable <see cref="Code"/>
/// (see <see cref="ErrorCodes"/>) that clients can rely on.
/// </summary>
public class DomainException(ErrorKind kind, string code, string message) : Exception(message)
{
    public ErrorKind Kind { get; } = kind;

    public string Code { get; } = code;

    public static DomainException Validation(string code, string message) => new(ErrorKind.Validation, code, message);

    public static DomainException NotFound(string code, string message) => new(ErrorKind.NotFound, code, message);

    public static DomainException Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);
}

/// <summary>An entity was asked to move between two states that its state machine does not allow.</summary>
public sealed class InvalidStateTransitionException(string entity, object from, object to)
    : DomainException(ErrorKind.Conflict, ErrorCodes.InvalidStateTransition, $"{entity} cannot move from {from} to {to}.");
