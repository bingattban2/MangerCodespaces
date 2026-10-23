namespace AlTamam.Application.Excel;

// Text and numbers are separate at the application boundary. User-supplied text is never a formula.
public sealed record WorkbookCell(string Text, long? Integer = null)
{
    public static WorkbookCell Number(long value) => new("", value);
}
public sealed record WorkbookSheet(string Name, IReadOnlyList<string> Headers,
    IEnumerable<IReadOnlyList<WorkbookCell>> Rows, int FrozenColumns = 4);
public interface IWorkbookWriter
{
    // The caller owns staging and atomic publication; failed/cancelled output must never replace a user file.
    void Write(Stream destination, IReadOnlyList<WorkbookSheet> sheets,
        Action<long>? rowWritten = null, CancellationToken cancellationToken = default);
}
