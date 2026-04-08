using System.Security.Claims;
using bringeri_api.DTOs.Admin;
using bringeri_api.DTOs.Auth;

namespace bringeri_api.Services.Auth;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);

    Task<TenantByEmailResponse?> FindTenantByEmailAsync(string email);

    Task<string?> FindTenantSlugForCredentialsAsync(LoginRequest request);

    Task<AuthUserDto?> GetCurrentUserAsync(ClaimsPrincipal principal);

    Task<AuthUserDto> CreateAdminUserAsync(CreateAdminUserRequest request);

    Task<AuthUserDto> CreateUserAsync(CreateUserRequest request);
}
