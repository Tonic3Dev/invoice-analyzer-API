namespace bringeri_api.DTOs.Auth;

public class TenantByEmailResponse
{
    public string TenantSlug { get; set; } = string.Empty;

    public string TenantName { get; set; } = string.Empty;

    public string TenantEncoded { get; set; } = string.Empty;
}
