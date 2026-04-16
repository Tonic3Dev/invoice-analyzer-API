using System.Net;

namespace bringeri_api.Services.Serenity;

public class SerenityTransientRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);

    private readonly ILogger<SerenityTransientRetryHandler> _logger;

    public SerenityTransientRetryHandler(ILogger<SerenityTransientRetryHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var template = await HttpRequestTemplate.CreateAsync(request, cancellationToken);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var clonedRequest = template.Create();

            try
            {
                var response = await base.SendAsync(clonedRequest, cancellationToken);
                if (!ShouldRetry(response.StatusCode) || attempt == MaxAttempts)
                {
                    return response;
                }

                _logger.LogWarning(
                    "[SerenityRetryStatus] Attempt={Attempt}/{MaxAttempts} StatusCode={StatusCode}",
                    attempt,
                    MaxAttempts,
                    (int)response.StatusCode);

                response.Dispose();
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "[SerenityRetryTimeout] Attempt={Attempt}/{MaxAttempts}", attempt, MaxAttempts);
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "[SerenityRetryTransport] Attempt={Attempt}/{MaxAttempts}", attempt, MaxAttempts);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }

        throw new HttpRequestException("Serenity request failed after retry attempts.");
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout
            || statusCode == HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;
    }

    private sealed class HttpRequestTemplate
    {
        private readonly HttpMethod _method;
        private readonly Uri _requestUri;
        private readonly Version _version;
        private readonly HttpVersionPolicy _versionPolicy;
        private readonly List<KeyValuePair<string, IEnumerable<string>>> _headers;
        private readonly byte[]? _content;
        private readonly List<KeyValuePair<string, IEnumerable<string>>> _contentHeaders;

        private HttpRequestTemplate(
            HttpMethod method,
            Uri requestUri,
            Version version,
            HttpVersionPolicy versionPolicy,
            List<KeyValuePair<string, IEnumerable<string>>> headers,
            byte[]? content,
            List<KeyValuePair<string, IEnumerable<string>>> contentHeaders)
        {
            _method = method;
            _requestUri = requestUri;
            _version = version;
            _versionPolicy = versionPolicy;
            _headers = headers;
            _content = content;
            _contentHeaders = contentHeaders;
        }

        public static async Task<HttpRequestTemplate> CreateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Serenity request URI is required.");

            byte[]? content = null;
            var contentHeaders = new List<KeyValuePair<string, IEnumerable<string>>>();
            if (request.Content != null)
            {
                content = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                contentHeaders = request.Content.Headers
                    .Select(header => new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value.ToArray()))
                    .ToList();
            }

            var headers = request.Headers
                .Select(header => new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value.ToArray()))
                .ToList();

            return new HttpRequestTemplate(
                request.Method,
                uri,
                request.Version,
                request.VersionPolicy,
                headers,
                content,
                contentHeaders);
        }

        public HttpRequestMessage Create()
        {
            var request = new HttpRequestMessage(_method, _requestUri)
            {
                Version = _version,
                VersionPolicy = _versionPolicy,
            };

            foreach (var header in _headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (_content != null)
            {
                var content = new ByteArrayContent(_content);
                foreach (var header in _contentHeaders)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                request.Content = content;
            }

            return request;
        }
    }
}
