namespace AlTamam.Contracts.Dtos;

public sealed record AutoAttendanceRunDto(string JobKey, string BusinessDate, string CompletedAtUtc,
    bool CatchUp, int CopiedCount, int SkippedNoYesterdayCount, int SkippedMovementCount,
    int SkippedInactiveStatusCount, string SummaryArabic);
public sealed record DayProcessingQuery(string? BusinessDate = null);
public sealed record DayProcessingStatus(string BusinessDate, string DueTimeLocal, bool Completed,
    AutoAttendanceRunDto? Run, bool DayClosed, bool MonthClosed, bool CanCorrectClosedDay, string? LastFailure);
public sealed record ClosedDayCorrection(string PersonId, string BusinessDate, string StatusId, string Reason);
public sealed record ClosedDayCorrectionPreview(ClosedDayCorrection Change, string PersonName,
    string ServiceNumberA, string SiteName, string BeforeStatus, string AfterStatus, long Revision, string PreviewHash);
public sealed record CommitClosedDayCorrection(ClosedDayCorrection Change, string PreviewHash);

public sealed record DayProcessingAttempt(AutoAttendanceRunDto? Run);
