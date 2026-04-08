using bringeri_api.Entities;

namespace bringeri_api.Entities.Invoices;

public class InvoiceLineItem
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public Guid InvoiceDocumentId { get; set; }

    public InvoiceDocument InvoiceDocument { get; set; } = null!;

    public string SkuCode { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}