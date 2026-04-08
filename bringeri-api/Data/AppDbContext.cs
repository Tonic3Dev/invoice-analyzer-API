using bringeri_api.Entities;
using bringeri_api.Entities.Invoices;
using bringeri_api.Services.TenantProvider;
using Microsoft.EntityFrameworkCore;

namespace bringeri_api.Data;

public class AppDbContext : DbContext
{
    private ITenantProvider? _tenantProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<InvoiceBatch> InvoiceBatches => Set<InvoiceBatch>();

    public DbSet<InvoiceDocument> InvoiceDocuments => Set<InvoiceDocument>();

    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();

    public void SetTenantProvider(ITenantProvider tenantProvider)
    {
        _tenantProvider = tenantProvider;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).HasMaxLength(100).IsRequired();
            entity.Property(t => t.Slug).HasMaxLength(50).IsRequired();
            entity.Property(t => t.PageTitle).HasMaxLength(120).IsRequired();
            entity.Property(t => t.PrimaryColor).HasMaxLength(20).IsRequired();
            entity.Property(t => t.SecondaryColor).HasMaxLength(20).IsRequired();
            entity.Property(t => t.DefaultLanguage).HasMaxLength(10).IsRequired();
            entity.HasIndex(t => t.Slug).IsUnique();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Email).HasMaxLength(255).IsRequired();
            entity.Property(u => u.PasswordHash).IsRequired();
            entity.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(u => u.LastName).HasMaxLength(100).IsRequired();
            entity.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();

            entity.HasOne(u => u.Tenant)
                .WithMany(t => t.Users)
                .HasForeignKey(u => u.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(u =>
                _tenantProvider == null
                || !_tenantProvider.HasTenant
                || u.TenantId == _tenantProvider.TenantId);
        });

        modelBuilder.Entity<InvoiceBatch>(entity =>
        {
            entity.HasKey(batch => batch.Id);
            entity.Property(batch => batch.Title).HasMaxLength(200).IsRequired();
            entity.Property(batch => batch.Status).IsRequired();
            entity.HasIndex(batch => new { batch.TenantId, batch.CreatedAt });

            entity.HasOne(batch => batch.Tenant)
                .WithMany(tenant => tenant.InvoiceBatches)
                .HasForeignKey(batch => batch.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(batch => batch.CreatedByUser)
                .WithMany(user => user.CreatedInvoiceBatches)
                .HasForeignKey(batch => batch.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(batch =>
                _tenantProvider == null
                || !_tenantProvider.HasTenant
                || batch.TenantId == _tenantProvider.TenantId);
        });

        modelBuilder.Entity<InvoiceDocument>(entity =>
        {
            entity.HasKey(invoice => invoice.Id);
            entity.Property(invoice => invoice.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(invoice => invoice.ContentType).HasMaxLength(150).IsRequired();
            entity.Property(invoice => invoice.Status).IsRequired();
            entity.Property(invoice => invoice.IssuerLegalName).HasMaxLength(255);
            entity.Property(invoice => invoice.IssuerTaxId).HasMaxLength(100);
            entity.Property(invoice => invoice.IssuerTaxStatus).HasMaxLength(100);
            entity.Property(invoice => invoice.RecipientLegalName).HasMaxLength(255);
            entity.Property(invoice => invoice.RecipientTaxId).HasMaxLength(100);
            entity.Property(invoice => invoice.DocumentType).HasMaxLength(120);
            entity.Property(invoice => invoice.PointOfSaleNumber).HasMaxLength(50);
            entity.Property(invoice => invoice.DocumentNumber).HasMaxLength(50);
            entity.Property(invoice => invoice.FiscalAuthCode).HasMaxLength(120);
            entity.Property(invoice => invoice.Currency).HasMaxLength(20);
            entity.Property(invoice => invoice.NetSubtotal).HasPrecision(18, 2);
            entity.Property(invoice => invoice.Vat21).HasPrecision(18, 2);
            entity.Property(invoice => invoice.GrossIncomePerceptions).HasPrecision(18, 2);
            entity.Property(invoice => invoice.TotalAmount).HasPrecision(18, 2);
            entity.HasIndex(invoice => new { invoice.TenantId, invoice.InvoiceBatchId, invoice.SortOrder });

            entity.HasOne(invoice => invoice.Tenant)
                .WithMany(tenant => tenant.InvoiceDocuments)
                .HasForeignKey(invoice => invoice.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(invoice => invoice.InvoiceBatch)
                .WithMany(batch => batch.Invoices)
                .HasForeignKey(invoice => invoice.InvoiceBatchId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(invoice =>
                _tenantProvider == null
                || !_tenantProvider.HasTenant
                || invoice.TenantId == _tenantProvider.TenantId);
        });

        modelBuilder.Entity<InvoiceLineItem>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.SkuCode).HasMaxLength(120);
            entity.Property(item => item.Description).HasMaxLength(4000);
            entity.Property(item => item.Quantity).HasPrecision(18, 2);
            entity.Property(item => item.UnitPrice).HasPrecision(18, 2);
            entity.Property(item => item.LineTotal).HasPrecision(18, 2);
            entity.HasIndex(item => new { item.TenantId, item.InvoiceDocumentId, item.SortOrder });

            entity.HasOne(item => item.Tenant)
                .WithMany(tenant => tenant.InvoiceLineItems)
                .HasForeignKey(item => item.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(item => item.InvoiceDocument)
                .WithMany(invoice => invoice.Items)
                .HasForeignKey(item => item.InvoiceDocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasQueryFilter(item =>
                _tenantProvider == null
                || !_tenantProvider.HasTenant
                || item.TenantId == _tenantProvider.TenantId);
        });
    }
}
