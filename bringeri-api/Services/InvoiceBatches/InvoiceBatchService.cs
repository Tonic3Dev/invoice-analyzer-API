using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Security.Claims;
using AutoMapper;
using bringeri_api.Data;
using bringeri_api.DTOs.InvoiceBatches;
using bringeri_api.Entities.Invoices;
using bringeri_api.Services.Serenity;
using bringeri_api.Services.TenantProvider;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace bringeri_api.Services.InvoiceBatches;

public class InvoiceBatchService : IInvoiceBatchService
{
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenantProvider;
    private readonly ISerenityInvoiceAgentService _serenityInvoiceAgentService;
    private readonly IMapper _mapper;

    public InvoiceBatchService(
        AppDbContext db,
        ITenantProvider tenantProvider,
        ISerenityInvoiceAgentService serenityInvoiceAgentService,
        IMapper mapper)
    {
        _db = db;
        _tenantProvider = tenantProvider;
        _serenityInvoiceAgentService = serenityInvoiceAgentService;
        _mapper = mapper;
    }

    public async Task<InvoiceBatchPreviewDto> PreviewAsync(string? title, IReadOnlyList<IFormFile> files, CancellationToken cancellationToken = default)
    {
        EnsureTenant();
        EnsureFiles(files);

        var invoices = new List<InvoiceEditorDto>(files.Count);
        foreach (var file in files)
        {
            invoices.Add(await _serenityInvoiceAgentService.AnalyzeInvoiceAsync(file, cancellationToken));
        }

        return new InvoiceBatchPreviewDto
        {
            Title = ResolveTitle(title, files),
            InvoiceCount = invoices.Count,
            Invoices = invoices,
        };
    }

    public async Task<InvoiceBatchDetailDto> SaveAsync(
        ClaimsPrincipal principal,
        InvoiceBatchUpsertRequest request,
        IReadOnlyList<IFormFile> files,
        CancellationToken cancellationToken = default)
    {
        EnsureTenant();
        EnsureFiles(files);

        if (request.Invoices.Count != files.Count)
        {
            throw new InvalidOperationException("The number of reviewed invoices must match the number of uploaded files.");
        }

        var currentUserId = ResolveCurrentUserId(principal);
        var tenantId = _tenantProvider.TenantId!.Value;

        var batch = new InvoiceBatch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CreatedByUserId = currentUserId,
            Title = ResolveTitle(request.Title, files),
            Status = InvoiceBatchStatus.Ready,
            InvoiceCount = request.Invoices.Count,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        for (var index = 0; index < request.Invoices.Count; index++)
        {
            var invoiceRequest = request.Invoices[index];
            var file = files[index];
            var fileContent = await ReadFileAsync(file, cancellationToken);

            var invoice = new InvoiceDocument
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                InvoiceBatchId = batch.Id,
                OriginalFileName = file.FileName,
                ContentType = file.ContentType ?? "application/octet-stream",
                FileSize = file.Length,
                FileContent = fileContent,
                Status = InvoiceDocumentStatus.Ready,
                RawAgentResponse = invoiceRequest.RawAgentResponse,
                IssuerLegalName = invoiceRequest.Issuer.LegalName,
                IssuerTaxId = invoiceRequest.Issuer.TaxId,
                IssuerTaxStatus = invoiceRequest.Issuer.TaxStatus,
                RecipientLegalName = invoiceRequest.Recipient.LegalName,
                RecipientTaxId = invoiceRequest.Recipient.TaxId,
                DocumentType = invoiceRequest.Document.Type,
                PointOfSaleNumber = invoiceRequest.Document.PosNumber,
                DocumentNumber = invoiceRequest.Document.Number,
                IssueDate = ParseDateOnly(invoiceRequest.Document.IssueDate),
                FiscalAuthCode = invoiceRequest.Document.FiscalAuthCode,
                FiscalAuthExpiry = ParseDateOnly(invoiceRequest.Document.FiscalAuthExpiry),
                Currency = invoiceRequest.Totals.Currency,
                NetSubtotal = invoiceRequest.Totals.NetSubtotal,
                Vat21 = invoiceRequest.Totals.Vat21,
                GrossIncomePerceptions = invoiceRequest.Totals.GrossIncomePerceptions,
                TotalAmount = invoiceRequest.Totals.TotalAmount,
                SortOrder = index,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            invoice.Items = invoiceRequest.Items
                .Select((item, itemIndex) => new InvoiceLineItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    InvoiceDocumentId = invoice.Id,
                    SkuCode = item.SkuCode,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.LineTotal,
                    SortOrder = itemIndex,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                })
                .ToList();

            batch.Invoices.Add(invoice);
        }

