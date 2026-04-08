namespace bringeri_api.Entities;

using bringeri_api.Entities.Invoices;

public class Tenant
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string PageTitle { get; set; } = "Invoice Analyzer";

    public string PrimaryColor { get; set; } = "#cb4b27";

    public string SecondaryColor { get; set; } = "#180901";

    public string DefaultLanguage { get; set; } = "en";

    public string? LogoBase64 { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<User> Users { get; set; } = new List<User>();

    public ICollection<InvoiceBatch> InvoiceBatches { get; set; } = new List<InvoiceBatch>();

    public ICollection<InvoiceDocument> InvoiceDocuments { get; set; } = new List<InvoiceDocument>();

    public ICollection<InvoiceLineItem> InvoiceLineItems { get; set; } = new List<InvoiceLineItem>();
}
