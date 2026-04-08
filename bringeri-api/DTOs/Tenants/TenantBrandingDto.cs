namespace bringeri_api.DTOs.Tenants;

public class TenantBrandingDto
{
    public string TenantSlug { get; set; } = string.Empty;

    public string TenantName { get; set; } = string.Empty;

    public string PageTitle { get; set; } = "Invoice Analyzer";

    public string PrimaryColor { get; set; } = "#cb4b27";

    public string SecondaryColor { get; set; } = "#180901";

    public string DefaultLanguage { get; set; } = "en";

    public string? LogoBase64 { get; set; }
}
