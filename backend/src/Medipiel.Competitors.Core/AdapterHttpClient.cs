using System.Net.Http.Headers;

namespace Medipiel.Competitors.Core;

public sealed class AdapterHttpClient
{
    private readonly HttpClient _httpClient;

    public AdapterHttpClient(string? userAgent = null, string? accept = "*/*", HttpMessageHandler? handler = null)
    {
        if (handler is null)
        {
            var httpHandler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                UseCookies = true,
            };
            _httpClient = new HttpClient(httpHandler);
        }
        else
        {
            _httpClient = new HttpClient(handler, disposeHandler: false);
        }

        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }

        if (!string.IsNullOrWhiteSpace(accept))
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        }
    }

    public async Task<string?> GetStringAsync(string url, int delayMs, CancellationToken ct)
    {
        if (delayMs > 0)
        {
            await Task.Delay(delayMs, ct);
        }

        using var response = await _httpClient.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                null,
                response.StatusCode
            );
        }

        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<string?> PostAsync(string url, HttpContent? content, int delayMs, CancellationToken ct)
    {
        if (delayMs > 0)
        {
            await Task.Delay(delayMs, ct);
        }

        using var response = await _httpClient.PostAsync(url, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                null,
                response.StatusCode
            );
        }

        return await response.Content.ReadAsStringAsync(ct);
    }
}
