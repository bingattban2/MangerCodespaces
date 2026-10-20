using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using AlTamam.Domain.ValueObjects;

namespace AlTamam.Application.Identity;

/// <summary>
/// Effective-date operations. A verified session is required for every read/write.
/// Implementation owns SQLCipher transactions and immutable audit/history evidence.
/// </summary>
public interface IPersonIdentityStore
{
    Task<ServiceResult<PersonHistoricIdentityDto>> ChangeAsync(
        PersonIdentityChange change, CancellationToken cancellationToken = default);

    Task<ServiceResult<PersonHistoricIdentityDto>> ReadAsOfAsync(
        PersonId personId, DateOnly businessDate, string sessionNonce,
        CancellationToken cancellationToken = default);

    /// <summary>Returns only persisted attendance days for the requested month with
    /// the names/numbers/site/status snapshots in effect on each original day.</summary>
    Task<ServiceResult<IReadOnlyList<HistoricAttendanceDayDto>>> ReadMonthlyHistoryAsync(
        PersonId personId, int year, int month, string sessionNonce,
        CancellationToken cancellationToken = default);
}

public sealed record PersonIdentityChange(
    PersonId PersonId,
    string VisibleName,
    string ServiceNumberA,
    DateOnly EffectiveLocalDate,
    long ExpectedRowVersion,
    string RequestId,
    string SessionNonce,
    string Reason);