        _db.Set<InvoiceBatch>().Add(batch);
        await _db.SaveChangesAsync(cancellationToken);

        return await GetAsync(batch.Id, canEdit: true, cancellationToken)
            ?? throw new InvalidOperationException("Saved invoice batch could not be reloaded.");
    }

    public async Task<IReadOnlyList<InvoiceBatchSummaryDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        EnsureTenant();

        var batches = await _db.Set<InvoiceBatch>()
            .AsNoTracking()
            .Include(batch => batch.Invoices)
            .OrderByDescending(batch => batch.CreatedAt)
            .ToListAsync(cancellationToken);

        return batches.Select(batch => new InvoiceBatchSummaryDto
        {
            Id = batch.Id.ToString(),
            Title = batch.Title,
            Status = batch.Status.ToString().ToLowerInvariant(),
            InvoiceCount = batch.InvoiceCount,
            ProviderSummary = BuildSummary(batch.Invoices.Select(invoice => invoice.IssuerLegalName)),
            RecipientSummary = BuildSummary(batch.Invoices.Select(invoice => invoice.RecipientLegalName)),
            TotalAmount = batch.Invoices.Sum(invoice => invoice.TotalAmount),
            UploadedAt = batch.CreatedAt,
        }).ToList();
    }

    public async Task<InvoiceBatchDetailDto?> GetAsync(Guid batchId, bool canEdit, CancellationToken cancellationToken = default)
    {
        EnsureTenant();

        var batch = await _db.Set<InvoiceBatch>()
            .AsNoTracking()
            .Include(invoiceBatch => invoiceBatch.Invoices)
            .ThenInclude(invoice => invoice.Items)
            .FirstOrDefaultAsync(invoiceBatch => invoiceBatch.Id == batchId, cancellationToken);

        if (batch == null)
        {
            return null;
        }

        return new InvoiceBatchDetailDto
        {
            Id = batch.Id.ToString(),
            Title = batch.Title,
            Status = batch.Status.ToString().ToLowerInvariant(),
            InvoiceCount = batch.InvoiceCount,
            UploadedAt = batch.CreatedAt,
            UpdatedAt = batch.UpdatedAt,
            CanEdit = canEdit,
            Invoices = batch.Invoices
                .OrderBy(invoice => invoice.SortOrder)
                .Select(invoice => _mapper.Map<InvoiceEditorDto>(invoice))
                .ToList(),
        };
    }

    public async Task<InvoiceBatchDetailDto?> UpdateAsync(Guid batchId, InvoiceBatchUpsertRequest request, bool canEdit, CancellationToken cancellationToken = default)
    {
        EnsureTenant();

        if (!canEdit)
        {
            throw new UnauthorizedAccessException("Only admins can edit saved invoice batches.");
        }

        var batch = await _db.Set<InvoiceBatch>()
            .Include(invoiceBatch => invoiceBatch.Invoices)
            .ThenInclude(invoice => invoice.Items)
            .FirstOrDefaultAsync(invoiceBatch => invoiceBatch.Id == batchId, cancellationToken);

        if (batch == null)
        {
            return null;
        }

        if (request.Invoices.Count != batch.Invoices.Count)
        {
            throw new InvalidOperationException("Saved batches cannot change the number of invoices.");
        }

        batch.Title = string.IsNullOrWhiteSpace(request.Title) ? batch.Title : request.Title.Trim();
        batch.UpdatedAt = DateTime.UtcNow;

        var orderedInvoices = batch.Invoices.OrderBy(invoice => invoice.SortOrder).ToList();
        for (var index = 0; index < request.Invoices.Count; index++)
        {
            var source = request.Invoices[index];
            var target = orderedInvoices[index];

            target.IssuerLegalName = source.Issuer.LegalName;
            target.IssuerTaxId = source.Issuer.TaxId;
            target.IssuerTaxStatus = source.Issuer.TaxStatus;
            target.RecipientLegalName = source.Recipient.LegalName;
            target.RecipientTaxId = source.Recipient.TaxId;
            target.DocumentType = source.Document.Type;
            target.PointOfSaleNumber = source.Document.PosNumber;
            target.DocumentNumber = source.Document.Number;
            target.IssueDate = ParseDateOnly(source.Document.IssueDate);
            target.FiscalAuthCode = source.Document.FiscalAuthCode;
            target.FiscalAuthExpiry = ParseDateOnly(source.Document.FiscalAuthExpiry);
            target.Currency = source.Totals.Currency;
            target.NetSubtotal = source.Totals.NetSubtotal;
            target.Vat21 = source.Totals.Vat21;
            target.GrossIncomePerceptions = source.Totals.GrossIncomePerceptions;
            target.TotalAmount = source.Totals.TotalAmount;
            target.RawAgentResponse = source.RawAgentResponse;
            target.UpdatedAt = DateTime.UtcNow;

            _db.Set<InvoiceLineItem>().RemoveRange(target.Items);
            target.Items = source.Items.Select((item, itemIndex) => new InvoiceLineItem
            {
                Id = Guid.NewGuid(),
                TenantId = target.TenantId,
                InvoiceDocumentId = target.Id,
                SkuCode = item.SkuCode,
                Description = item.Description,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                LineTotal = item.LineTotal,
                SortOrder = itemIndex,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }).ToList();
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(batchId, canEdit, cancellationToken);
    }

    public async Task<(byte[] Content, string FileName, string ContentType)?> DownloadBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        EnsureTenant();

        var batch = await _db.Set<InvoiceBatch>()
            .AsNoTracking()
            .Include(invoiceBatch => invoiceBatch.Invoices)
            .FirstOrDefaultAsync(invoiceBatch => invoiceBatch.Id == batchId, cancellationToken);

        if (batch == null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach (var invoice in batch.Invoices.OrderBy(invoice => invoice.SortOrder))
            {
                var entry = archive.CreateEntry(invoice.OriginalFileName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(invoice.FileContent, cancellationToken);
            }
        }

        return (memory.ToArray(), $"{SanitizeFileName(batch.Title)}.zip", "application/zip");
    }

    public async Task<(byte[] Content, string FileName, string ContentType)?> DownloadFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        EnsureTenant();

        var file = await _db.Set<InvoiceDocument>()
            .AsNoTracking()
            .FirstOrDefaultAsync(invoice => invoice.Id == fileId, cancellationToken);

        if (file == null)
        {
            return null;
        }

        return (file.FileContent, file.OriginalFileName, file.ContentType);
    }

    private void EnsureTenant()
    {
        if (!_tenantProvider.HasTenant || !_tenantProvider.TenantId.HasValue)
        {
            throw new InvalidOperationException("A valid tenant context is required.");
        }
    }

    private static void EnsureFiles(IReadOnlyList<IFormFile> files)
    {
        if (files.Count == 0)
        {
            throw new InvalidOperationException("At least one invoice file is required.");
        }
    }

    private static string ResolveTitle(string? requestedTitle, IReadOnlyList<IFormFile> files)
    {
        if (!string.IsNullOrWhiteSpace(requestedTitle))
        {
            return requestedTitle.Trim();
        }

        if (files.Count == 1)
        {
            return Path.GetFileNameWithoutExtension(files[0].FileName);
        }

        return $"Invoice batch {DateTime.UtcNow:yyyy-MM-dd HH:mm}";
    }

    private static Guid ResolveCurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(value, out var userId))
        {
            throw new InvalidOperationException("Current user id could not be resolved.");
        }

        return userId;
    }

    private static async Task<byte[]> ReadFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        return stream.ToArray();
    }

    private static DateOnly? ParseDateOnly(string? value)
    {
        return DateOnly.TryParse(value, out var date) ? date : null;
    }

    private static string BuildSummary(IEnumerable<string> values)
    {
        var entries = values
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return entries.Count == 0 ? "-" : string.Join(", ", entries);
    }

    private static string SanitizeFileName(string title)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(title.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "invoice-batch" : sanitized;
    }
}