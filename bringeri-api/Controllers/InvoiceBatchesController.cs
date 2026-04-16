using System.Text.Json;
using bringeri_api.DTOs.InvoiceBatches;
using bringeri_api.Services.InvoiceBatches;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace bringeri_api.Controllers;

[ApiController]
[Authorize]
[Route("api/invoice-batches")]
public class InvoiceBatchesController : ControllerBase
{
    private readonly IInvoiceBatchService _invoiceBatchService;

    public InvoiceBatchesController(IInvoiceBatchService invoiceBatchService)
    {
        _invoiceBatchService = invoiceBatchService;
    }

    [HttpPost("preview")]
    [RequestFormLimits(MultipartBodyLengthLimit = 25_000_000)]
    public async Task<ActionResult<InvoiceBatchPreviewDto>> Preview(
        [FromForm] string? title,
        [FromForm] List<IFormFile> files,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _invoiceBatchService.PreviewAsync(title, files, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return CorrelatedBadRequest(ex.Message);
        }
    }

    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = 25_000_000)]
    public async Task<ActionResult<InvoiceBatchDetailDto>> Save(
        [FromForm] string request,
        [FromForm] List<IFormFile> files,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = DeserializeRequest(request);
            var result = await _invoiceBatchService.SaveAsync(User, payload, files, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { batchId = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return CorrelatedBadRequest(ex.Message);
        }
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvoiceBatchSummaryDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _invoiceBatchService.ListAsync(cancellationToken));
    }

    [HttpGet("{batchId:guid}")]
    public async Task<ActionResult<InvoiceBatchDetailDto>> GetById(Guid batchId, CancellationToken cancellationToken)
    {
        var canEdit = User.IsInRole("Admin");
        var result = await _invoiceBatchService.GetAsync(batchId, canEdit, cancellationToken);
        if (result == null)
        {
            return NotFound(new { message = "Invoice batch not found." });
        }

        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{batchId:guid}")]
    public async Task<ActionResult<InvoiceBatchDetailDto>> Update(
        Guid batchId,
        [FromBody] InvoiceBatchUpsertRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _invoiceBatchService.UpdateAsync(batchId, request, canEdit: true, cancellationToken);
            if (result == null)
            {
                return NotFound(new { message = "Invoice batch not found." });
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return CorrelatedBadRequest(ex.Message);
        }
    }

    [HttpGet("{batchId:guid}/download")]
    public async Task<IActionResult> DownloadBatch(Guid batchId, CancellationToken cancellationToken)
    {
        var file = await _invoiceBatchService.DownloadBatchAsync(batchId, cancellationToken);
        if (file == null)
        {
            return NotFound(new { message = "Invoice batch not found." });
        }

        return File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
    }

    [HttpGet("files/{fileId:guid}")]
    public async Task<IActionResult> DownloadFile(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await _invoiceBatchService.DownloadFileAsync(fileId, cancellationToken);
        if (file == null)
        {
            return NotFound(new { message = "Invoice file not found." });
        }

        return File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
    }

    private static InvoiceBatchUpsertRequest DeserializeRequest(string request)
    {
        var payload = JsonSerializer.Deserialize<InvoiceBatchUpsertRequest>(request, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        return payload ?? throw new InvalidOperationException("Invoice batch request payload was invalid.");
    }

    private BadRequestObjectResult CorrelatedBadRequest(string message)
    {
        Response.Headers["X-Correlation-ID"] = HttpContext.TraceIdentifier;
        return BadRequest(new
        {
            message,
            correlationId = HttpContext.TraceIdentifier,
        });
    }
}