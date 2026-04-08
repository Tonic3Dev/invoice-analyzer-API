using System.Security.Claims;
using bringeri_api.DTOs.InvoiceBatches;
using Microsoft.AspNetCore.Http;

namespace bringeri_api.Services.InvoiceBatches;

public interface IInvoiceBatchService
{
    Task<InvoiceBatchPreviewDto> PreviewAsync(string? title, IReadOnlyList<IFormFile> files, CancellationToken cancellationToken = default);

    Task<InvoiceBatchDetailDto> SaveAsync(ClaimsPrincipal principal, InvoiceBatchUpsertRequest request, IReadOnlyList<IFormFile> files, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InvoiceBatchSummaryDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<InvoiceBatchDetailDto?> GetAsync(Guid batchId, bool canEdit, CancellationToken cancellationToken = default);

    Task<InvoiceBatchDetailDto?> UpdateAsync(Guid batchId, InvoiceBatchUpsertRequest request, bool canEdit, CancellationToken cancellationToken = default);

    Task<(byte[] Content, string FileName, string ContentType)?> DownloadBatchAsync(Guid batchId, CancellationToken cancellationToken = default);

    Task<(byte[] Content, string FileName, string ContentType)?> DownloadFileAsync(Guid fileId, CancellationToken cancellationToken = default);
}