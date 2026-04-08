using System.Text;
using bringeri_api.Data;
using bringeri_api.Data.Seeders;
using bringeri_api.Filters;
using bringeri_api.Mappings;
using bringeri_api.Services.InvoiceBatches;
using bringeri_api.Services.Serenity;
using bringeri_api.Services.Auth;
using bringeri_api.Services.TenantProvider;
using bringeri_api.Services.Tenants;
using bringeri_api.Utils;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

var rawConnectionString = Environment.GetEnvironmentVariable("INVOICE_ANALYZER_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("INVOICE_ANALYZER_CONNECTION_STRING environment variable or DefaultConnection not configured.");

var connectionString = rawConnectionString.BuildPostgresConnectionString();

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

builder.Services.AddScoped<AppDbContext>(sp =>
{
    var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
    var tenantProvider = sp.GetRequiredService<ITenantProvider>();
    var db = factory.CreateDbContext();
    db.SetTenantProvider(tenantProvider);
    return db;
});

var jwtKey = Environment.GetEnvironmentVariable("INVOICE_ANALYZER_JWT_KEY")
    ?? builder.Configuration["JwtSettings:SecretKey"]
    ?? throw new InvalidOperationException("INVOICE_ANALYZER_JWT_KEY environment variable or JwtSettings:SecretKey not configured.");

var issuer = builder.Configuration["JwtSettings:Issuer"] ?? "bringeri-api";
var audience = builder.Configuration["JwtSettings:Audience"] ?? "bringeri-front";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.Zero,
    };
});

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, TenantProvider>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<IInvoiceBatchService, InvoiceBatchService>();
builder.Services.AddHttpClient<ISerenityInvoiceAgentService, SerenityInvoiceAgentService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Serenity:BaseUrl"] ?? "https://api.serenitystar.ai/api/v2/");
    client.Timeout = TimeSpan.FromMinutes(3);
});
builder.Services.AddAutoMapper(_ => { }, typeof(MappingProfile).Assembly);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Bringeri API",
        Version = "v1",
        Description = "Invoice Analyzer multi-tenant API",
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            },
            Array.Empty<string>()
        },
    });

    options.OperationFilter<TenantHeaderOperationFilter>();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors("AllowAll");

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";

        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var message = feature?.Error?.Message ?? "An internal server error occurred.";

        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        if (feature?.Error != null)
        {
            logger.LogError(feature.Error, "[UnhandledException] {Path}", context.Request.Path);
        }

        await context.Response.WriteAsJsonAsync(new { message });
    });
});

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Bringeri API v1");
    options.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => new { name = "Bringeri API", version = "1.0.0", swagger = "/swagger" });

app.MapGet("/api/health", async (AppDbContext db) =>
{
    try
    {
        await db.Database.CanConnectAsync();
        return Results.Ok(new { status = "healthy", database = "connected", timestamp = DateTime.UtcNow });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { status = "unhealthy", database = "disconnected", error = ex.Message, timestamp = DateTime.UtcNow });
    }
});

using (var scope = app.Services.CreateScope())
{
    try
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = factory.CreateDbContext();
        await db.Database.MigrateAsync();
        await DatabaseSeeder.SeedAsync(db);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "[Startup] Database seed failed.");
    }
}

app.Run();
