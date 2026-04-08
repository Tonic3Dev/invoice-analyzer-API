using bringeri_api.Entities;

namespace bringeri_api.Entities.Invoices;

public class InvoiceDocument
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public Guid InvoiceBatchId { get; set; }

    public InvoiceBatch InvoiceBatch { get; set; } = null!;

    public string OriginalFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long FileSize { get; set; }

    public byte[] FileContent { get; set; } = Array.Empty<byte>();

    public InvoiceDocumentStatus Status { get; set; } = InvoiceDocumentStatus.Ready;

    public string? RawAgentResponse { get; set; }

    public string IssuerLegalName { get; set; } = string.Empty;

    public string IssuerTaxId { get; set; } = string.Empty;

    public string IssuerTaxStatus { get; set; } = string.Empty;

    public string RecipientLegalName { get; set; } = string.Empty;

    public string RecipientTaxId { get; set; } = string.Empty;

    public string DocumentType { get; set; } = string.Empty;

    public string PointOfSaleNumber { get; set; } = string.Empty;

    public string DocumentNumber { get; set; } = string.Empty;

    public DateOnly? IssueDate { get; set; }

    public string FiscalAuthCode { get; set; } = string.Empty;

    public DateOnly? FiscalAuthExpiry { get; set; }

    public string Currency { get; set; } = string.Empty;

    public decimal NetSubtotal { get; set; }

    public decimal Vat21 { get; set; }

    public decimal GrossIncomePerceptions { get; set; }

    public decimal TotalAmount { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<InvoiceLineItem> Items { get; set; } = new List<InvoiceLineItem>();
}