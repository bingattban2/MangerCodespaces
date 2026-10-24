using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlTamam.Application.Excel;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using AlTamam.Domain.Policies;
using Microsoft.Data.Sqlite;
using static AlTamam.Infrastructure.Persistence.SqliteOperations;
namespace AlTamam.Infrastructure.Persistence;

public sealed partial class SqliteRegistryStore : IExportSnapshotStore
{
    private const int ExportMaximumRows = 100000;
    private static WorkbookCell Cell(string? text) => new(text ?? "");
    private static WorkbookCell CountCell(long number) => WorkbookCell.Number(number);
    private static WorkbookSheet Sheet(string name, string[] headers, IReadOnlyList<WorkbookCell>[] rows, int frozen = 4) => new(name, headers, rows, Math.Min(frozen, headers.Length));
    private static void ExportBound(int count) => Require(count <= ExportMaximumRows, "حجم التقرير يتجاوز مائة ألف صف؛ ضيّق نطاق التاريخ أو اختر موقعاً واحداً.");
    public Task<ServiceResult<ExportSnapshot>> CreateExportSnapshotAsync(string nonce, ExportQuery query, CancellationToken token = default) => ReadAsync(nonce, async (c, actor) =>
    {
        Require(query.Kind is "monthly" or "person" or "force", "نوع التصدير غير صحيح.");
        using var t = c.BeginTransaction(deferred: true);
        List<ExportDocument> documents;
        if (query.Kind == "monthly") documents = await MonthlyExportAsync(c, t, query, token).ConfigureAwait(false);
        else
        {
            Require(query.Report is not null && query.Year == 0 && query.Month == 0 && query.SiteId is null, "معايير تصدير التقرير غير صحيحة.");
            var q = ValidateReport(query.Report!, query.Kind == "person") with { Offset = 0, Limit = 100 };
            if (query.HistoryQueryToken is not null) Require(query.HistoryQueryToken == await ReportTokenAsync(c, t, q).ConfigureAwait(false), "تغيرت البيانات منذ عرض التقرير؛ اعرضه مجدداً قبل التصدير.", BridgeErrorCodes.ConflictStale);
            documents = query.Kind == "person" ? await PersonExportAsync(c, t, q, token).ConfigureAwait(false) : await ForceExportAsync(c, t, q, token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        var count = documents.Sum(d => d.Sheets.Sum(s => (long)s.Rows.Count()));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { query, documents }))));
        t.Commit(); return new ExportSnapshot(documents, hash, count);
    }, token);
    private async Task<List<ExportDocument>> MonthlyExportAsync(SqliteConnection c, SqliteTransaction t, ExportQuery q, CancellationToken token)
    {
        Require(q.Report is null && q.Year is >= 2020 and <= 9998 && q.Month is >= 1 and <= 12 && (q.SiteId is null || q.SiteId.Length is > 0 and <= 100), "اختر الشهر والسنة والموقع الصحيح.");
        var first = new DateOnly(q.Year, q.Month, 1); Require(first <= clock.CurrentBusinessDate, "لا يمكن تصدير شهر مستقبلي.");
        var last = first.AddMonths(1).AddDays(-1); var asof = last < clock.CurrentBusinessDate ? last : clock.CurrentBusinessDate;
        MonthFunctions(c);
        var p = new (string, object?)[] { ("$first", first.ToString("yyyy-MM-dd")), ("$last", last.ToString("yyyy-MM-dd")), ("$asof", asof.ToString("yyyy-MM-dd")), ("$site", q.SiteId), ("$search", "%"), ("$number", "%") };
        var keys = await QueryAsync(c, t, MonthKeysSql + " ORDER BY i.NameSnapshot,m.PersonId,m.SiteId LIMIT 100001", r => new MonthKey(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)), p).ConfigureAwait(false);
        Require(keys.Count <= 20000, "الكشف كبير؛ اختر موقعاً واحداً للتصدير.");
        var rows = new List<MonthlyRow>();
        foreach (var batch in keys.Chunk(500)) { token.ThrowIfCancellationRequested(); rows.AddRange(await BuildMonthRowsAsync(c, t, batch, first, last, token).ConfigureAwait(false)); }
        var headers = new[] { "الرقم", "الرقم (ع)", "الاسم", "الموقع" }.Concat(Enumerable.Range(1, last.Day).Select(d => d.ToString(CultureInfo.InvariantCulture))).ToArray();
        ExportDocument Document(string file, string name, IReadOnlyList<MonthlyRow> people)
        {
            var data = people.Select((r, index) => (IReadOnlyList<WorkbookCell>)new[] { CountCell(index + 1), Cell(r.ServiceNumberA), Cell(r.Name), Cell(r.SiteName) }.Concat(r.Cells.Select(cell => Cell(cell.Kind switch { "attendance" => cell.Symbol, "empty" => "", "unprepared" => "—", _ => cell.DisplayLabelSnapshot }))).ToArray()).ToArray();
            var legend = people.SelectMany(r => r.Cells).Where(cell => cell.Kind == "attendance").Select(cell => (cell.StatusId, cell.DisplayLabelSnapshot, cell.Symbol)).Distinct().OrderBy(cell => cell.StatusId, StringComparer.Ordinal).ThenBy(cell => cell.DisplayLabelSnapshot, StringComparer.Ordinal)
                .Select(cell => (IReadOnlyList<WorkbookCell>)new[] { Cell(cell.StatusId), Cell(cell.DisplayLabelSnapshot), Cell(cell.Symbol) }).ToArray();
            return new(file, [Sheet(name, headers, data), Sheet("الحالات المستخدمة", ["معرف الحالة", "اسم الحالة التاريخي", "الرمز"], legend, 0)]);
        }
        var result = new List<ExportDocument> { Document("التمام-الشهري.xlsx", $"تمام {q.Year}-{q.Month:00}", rows) };
        if (q.SiteId is null)
        {
            var sites = await QueryAsync(c, t, "SELECT SiteId,VisibleNameSnapshot FROM SiteNameHistory WHERE ValidFromLocalDate<=$asof AND (ValidToLocalDate IS NULL OR ValidToLocalDate>$asof) ORDER BY SiteId LIMIT 1001", r => (Id: r.GetString(0), Name: r.GetString(1)), p).ConfigureAwait(false);
            Require(sites.Count <= 1000, "عدد المواقع كبير؛ صدّر كل موقع على حدة.");
            var grouped = rows.ToLookup(r => r.SiteId); var index = 0;
            foreach (var site in sites)
            {
                token.ThrowIfCancellationRequested();
                var name = new string(site.Name.Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_').Take(60).ToArray()).Trim();
                result.Add(Document($"موقع-{++index:000}-{name}.xlsx", site.Name, grouped[site.Id].ToArray()));
            }
        }
        return result;
    }
    private async Task<List<ExportDocument>> PersonExportAsync(SqliteConnection c, SqliteTransaction t, ReportQuery q, CancellationToken token)
    {
        var summary = await PersonSummaryAsync(c, t, q).ConfigureAwait(false); var p = ReportParameters(q);
        var identities = await QueryAsync(c, t, "SELECT NameSnapshot,ServiceNumberASnapshot,ValidFromLocalDate,COALESCE(ValidToLocalDate,''),Reason FROM PersonIdentityHistory WHERE PersonId=$person AND ValidFromLocalDate<=$to AND (ValidToLocalDate IS NULL OR ValidToLocalDate>$from) ORDER BY ValidFromLocalDate,HistoryId LIMIT 100001", r => (IReadOnlyList<WorkbookCell>)Enumerable.Range(0, 5).Select(i => Cell(r.GetString(i))).ToArray(), p).ConfigureAwait(false); ExportBound(identities.Count);
        var attendance = await QueryAsync(c, t, """
            SELECT a.AttendanceLocalDate,i.NameSnapshot,i.ServiceNumberASnapshot,a.SiteNameSnapshot,a.StatusNameSnapshot,a.StatusSymbolSnapshot,a.Source,a.RevisionNumber,a.CommittedAtUtc
            FROM AttendanceRecords a JOIN PersonIdentityHistory i ON i.PersonId=a.PersonId AND i.ValidFromLocalDate<=a.AttendanceLocalDate AND (i.ValidToLocalDate IS NULL OR i.ValidToLocalDate>a.AttendanceLocalDate)
            WHERE a.PersonId=$person AND a.AttendanceLocalDate BETWEEN $from AND $to AND ($site IS NULL OR a.SiteIdAtEffectiveDate=$site)
            ORDER BY a.AttendanceLocalDate,a.RecordId LIMIT 100001
            """, r => (IReadOnlyList<WorkbookCell>)[Cell(r.GetString(0)), Cell(r.GetString(1)), Cell(r.GetString(2)), Cell(r.GetString(3)), Cell(r.GetString(4)), Cell(r.GetString(5)), Cell(r.GetString(6)), CountCell(r.GetInt64(7)), Cell(r.GetString(8))], p).ConfigureAwait(false); ExportBound(attendance.Count);
        var movements = await QueryAsync(c, t, MovementArchiveSelect + " WHERE PersonId=$person AND EffectiveLocalDate BETWEEN $from AND $to AND ($site IS NULL OR SourceSiteId=$site OR DestinationSiteId=$site) ORDER BY EffectiveLocalDate,MovementId LIMIT 100001", MovementRow, p).ConfigureAwait(false); ExportBound(movements.Count);
        var operations = await QueryAsync(c, t, "SELECT a.EffectiveLocalDate,a.ExecutedAtUtc,a.OperationArabicName,a.ActorUsernameSnapshot,COALESCE(a.ActorRosterFullNameSnapshot,''),a.DefaultArabicDescription,COALESCE(a.Reason,'') FROM AuditEvents a " + PersonOperationFilter + " ORDER BY a.ExecutedAtUtc,a.rowid LIMIT 100001", r => (IReadOnlyList<WorkbookCell>)Enumerable.Range(0, 7).Select(i => Cell(r.GetString(i))).ToArray(), p).ConfigureAwait(false); ExportBound(operations.Count);
        token.ThrowIfCancellationRequested();
        return [new("تقرير-الفرد.xlsx", [
            Sheet("ملخص", ["الفرد", "الرقم (ع)", "القيد", "من", "إلى", "الأيام المعتمدة", "النقل الداخلي"], [[Cell(summary.Name), Cell(summary.ServiceNumberA), Cell(summary.EnrollmentDate), Cell(summary.FromDate), Cell(summary.ToDate), CountCell(summary.ApprovedDays), CountCell(summary.InternalTransfers)]]),
            Sheet("أيام الحالات", ["معرف الحالة", "الحالة التاريخية", "الرمز", "الأيام"], summary.StatusDays.Select(s => (IReadOnlyList<WorkbookCell>)[Cell(s.StatusId), Cell(s.Caption), Cell(s.Symbol), CountCell(s.Days)]).ToArray()),
            Sheet("تاريخ الهوية", ["الاسم", "الرقم (ع)", "من", "إلى (غير شامل)", "السبب"], identities.ToArray()),
            Sheet("الحضور", ["التاريخ", "الاسم التاريخي", "الرقم (ع)", "الموقع التاريخي", "الحالة", "الرمز", "المصدر", "المراجعة", "وقت الاعتماد"], attendance.ToArray()),
            Sheet("الحركات", ["التاريخ", "الاسم التاريخي", "الرقم (ع)", "نوع الحركة", "من", "إلى", "السبب"], movements.Select(m => (IReadOnlyList<WorkbookCell>)[Cell(m.EffectiveDate), Cell(m.PersonName), Cell(m.ServiceNumberA), Cell(m.Type == 1 ? "نقل داخلي" : m.Type == 2 ? "نقل خارجي" : "فصل"), Cell(m.SourceSiteName), Cell(m.DestinationName), Cell(m.Reason)]).ToArray()),
            Sheet("العمليات", ["التاريخ", "وقت التنفيذ", "العملية", "اسم المستخدم", "الاسم الكامل وقت العملية", "الوصف", "السبب"], operations.ToArray())])];
    }
    private async Task<List<ExportDocument>> ForceExportAsync(SqliteConnection c, SqliteTransaction t, ReportQuery q, CancellationToken token)
    {
        MonthFunctions(c); var p = ReportParameters(q).Concat(new (string, object?)[] { ("$search", MonthSearch(ArabicTextNormalizer.NormalizeForSearch(q.Search))), ("$number", MonthSearch(ArabicTextNormalizer.NormalizeDigitsOnly(q.Search))) }).ToArray();
        var keys = await QueryAsync(c, t, ForcePeopleSql + " ORDER BY i.NameSnapshot,p.PersonId LIMIT 100001", r => (Id: r.GetString(0), Name: r.GetString(1), Number: r.GetString(2)), p).ConfigureAwait(false); ExportBound(keys.Count);
        var parameters = p.Concat(new (string, object?)[] { ("$ids", JsonSerializer.Serialize(keys.Select(k => k.Id))) }).ToArray();
        var counts = await QueryAsync(c, t, "SELECT PersonId,StatusId,StatusNameSnapshot,StatusSymbolSnapshot,COUNT(*) FROM AttendanceRecords WHERE PersonId IN(SELECT value FROM json_each($ids)) AND AttendanceLocalDate BETWEEN $from AND $to AND ($site IS NULL OR SiteIdAtEffectiveDate=$site) GROUP BY PersonId,StatusId,StatusNameSnapshot,StatusSymbolSnapshot", r => (Person: r.GetString(0), Key: (Id: r.GetString(1), Name: r.GetString(2), Symbol: r.GetString(3)), Days: r.GetInt64(4)), parameters).ConfigureAwait(false);
        var movements = await QueryAsync(c, t, "SELECT PersonId,COUNT(*) FROM PersonnelMovements WHERE PersonId IN(SELECT value FROM json_each($ids)) AND MovementType=1 AND EffectiveLocalDate BETWEEN $from AND $to AND ($site IS NULL OR SourceSiteId=$site OR DestinationSiteId=$site) GROUP BY PersonId", r => (Id: r.GetString(0), Count: r.GetInt64(1)), parameters).ConfigureAwait(false);
        var columns = counts.Select(s => s.Key).Distinct().OrderBy(s => s.Id, StringComparer.Ordinal).ThenBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.Symbol, StringComparer.Ordinal).ToArray();
        Require(columns.Length <= 500 && (long)keys.Count * (columns.Length + 5) <= 1000000, "عدد أعمدة الحالات كبير؛ ضيّق نطاق التقرير.");
        var days = counts.ToDictionary(s => (s.Person, s.Key), s => s.Days); var moves = movements.ToDictionary(m => m.Id, m => m.Count);
        var headers = new[] { "الرقم", "الرقم (ع)", "الاسم", "النقل الداخلي" }.Concat(columns.Select(s => s.Name + " (" + s.Symbol + ")")).Concat(["مجموع الأيام"]).ToArray();
        var rows = keys.Select((person, index) => { token.ThrowIfCancellationRequested(); var values = columns.Select(s => days.GetValueOrDefault((person.Id, s))).ToArray(); return (IReadOnlyList<WorkbookCell>)new[] { CountCell(index + 1), Cell(person.Number), Cell(person.Name), CountCell(moves.GetValueOrDefault(person.Id)) }.Concat(values.Select(CountCell)).Concat([CountCell(values.Sum())]).ToArray(); }).ToList();
        rows.Add(new[] { Cell(""), Cell(""), Cell("الإجمالي"), CountCell(movements.Sum(m => m.Count)) }.Concat(columns.Select(col => CountCell(counts.Where(s => s.Key == col).Sum(s => s.Days)))).Concat([CountCell(counts.Sum(s => s.Days))]).ToArray());
        return [new("تقرير-القوة.xlsx", [Sheet("تقرير القوة الشامل", headers, rows.ToArray()), Sheet("نطاق التقرير", ["من", "إلى", "الموقع", "البحث"], [[Cell(q.FromDate), Cell(q.ToDate), Cell(q.SiteId ?? "جميع المواقع"), Cell(q.Search)]])])];
    }
}
