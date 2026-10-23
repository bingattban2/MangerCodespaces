namespace AlTamam.Contracts.Dtos;

public sealed record ReportQuery(string? PersonId = null, string? FromDate = null, string? ToDate = null,
    string? SiteId = null, string Search = "", int Offset = 0, int Limit = 50);
public sealed record ReportPage<T>(IReadOnlyList<T> Items, long Total, int Offset);
public sealed record ReportIdentity(string Name, string ServiceNumberA, string FromDate, string? ToDate, string Reason);
public sealed record ReportStatusDays(string StatusId, string Caption, string Symbol, long Days);
public sealed record PersonReportSummary(string PersonId, string Name, string ServiceNumberA, string EnrollmentDate,
    string FromDate, string ToDate, string AsOfUtc, long ApprovedDays, long InternalTransfers,
    IReadOnlyList<ReportStatusDays> StatusDays);
public sealed record ReportAttendance(long RecordId, string Date, string PersonName, string ServiceNumberA,
    string SiteId, string SiteName, string StatusId, string StatusName, string Symbol, string Source,
    long Revision, string CommittedAtUtc);
public sealed record ReportOperation(string Id, string Date, string ExecutedAtUtc, string Operation,
    string Actor, string Description, string Reason);
public sealed record PersonLifetimeReport(PersonReportSummary Summary, ReportPage<ReportIdentity> Identities);
public sealed record ForceReportRow(string PersonId, string Name, string ServiceNumberA, long ApprovedDays,
    long InternalTransfers, IReadOnlyList<ReportStatusDays> StatusDays);
public sealed record ForceReportPage(string FromDate, string ToDate, ReportPage<ForceReportRow> Rows,
    IReadOnlyList<ReportStatusDays> StatusTotals, long ApprovedDays, long InternalTransfers);
