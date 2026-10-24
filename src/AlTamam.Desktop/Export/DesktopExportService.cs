using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AlTamam.Application.Authentication;
using AlTamam.Application.Excel;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using Microsoft.Win32;
namespace AlTamam.Desktop.Export;

public sealed class DesktopExportService(IExportSnapshotStore snapshots, IWorkbookWriter writer, AuthenticationService authentication) : IDisposable
{
    private sealed class Job(string id, string nonce, string request, string fingerprint, ExportSnapshot snapshot, string path)
    {
        internal readonly object Gate = new();
        internal readonly string Nonce = nonce, Request = request, Fingerprint = fingerprint;
        internal readonly CancellationTokenSource Cancel = new();
        internal ExportSnapshot? Snapshot = snapshot;
        internal readonly string Path = path;
        internal ExportJobState State = new(id, "running", 0, snapshot.RowCount, System.IO.Path.GetFileName(path));
        internal Task Work = Task.CompletedTask;
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    private bool _disposed;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private static ExportPreview Preview(ExportSnapshot s) => new(s.Hash, s.RowCount, s.Documents.Select(d => d.FileName).ToArray(), s.Documents.Count > 1 ? ".zip" : ".xlsx");
    public async Task<ServiceResult<ExportPreview>> PreviewAsync(string nonce, ExportQuery query, CancellationToken token = default)
    {
        var result = await snapshots.CreateExportSnapshotAsync(nonce, query, token).ConfigureAwait(false);
        return result.IsSuccess ? ServiceResult<ExportPreview>.Ok(Preview(result.Value!)) : ServiceResult<ExportPreview>.Fail(result.Failure!);
    }
    private static Task<string?> ChoosePathAsync(ExportPreview preview) => System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
    {
        var dialog = new SaveFileDialog { Title = "حفظ التصدير", AddExtension = true, DefaultExt = preview.Extension,
            Filter = preview.Extension == ".zip" ? "مجموعة كشوف Excel (*.zip)|*.zip" : "Excel (*.xlsx)|*.xlsx",
            FileName = preview.Extension == ".zip" ? "كشوف-التمام.zip" : preview.Files[0], OverwritePrompt = true, CheckPathExists = true };
        return dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true ? dialog.FileName : null;
    }).Task;
    private static string? ExistingFileHash(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    public async Task<ServiceResult<ExportJobState>> StartAsync(string nonce, string requestId, CommitExport command, CancellationToken token = default)
    {
        await _startGate.WaitAsync(token).ConfigureAwait(false);
        try { return await StartCoreAsync(nonce, requestId, command, token).ConfigureAwait(false); }
        finally { _startGate.Release(); }
    }
    private async Task<ServiceResult<ExportJobState>> StartCoreAsync(string nonce, string requestId, CommitExport command, CancellationToken token)
    {
        if (!Guid.TryParseExact(requestId, "D", out _)) return ServiceResult<ExportJobState>.Fail(BridgeErrorCodes.ValidationError, "معرف الطلب غير صحيح.");
        var auth = await authentication.ValidateSessionAsync(nonce, token).ConfigureAwait(false);
        if (!auth.IsSuccess) return ServiceResult<ExportJobState>.Fail(auth.Failure!);
        var fingerprint = JsonSerializer.Serialize(command);
        lock (_gate)
        {
            if (_disposed) return ServiceResult<ExportJobState>.Fail(BridgeErrorCodes.StorageBusy, "البرنامج قيد الإغلاق.");
            var prior = _jobs.Values.FirstOrDefault(j => j.Request == requestId);
            if (prior is not null)
            {
                if (prior.Nonce != nonce || prior.Fingerprint != fingerprint) return ServiceResult<ExportJobState>.Fail(BridgeErrorCodes.ConflictStale, "معرف الطلب مستخدم لتصدير آخر.");
                lock (prior.Gate) return ServiceResult<ExportJobState>.Ok(prior.State);
            }
            if (_jobs.Values.Any(j => !j.Work.IsCompleted)) return ServiceResult<ExportJobState>.Fail(BridgeErrorCodes.StorageBusy, "انتظر اكتمال التصدير الحالي أو ألغِه.");
            if (_jobs.Count >= 64) return ServiceResult<ExportJobState>.Fail(BridgeErrorCodes.StorageBusy, "أعد تشغيل البرنامج بعد اكتمال التصدير لبدء دفعة جديدة.");
        }
        var result = await snapshots.CreateExportSnapshotAsync(nonce, command.Query, token).ConfigureAwait(false);
        if (!result.IsSuccess) return ServiceResult<ExportJobState>.Fail(result.Failure!);
        if (!StringComparer.Ordinal.Equals(result.Value!.Hash, command.PreviewHash)) return ServiceResult<ExportJobState>.Fail(BridgeErrorCodes.ConflictStale, "تغيرت بيانات التقرير؛ أعد المعاينة قبل التصدير.");
        var path = await ChoosePathAsync(Preview(result.Value)).ConfigureAwait(false);
        if (path is null) return ServiceResult<ExportJobState>.Ok(new("", "cancelled", 0, result.Value.RowCount, ""));
        token.ThrowIfCancellationRequested();
        string? existing;
        try { existing = ExistingFileHash(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ServiceResult<ExportJobState>.Fail(BridgeErrorCodes.ExcelExportFailed, "تعذر الوصول إلى الملف المختار؛ قد يكون مفتوحاً في برنامج آخر."); }
        var job = new Job(Guid.NewGuid().ToString("N"), nonce, requestId, fingerprint, result.Value, path);
        lock (_gate)
        {
            if (_disposed) { job.Cancel.Dispose(); return ServiceResult<ExportJobState>.Fail(BridgeErrorCodes.StorageBusy, "البرنامج قيد الإغلاق."); }
            _jobs.Add(job.State.JobId, job);
            job.Work = Task.Run(() => WriteAsync(job, existing));
        }
        return ServiceResult<ExportJobState>.Ok(job.State);
    }
    private async Task WriteAsync(Job job, string? originalHash)
    {
        var snapshot = job.Snapshot!;
        var temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(job.Path)!, ".altamam-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var token = job.Cancel.Token;
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                long completed = 0;
                void Progress(long rows) { lock (job.Gate) job.State = job.State with { WrittenRows = completed + rows }; }
                if (snapshot.Documents.Count == 1) writer.Write(stream, snapshot.Documents[0].Sheets, Progress, token);
                else
                {
                    using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                    foreach (var document in snapshot.Documents)
                    {
                        token.ThrowIfCancellationRequested(); using var entry = zip.CreateEntry(document.FileName, CompressionLevel.NoCompression).Open();
                        writer.Write(entry, document.Sheets, Progress, token); completed += document.Sheets.Sum(s => (long)s.Rows.Count());
                    }
                }
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            var auth = await authentication.ValidateSessionAsync(job.Nonce, token).ConfigureAwait(false);
            if (!auth.IsSuccess) throw new InvalidOperationException("انتهت صلاحية الجلسة؛ لم يُحفظ التصدير.");
            lock (job.Gate)
            {
                token.ThrowIfCancellationRequested();
                if (!StringComparer.Ordinal.Equals(originalHash, ExistingFileHash(job.Path))) throw new InvalidOperationException("تغير الملف المختار أثناء التصدير؛ لم يتم استبداله.");
                File.Move(temporary, job.Path, overwrite: true);
                job.State = job.State with { State = "completed", WrittenRows = snapshot.RowCount };
            }
        }
        catch (OperationCanceledException) { lock (job.Gate) job.State = job.State with { State = "cancelled" }; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Xml.XmlException)
        {
            var message = e is InvalidOperationException ? e.Message : "تعذر حفظ التصدير؛ تحقق من المساحة وصلاحية المجلد وإغلاق الملف في البرامج الأخرى.";
            lock (job.Gate) job.State = job.State with { State = "failed", Error = message };
        }
        finally { job.Snapshot = null; try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    public async Task<ServiceResult<ExportJobState>> StatusAsync(string nonce, ExportJobQuery query, bool cancel = false, CancellationToken token = default)
    {
        var auth = await authentication.ValidateSessionAsync(nonce, token).ConfigureAwait(false);
        if (!auth.IsSuccess) return ServiceResult<ExportJobState>.Fail(auth.Failure!);
        lock (_gate)
        {
            var job = query.JobId.Length == 0 ? _jobs.Values.LastOrDefault(j => j.Nonce == nonce) : _jobs.GetValueOrDefault(query.JobId);
            if (job is null || job.Nonce != nonce) return ServiceResult<ExportJobState>.Fail(BridgeErrorCodes.MissingData, "عملية التصدير غير موجودة في جلستك.");
            lock (job.Gate) { if (cancel && job.State.State == "running") job.Cancel.Cancel(); return ServiceResult<ExportJobState>.Ok(job.State); }
        }
    }
    public void Dispose()
    {
        Job[] jobs;
        lock (_gate) { if (_disposed) return; _disposed = true; jobs = _jobs.Values.ToArray(); foreach (var job in jobs) job.Cancel.Cancel(); }
        Task.WhenAll(jobs.Select(j => j.Work)).GetAwaiter().GetResult();
        foreach (var job in jobs) job.Cancel.Dispose();
    }
}
