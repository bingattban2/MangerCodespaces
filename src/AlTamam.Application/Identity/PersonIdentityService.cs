using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using AlTamam.Domain.Abstractions;
using AlTamam.Domain.Policies;
using AlTamam.Domain.ValueObjects;

namespace AlTamam.Application.Identity;

/// <summary>
/// Business boundary for corrections with an explicit effective local date.
/// This is not a WebView2 dispatcher. The store rechecks persisted session/role
/// and atomic persistence; no response implies a commit without that store.
/// </summary>
public sealed class PersonIdentityService
{
    private readonly IPersonIdentityStore _store;
    private readonly ISystemClock _clock;

    public PersonIdentityService(IPersonIdentityStore store, ISystemClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<ServiceResult<PersonHistoricIdentityDto>> ChangeAsync(
        PersonIdentityChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var name = change.VisibleName?.Trim() ?? string.Empty;
        var number = change.ServiceNumberA?.Trim() ?? string.Empty;
        var normalizedNumber = ArabicTextNormalizer.NormalizeDigitsOnly(number);
        if (string.IsNullOrWhiteSpace(change.PersonId.Value) ||
            string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(number) ||
            string.IsNullOrWhiteSpace(normalizedNumber) ||
            string.IsNullOrWhiteSpace(change.Reason) ||
            string.IsNullOrWhiteSpace(change.RequestId) ||
            string.IsNullOrWhiteSpace(change.SessionNonce) ||
            change.ExpectedRowVersion < 1)
            return Task.FromResult(ServiceResult<PersonHistoricIdentityDto>.Fail(
                BridgeErrorCodes.ValidationError, "بيانات تعديل هوية الفرد غير مكتملة أو غير صحيحة."));

        // Prevent retroactive rewriting of an already-effective daily or monthly roster.
        if (change.EffectiveLocalDate != _clock.CurrentBusinessDate)
            return Task.FromResult(ServiceResult<PersonHistoricIdentityDto>.Fail(
                BridgeErrorCodes.HistoricDayLocked, "يمكن تغيير الهوية ابتداءً من يوم العمل الحالي فقط."));

        return _store.ChangeAsync(change with
        {
            VisibleName = name,
            ServiceNumberA = number,
            Reason = change.Reason.Trim()
        }, cancellationToken);
    }

    /// <summary>
    /// Read the actual encrypted attendance rows for a month with historical labels,
    /// never substituting mutable current names, site labels or status labels.
    /// </summary>
    public Task<ServiceResult<IReadOnlyList<HistoricAttendanceDayDto>>> ReadMonthlyHistoryAsync(
        PersonId personId, int year, int month, string sessionNonce,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(personId.Value) ||
            string.IsNullOrWhiteSpace(sessionNonce) || year is < 1 or > 9999 ||
            month is < 1 or > 12)
            return Task.FromResult(ServiceResult<IReadOnlyList<HistoricAttendanceDayDto>>.Fail(
                BridgeErrorCodes.ValidationError, "معرف الفرد والجلسة والشهر والسنة مطلوبة وصحيحة."));
        return _store.ReadMonthlyHistoryAsync(personId, year, month, sessionNonce, cancellationToken);
    }

    public Task<ServiceResult<PersonHistoricIdentityDto>> ReadAsOfAsync(
        PersonId personId, DateOnly businessDate, string sessionNonce,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(personId.Value) || string.IsNullOrWhiteSpace(sessionNonce))
            return Task.FromResult(ServiceResult<PersonHistoricIdentityDto>.Fail(
                BridgeErrorCodes.ValidationError, "معرف الفرد والجلسة مطلوبان للاستعلام التاريخي."));
        return _store.ReadAsOfAsync(personId, businessDate, sessionNonce, cancellationToken);
    }
}
