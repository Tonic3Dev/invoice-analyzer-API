using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AutoMapper;
using bringeri_api.Data;
using bringeri_api.DTOs.Admin;
using bringeri_api.DTOs.Auth;
using bringeri_api.Entities;
using bringeri_api.Services.TenantProvider;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace bringeri_api.Services.Auth;

public class AuthService : IAuthService
{
    private const int HashIterations = 100_000;
    private const int SaltLength = 16;
    private const int HashLength = 32;

    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenantProvider;
    private readonly IMapper _mapper;
    private readonly IConfiguration _configuration;

    public AuthService(AppDbContext db, ITenantProvider tenantProvider, IMapper mapper, IConfiguration configuration)
    {
        _db = db;
        _tenantProvider = tenantProvider;
        _mapper = mapper;
        _configuration = configuration;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        if (!_tenantProvider.HasTenant)
        {
            return null;
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _db.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail && u.IsActive);

        if (user == null || !VerifyPassword(request.Password, user.PasswordHash))
        {
            return null;
        }

        var token = GenerateJwtToken(user);

        return new LoginResponse
        {
            Token = token,
            User = _mapper.Map<AuthUserDto>(user),
        };
    }

    public async Task<TenantByEmailResponse?> FindTenantByEmailAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();

        var tenant = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Tenant)
            .Where(u => u.IsActive && u.Tenant.IsActive && u.Email.ToLower() == normalizedEmail)
            .Select(u => new
            {
                u.Tenant.Slug,
                u.Tenant.Name,
            })
            .FirstOrDefaultAsync();

        if (tenant == null)
        {
            return null;
        }

        return new TenantByEmailResponse
        {
            TenantSlug = tenant.Slug,
            TenantName = tenant.Name,
        };
    }

    public async Task<string?> FindTenantSlugForCredentialsAsync(LoginRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var candidates = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Tenant)
            .Where(u => u.IsActive && u.Email.ToLower() == normalizedEmail && u.Tenant.IsActive)
            .ToListAsync();

        foreach (var candidate in candidates)
        {
            if (VerifyPassword(request.Password, candidate.PasswordHash))
            {
                return candidate.Tenant.Slug;
            }
        }

        return null;
    }

    public async Task<AuthUserDto?> GetCurrentUserAsync(ClaimsPrincipal principal)
    {
        var userIdClaim = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        var user = await _db.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);

        if (user == null)
        {
            return null;
        }

        return _mapper.Map<AuthUserDto>(user);
    }

    public async Task<AuthUserDto> CreateAdminUserAsync(CreateAdminUserRequest request)
    {
        return await CreateUserInternalAsync(
            tenantSlug: request.TenantSlug,
            email: request.Email,
            password: request.Password,
            firstName: request.FirstName,
            lastName: request.LastName,
            role: UserRole.Admin);
    }

    public async Task<AuthUserDto> CreateUserAsync(CreateUserRequest request)
    {
        return await CreateUserInternalAsync(
            tenantSlug: request.TenantSlug,
            email: request.Email,
            password: request.Password,
            firstName: request.FirstName,
            lastName: request.LastName,
            role: UserRole.User);
    }

    private async Task<AuthUserDto> CreateUserInternalAsync(
        string tenantSlug,
        string email,
        string password,
        string firstName,
        string lastName,
        UserRole role)
    {
        var normalizedTenantSlug = tenantSlug.Trim().ToLowerInvariant();
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.IsActive && t.Slug.ToLower() == normalizedTenantSlug);

        if (tenant == null)
        {
            throw new InvalidOperationException($"Tenant '{tenantSlug}' not found.");
        }

        var existing = await _db.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.TenantId == tenant.Id && u.Email.ToLower() == normalizedEmail);

        if (existing)
        {
            throw new InvalidOperationException($"A user with email '{email}' already exists in tenant '{tenant.Slug}'.");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = normalizedEmail,
            PasswordHash = HashPassword(password),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        user.Tenant = tenant;

        return _mapper.Map<AuthUserDto>(user);
    }

    private string GenerateJwtToken(User user)
    {
        var jwtKey = Environment.GetEnvironmentVariable("INVOICE_ANALYZER_JWT_KEY")
            ?? _configuration["JwtSettings:SecretKey"]
            ?? throw new InvalidOperationException("INVOICE_ANALYZER_JWT_KEY environment variable or JwtSettings:SecretKey not configured.");

        var issuer = _configuration["JwtSettings:Issuer"] ?? "bringeri-api";
        var audience = _configuration["JwtSettings:Audience"] ?? "bringeri-frontend";

        var expirationDaysRaw = Environment.GetEnvironmentVariable("JWT_EXPIRATION_DAYS")
            ?? _configuration["JwtSettings:ExpirationDays"]
            ?? "7";

        if (!int.TryParse(expirationDaysRaw, out var expirationDays) || expirationDays <= 0)
        {
            expirationDays = 7;
        }

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
            new("tenant", user.Tenant.Slug),
            new("tenantName", user.Tenant.Name),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddDays(expirationDays);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            HashIterations,
            HashAlgorithmName.SHA256,
            HashLength);

        return $"pbkdf2${HashIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[0] != "pbkdf2")
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expectedHash = Convert.FromBase64String(parts[3]);

        var candidate = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(candidate, expectedHash);
    }
}
