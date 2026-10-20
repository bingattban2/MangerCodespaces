namespace AlTamam.Contracts.Dtos;

/// <summary>
/// One committed daily record as it appeared at its effective business date.
/// Monthly historical reads compose these records without current display labels.
/// This is a persisted-data projection, not a Windows/WebView2 screen.
/// </summary>
public sealed record HistoricAttendanceDayDto(
    string PersonId,
    string PersonNameSnapshot,
    string ServiceNumberASnapshot,
    string BusinessDate,
    string StatusId,
    string StatusNameSnapshot,
    string StatusSymbolSnapshot,
    string StatusColorSnapshot,
    string AssignmentIdAtEffectiveDate,
    string SiteIdAtEffectiveDate,
    string SiteNameSnapshot,
    string? SourceMovementId,
    int RevisionNumber);
