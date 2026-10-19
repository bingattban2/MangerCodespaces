using System;
using System.Collections.Generic;
using AlTamam.Contracts.Events;

namespace AlTamam.Contracts.Bridge;

/// <summary>
/// Typed domain/service failure guaranteed to carry a canonical BridgeErrorCodes value
/// and a clear Arabic user-facing message.
/// </summary>
public sealed record ServiceFailure(
    string ErrorCode,
    string UserMessageArabic,
    string? TechnicalDetail = null
);

/// <summary>
/// Typed service outcome that prevents returning success on exceptions or uncommitted operations.
/// </summary>
public sealed record ServiceResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public ServiceFailure? Failure { get; }
    public long? Revision { get; }
    public IReadOnlyList<UiInvalidationEventPayload> InvalidationEvents { get; }

    private ServiceResult(
        bool isSuccess,
        T? value,
        ServiceFailure? failure,
        long? revision,
        IReadOnlyList<UiInvalidationEventPayload>? invalidationEvents)
    {
        IsSuccess = isSuccess;
        Value = value;
        Failure = failure;
        Revision = revision;
        InvalidationEvents = invalidationEvents ?? Array.Empty<UiInvalidationEventPayload>();
    }

    public static ServiceResult<T> Ok(
        T value,
        long? revision = null,
        IReadOnlyList<UiInvalidationEventPayload>? invalidationEvents = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ServiceResult<T>(true, value, null, revision, invalidationEvents);
    }

    public static ServiceResult<T> Fail(string errorCode, string userMessageArabic, string? technicalDetail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessageArabic);
        return new ServiceResult<T>(false, default, new ServiceFailure(errorCode, userMessageArabic, technicalDetail), null, null);
    }

    public static ServiceResult<T> Fail(ServiceFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new ServiceResult<T>(false, default, failure, null, null);
    }

    public BridgeResponse ToBridgeResponse(string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        if (IsSuccess)
        {
            return new BridgeResponse(
                RequestId: requestId,
                Success: true,
                Data: Value,
                ErrorCode: null,
                UserMessage: null,
                Revision: Revision,
                InvalidationEvents: InvalidationEvents.Count > 0 ? InvalidationEvents : null);
        }

        var error = Failure ?? new ServiceFailure(BridgeErrorCodes.ValidationError, "حدث خطأ غير متوقع أثناء تنفيذ العملية.");
        return new BridgeResponse(
            RequestId: requestId,
            Success: false,
            Data: null,
            ErrorCode: error.ErrorCode,
            UserMessage: error.UserMessageArabic,
            Revision: null,
            InvalidationEvents: null);
    }
}
