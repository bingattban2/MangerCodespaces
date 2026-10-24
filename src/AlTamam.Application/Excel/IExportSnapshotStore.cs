using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
namespace AlTamam.Application.Excel;
public sealed record ExportDocument(string FileName, IReadOnlyList<WorkbookSheet> Sheets);
public sealed record ExportSnapshot(IReadOnlyList<ExportDocument> Documents, string Hash, long RowCount);
public interface IExportSnapshotStore
{
    Task<ServiceResult<ExportSnapshot>> CreateExportSnapshotAsync(string nonce, ExportQuery query, CancellationToken token = default);
}
