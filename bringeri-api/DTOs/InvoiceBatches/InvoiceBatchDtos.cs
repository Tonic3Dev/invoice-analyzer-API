namespace bringeri_api.DTOs.InvoiceBatches;

public class InvoicePartyDto
{
    public string LegalName { get; set; } = string.Empty;

    public string TaxId { get; set; } = string.Empty;

    public string TaxStatus { get; set; } = string.Empty;
}

public class InvoiceDocumentInfoDto
{
    public string Type { get; set; } = string.Empty;

    public string PosNumber { get; set; } = string.Empty;

    public string Number { get; set; } = string.Empty;

    public string IssueDate { get; set; } = string.Empty;

    public string FiscalAuthCode { get; set; } = string.Empty;

    public string FiscalAuthExpiry { get; set; } = string.Empty;
}

public class InvoiceTotalsDto
{
    public string Currency { get; set; } = string.Empty;

    public decimal NetSubtotal { get; set; }

    public decimal Vat21 { get; set; }

    public decimal GrossIncomePerceptions { get; set; }

    public decimal TotalAmount { get; set; }
}

public class InvoiceLineItemDto
{
    public string SkuCode { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }
}

public class InvoiceEditorDto
{
    public string? InvoiceId { get; set; }

    public string? FileId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long FileSize { get; set; }

    public string RawAgentResponse { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public InvoicePartyDto Issuer { get; set; } = new();

    public InvoiceDocumentInfoDto Document { get; set; } = new();

    public InvoicePartyDto Recipient { get; set; } = new();

    public InvoiceTotalsDto Totals { get; set; } = new();

    public List<InvoiceLineItemDto> Items { get; set; } = new();
}

public class InvoiceBatchPreviewDto
{
    public string Title { get; set; } = string.Empty;

    public int InvoiceCount { get; set; }

    public List<InvoiceEditorDto> Invoices { get; set; } = new();
}

public class InvoiceBatchUpsertRequest
{
    public string Title { get; set; } = string.Empty;

    public List<InvoiceEditorDto> Invoices { get; set; } = new();
}

public class InvoiceBatchSummaryDto
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int InvoiceCount { get; set; }

    public string ProviderSummary { get; set; } = string.Empty;

    public string RecipientSummary { get; set; } = string.Empty;

    public decimal TotalAmount { get; set; }

    public DateTime UploadedAt { get; set; }
}

public class InvoiceBatchDetailDto
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int InvoiceCount { get; set; }

    public DateTime UploadedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool CanEdit { get; set; }

    public List<InvoiceEditorDto> Invoices { get; set; } = new();
}