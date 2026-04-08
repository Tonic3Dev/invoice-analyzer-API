using bringeri_api.DTOs.InvoiceBatches;
using Microsoft.AspNetCore.Http;

namespace bringeri_api.Services.Serenity;

public interface ISerenityInvoiceAgentService
{
    Task<InvoiceEditorDto> AnalyzeInvoiceAsync(IFormFile file, CancellationToken cancellationToken = default);
}