using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using bringeri_api.DTOs.InvoiceBatches;
using Microsoft.AspNetCore.Http;

namespace bringeri_api.Services.Serenity;

public class SerenityInvoiceAgentService : ISerenityInvoiceAgentService
{
    private const string ApiKeyEnvironmentName = "INVOICE_SERENITY_API_KEY";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public SerenityInvoiceAgentService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<InvoiceEditorDto> AnalyzeInvoiceAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var knowledgeId = await UploadFileAsync(file, cancellationToken);
        var analysis = await ExecuteAnalysisAsync(knowledgeId, cancellationToken);

        return BuildEditorDto(file, analysis.document.RootElement, analysis.rawResponse);
    }

    private async Task<string> UploadFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var stream = file.OpenReadStream();
        using var content = new StreamContent(stream);

        content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
        form.Add(content, "file", file.FileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "VolatileKnowledge")
        {
            Content = form,
        };

        request.Headers.Add("X-API-KEY", ResolveApiKey());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(responseBody);

        if (!document.RootElement.TryGetProperty("id", out var idElement))
        {
            throw new InvalidOperationException("Serenity upload response did not include an id.");
        }

        return idElement.GetString() ?? throw new InvalidOperationException("Serenity upload id was empty.");
    }

    private async Task<(JsonDocument document, string rawResponse)> ExecuteAnalysisAsync(string knowledgeId, CancellationToken cancellationToken)
    {
        var payload = new[]
        {
            new
            {
                Key = "volatileKnowledgeIds",
                Value = new[] { knowledgeId },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "agent/InvoiceBridge/execute")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

        request.Headers.Add("X-API-KEY", ResolveApiKey());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var outer = JsonDocument.Parse(responseBody);

        if (outer.RootElement.TryGetProperty("jsonContent", out var jsonContentElement)
            && jsonContentElement.ValueKind == JsonValueKind.Object)
        {
            return (JsonDocument.Parse(jsonContentElement.GetRawText()), responseBody);
        }

        if (outer.RootElement.TryGetProperty("content", out var contentElement)
            && contentElement.ValueKind == JsonValueKind.String)
        {
            var content = contentElement.GetString();
            if (!string.IsNullOrWhiteSpace(content))
            {
                return (JsonDocument.Parse(content), responseBody);
            }
        }

        throw new InvalidOperationException("Serenity analysis response did not include jsonContent or parsable content.");
    }

    private static InvoiceEditorDto BuildEditorDto(IFormFile file, JsonElement analysis, string rawResponse)
    {
        var dto = new InvoiceEditorDto
        {
            FileName = file.FileName,
            ContentType = file.ContentType ?? "application/octet-stream",
            FileSize = file.Length,
            RawAgentResponse = rawResponse,
            Status = "ready",
            Issuer = ReadParty(analysis, "issuer", includeTaxStatus: true),
            Recipient = ReadParty(analysis, "recipient", includeTaxStatus: false),
            Document = ReadDocument(analysis),
            Totals = ReadTotals(analysis),
            Items = ReadItems(analysis),
        };

        return dto;
    }

    private static InvoicePartyDto ReadParty(JsonElement analysis, string propertyName, bool includeTaxStatus)
    {
        if (!analysis.TryGetProperty(propertyName, out var party) || party.ValueKind != JsonValueKind.Object)
        {
            return new InvoicePartyDto();
        }

        return new InvoicePartyDto
        {
            LegalName = ReadString(party, "legal_name"),
            TaxId = ReadString(party, "tax_id"),
            TaxStatus = includeTaxStatus ? ReadString(party, "tax_status") : string.Empty,
        };
    }

    private static InvoiceDocumentInfoDto ReadDocument(JsonElement analysis)
    {
        if (!analysis.TryGetProperty("document", out var document) || document.ValueKind != JsonValueKind.Object)
        {
            return new InvoiceDocumentInfoDto();
        }

        return new InvoiceDocumentInfoDto
        {
            Type = ReadString(document, "type"),
            PosNumber = ReadString(document, "pos_number"),
            Number = ReadString(document, "number"),
            IssueDate = ReadString(document, "issue_date"),
            FiscalAuthCode = ReadString(document, "fiscal_auth_code"),
            FiscalAuthExpiry = ReadString(document, "fiscal_auth_expiry"),
        };
    }

    private static InvoiceTotalsDto ReadTotals(JsonElement analysis)
    {
        if (!analysis.TryGetProperty("totals", out var totals) || totals.ValueKind != JsonValueKind.Object)
        {
            return new InvoiceTotalsDto();
        }

        return new InvoiceTotalsDto
        {
            Currency = ReadString(totals, "currency"),
            NetSubtotal = ReadDecimal(totals, "net_subtotal"),
            Vat21 = ReadDecimal(totals, "vat_21"),
            GrossIncomePerceptions = ReadDecimal(totals, "gross_income_perceptions"),
            TotalAmount = ReadDecimal(totals, "total_amount"),
        };
    }

    private static List<InvoiceLineItemDto> ReadItems(JsonElement analysis)
    {
        if (!analysis.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return new List<InvoiceLineItemDto>();
        }

        var result = new List<InvoiceLineItemDto>();

        foreach (var item in items.EnumerateArray())
        {
            result.Add(new InvoiceLineItemDto
            {
                SkuCode = ReadString(item, "sku_code"),
                Description = ReadString(item, "description"),
                Quantity = ReadDecimal(item, "quantity"),
                UnitPrice = ReadDecimal(item, "unit_price"),
                LineTotal = ReadDecimal(item, "line_total"),
            });
        }

        return result;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static decimal ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return 0m;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0m;
    }

    private string ResolveApiKey()
    {
        return Environment.GetEnvironmentVariable(ApiKeyEnvironmentName)
            ?? _configuration[ApiKeyEnvironmentName]
            ?? throw new InvalidOperationException($"{ApiKeyEnvironmentName} environment variable is not configured.");
    }
}