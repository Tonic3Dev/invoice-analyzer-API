using bringeri_api.Entities;

namespace bringeri_api.Entities.Invoices;

public class InvoiceBatch
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Tenant Tenant { get; set; } = null!;

    public Guid CreatedByUserId { get; set; }

    public User CreatedByUser { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    public InvoiceBatchStatus Status { get; set; } = InvoiceBatchStatus.Ready;

    public int InvoiceCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<InvoiceDocument> Invoices { get; set; } = new List<InvoiceDocument>();
}