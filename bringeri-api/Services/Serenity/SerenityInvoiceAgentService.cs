using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using bringeri_api.DTOs.InvoiceBatches;
using Microsoft.AspNetCore.Http;

namespace bringeri_api.Services.Serenity;

public class SerenityInvoiceAgentService : ISerenityInvoiceAgentService
{
    private const string ApiKeyEnvironmentName = "INVOICE_SERENITY_API_KEY";
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan AnalysisTimeout = TimeSpan.FromSeconds(165);
    private static readonly TimeSpan ExecuteReadinessRetryDelay = TimeSpan.FromMilliseconds(500);
    private const int ExecuteReadinessMaxAttempts = 3;
    private const int MaxBodySnippetLength = 600;
    private const string GenericPreviewFailureMessage = "Unable to analyze one or more invoices. Please verify the file and retry.";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SerenityInvoiceAgentService> _logger;

    public SerenityInvoiceAgentService(HttpClient httpClient, IConfiguration configuration, ILogger<SerenityInvoiceAgentService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<InvoiceEditorDto> AnalyzeInvoiceAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SerenityAnalyzeStart] FileName={FileName} FileSize={FileSize}", file.FileName, file.Length);

        var knowledgeId = await UploadFileAsync(file, cancellationToken);
        var analysis = await ExecuteAnalysisAsync(knowledgeId, cancellationToken);

        _logger.LogInformation("[SerenityAnalyzeSuccess] FileName={FileName} KnowledgeId={KnowledgeId}", file.FileName, knowledgeId);

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

        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationCts.CancelAfter(UploadTimeout);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, operationCts.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException("Serenity upload request timed out.", ex);
        }

        using (response)
        {
            await EnsureSuccessOrThrowAsync(response, "Upload", cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(responseBody);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "[SerenityUploadParseFailure] BodySnippet={BodySnippet}", TruncateForLog(responseBody));
                throw new InvalidOperationException(GenericPreviewFailureMessage, ex);
            }

            using (document)
            {
                if (!document.RootElement.TryGetProperty("id", out var idElement))
                {
                    throw new InvalidOperationException("Unable to analyze one or more invoices. Serenity upload did not return a file id.");
                }

                return idElement.GetString() ?? throw new InvalidOperationException("Unable to analyze one or more invoices. Serenity upload id was empty.");
            }
        }
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

        for (var attempt = 1; attempt <= ExecuteReadinessMaxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "agent/InvoiceBridge/execute")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };

            request.Headers.Add("X-API-KEY", ResolveApiKey());

            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            operationCts.CancelAfter(AnalysisTimeout);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, operationCts.Token);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new HttpRequestException("Serenity analysis request timed out.", ex);
            }

            using (response)
            {
                var responseBody = await SafeReadBodyAsync(response, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    if (attempt < ExecuteReadinessMaxAttempts
                        && IsTransientExecuteReadinessError(response.StatusCode, responseBody))
                    {
                        _logger.LogInformation(
                            "[SerenityExecuteReadinessRetry] Attempt={Attempt}/{MaxAttempts} StatusCode={StatusCode} KnowledgeId={KnowledgeId}",
                            attempt,
                            ExecuteReadinessMaxAttempts,
                            (int)response.StatusCode,
                            knowledgeId);
                        await Task.Delay(ExecuteReadinessRetryDelay, cancellationToken);
                        continue;
                    }

                    await EnsureSuccessOrThrowAsync(response, "Execute", responseBody, cancellationToken);
                }

                JsonDocument outer;
                try
                {
                    outer = JsonDocument.Parse(responseBody);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "[SerenityExecuteParseFailure] BodySnippet={BodySnippet}", TruncateForLog(responseBody));
                    throw new InvalidOperationException(GenericPreviewFailureMessage, ex);
                }

                using (outer)
                {
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
                            try
                            {
                                return (JsonDocument.Parse(content), responseBody);
                            }
                            catch (JsonException ex)
                            {
                                _logger.LogWarning(ex, "[SerenityExecuteContentParseFailure] BodySnippet={BodySnippet}", TruncateForLog(content));
                                throw new InvalidOperationException(GenericPreviewFailureMessage, ex);
                            }
                        }
                    }
                }

                throw new InvalidOperationException("Unable to analyze one or more invoices. Serenity response did not include parseable analysis content.");
            }
        }

        throw new InvalidOperationException(GenericPreviewFailureMessage);
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

    private async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var body = await SafeReadBodyAsync(response, cancellationToken);
        await EnsureSuccessOrThrowAsync(response, operation, body, cancellationToken);
    }

    private async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, string operation, string responseBody, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        _ = cancellationToken;
        var bodySnippet = TruncateForLog(responseBody);

        _logger.LogWarning(
            "[Serenity{Operation}Failure] StatusCode={StatusCode} BodySnippet={BodySnippet}",
            operation,
            (int)response.StatusCode,
            bodySnippet);

        var statusCode = response.StatusCode;
        if ((int)statusCode >= 400
            && (int)statusCode < 500
            && statusCode != HttpStatusCode.RequestTimeout
            && statusCode != HttpStatusCode.TooManyRequests)
        {
            throw new InvalidOperationException(GenericPreviewFailureMessage);
        }

        throw new HttpRequestException($"Serenity {operation.ToLowerInvariant()} request failed with status {(int)statusCode} ({statusCode}).");
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TruncateForLog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaxBodySnippetLength
            ? trimmed
            : $"{trimmed[..MaxBodySnippetLength]}...(truncated)";
    }

    private static bool IsTransientExecuteReadinessError(HttpStatusCode statusCode, string responseBody)
    {
        if (statusCode != HttpStatusCode.BadRequest)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        var normalized = responseBody.ToLowerInvariant();
        return normalized.Contains("volatileknowledge")
            || normalized.Contains("volatile knowledge")
            || normalized.Contains("knowledge id")
            || normalized.Contains("not found")
            || normalized.Contains("not ready");
    }
}