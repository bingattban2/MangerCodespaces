namespace AlTamam.Contracts.Dtos;
public sealed record ExportQuery(string Kind, int Year = 0, int Month = 0, string? SiteId = null, ReportQuery? Report = null, string? HistoryQueryToken = null);
public sealed record ExportPreview(string PreviewHash, long RowCount, IReadOnlyList<string> Files, string Extension);
public sealed record CommitExport(ExportQuery Query, string PreviewHash);
public sealed record ExportJobQuery(string JobId);
public sealed record ExportJobState(string JobId, string State, long WrittenRows, long TotalRows, string FileName, string? Error = null);
