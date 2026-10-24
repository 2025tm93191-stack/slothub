namespace BuildingBlocks.Domain;

/// <summary>Catalogue of stable error codes returned in the <c>code</c> field of every Problem Details response.</summary>
public static class ErrorCodes
{
    // Generic
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string BadRequest = "BAD_REQUEST";
    public const string NotFound = "NOT_FOUND";
    public const string InvalidStateTransition = "INVALID_STATE_TRANSITION";
    public const string InternalError = "INTERNAL_ERROR";

    // Idempotency (BR4)
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";
    public const string IdempotencyKeyMismatch = "IDEMPOTENCY_KEY_MISMATCH";
    public const string RequestInProgress = "REQUEST_IN_PROGRESS";

    // Calls to other services
    public const string DownstreamUnavailable = "DOWNSTREAM_UNAVAILABLE";
    public const string DownstreamTimeout = "DOWNSTREAM_TIMEOUT";
    public const string DownstreamCircuitOpen = "DOWNSTREAM_CIRCUIT_OPEN";

    // Booking
    public const string BookingNotFound = "BOOKING_NOT_FOUND";
    public const string BookingBusy = "BOOKING_BUSY";
    public const string TenantMismatch = "TENANT_MISMATCH";
    public const string CancellationNotAllowed = "CANCELLATION_NOT_ALLOWED";
    public const string PaymentNotCompleted = "PAYMENT_NOT_COMPLETED";

    // Scheduling
    public const string TenantNotFound = "TENANT_NOT_FOUND";
    public const string ResourceNotFound = "RESOURCE_NOT_FOUND";
    public const string OfferingNotFound = "OFFERING_NOT_FOUND";
    public const string ResourceOfferingMismatch = "RESOURCE_OFFERING_MISMATCH";
    public const string SlotNotFound = "SLOT_NOT_FOUND";
    public const string SlotOverlap = "SLOT_OVERLAP";
    public const string SlotInPast = "SLOT_IN_PAST";
    public const string SlotUnavailable = "SLOT_UNAVAILABLE";
    public const string HoldNotFound = "HOLD_NOT_FOUND";
    public const string HoldExpired = "HOLD_EXPIRED";
    public const string BookingAlreadyHasHold = "BOOKING_ALREADY_HAS_HOLD";

    // Payment
    public const string PaymentNotFound = "PAYMENT_NOT_FOUND";
    public const string PaymentDeclined = "PAYMENT_DECLINED";
    public const string PaymentGatewayUnavailable = "PAYMENT_GATEWAY_UNAVAILABLE";
    public const string PaymentNotRefundable = "PAYMENT_NOT_REFUNDABLE";
    public const string RefundExceedsPayment = "REFUND_EXCEEDS_PAYMENT";

    // Notification
    public const string UnknownEventType = "UNKNOWN_EVENT_TYPE";
}
