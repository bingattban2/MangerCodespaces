namespace AlTamam.Application.Attendance;

/// <summary>Desktop-owned lifecycle; no WebView, UI session or screen is needed to process due work.</summary>
public sealed class EightPmAutoAttendanceScheduler(IDayProcessingStore store) : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    public void Start()
    {
        if (_worker is not null) throw new InvalidOperationException("Scheduler already started.");
        _worker = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            var first = true;
            try
            {
                do
                {
                    try
                    {
                        var result = await store.ProcessDueAsync(first, _stop.Token).ConfigureAwait(false);
                        await store.RecordSchedulerFailureAsync(result.IsSuccess ? null : result.Failure!.UserMessageArabic, _stop.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                    catch (Exception)
                    {
                        try { await store.RecordSchedulerFailureAsync("تعذر تشغيل مهمة التحضير التلقائي؛ ستُعاد المحاولة تلقائياً.", _stop.Token).ConfigureAwait(false); }
                        catch (Exception) { /* The next cycle retries storage; never report a successful run. */ }
                    }
                    first = false;
                } while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        });
    }
    public void Dispose()
    {
        _stop.Cancel();
        try { _worker?.GetAwaiter().GetResult(); }
        finally { _stop.Dispose(); }
    }
}
