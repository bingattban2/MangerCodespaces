namespace AlTamam.Contracts.Dtos;

public sealed record DailyRosterQuery(string Search = "", string? SiteId = null, string? StatusId = null,
    bool UnpreparedOnly = false, int Offset = 0, int Limit = 50);
public sealed record DailyRosterRow(string PersonId, string Name, string ServiceNumberA, string SiteId,
    string SiteName, string AssignmentId, long PersonVersion, string? StatusId, string? StatusName,
    string? StatusSymbol, string? StatusColor, long Revision, bool MovementExempt, string? YesterdayStatus);
public sealed record DailyRosterPage(string BusinessDate, IReadOnlyList<DailyRosterRow> Items, long Total);
public sealed record AttendanceCounter(string Id, string Caption, long Count);
public sealed record DashboardSummary(string BusinessDate, long RosterCount, long EligibleCount,
    long MovementExemptCount, long PreparedCount, long UnpreparedCount,
    IReadOnlyList<AttendanceCounter> StatusCounts, IReadOnlyList<AttendanceCounter> SiteCounts);
public sealed record AttendanceSelection(IReadOnlyList<string> PersonIds, bool AllMatching,
    DailyRosterQuery Filter, string? DraftId = null);
public sealed record AttendanceChange(string BusinessDate, AttendanceSelection Selection, string StatusId,
    bool Revise, string Reason = "");
public sealed record AttendancePreview(string BusinessDate, string PreviewHash, int Count,
    string StatusName, IReadOnlyList<DailyRosterRow> Sample);
public sealed record CommitAttendanceCommand(AttendanceChange Change, string PreviewHash);
public sealed record AttendanceCommitResult(string BusinessDate, int Count, bool Revised);
public sealed record SaveAttendanceDraftCommand(AttendanceChange Change);
public sealed record AttendanceDraftDto(string DraftId, string BusinessDate, string? SiteId,
    string StatusId, IReadOnlyList<string> PersonIds, string UpdatedAtUtc);
public sealed record DeleteAttendanceDraftCommand(string DraftId);
