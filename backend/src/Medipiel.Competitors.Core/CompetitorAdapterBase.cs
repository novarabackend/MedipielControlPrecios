using System.Globalization;
using Medipiel.Competitors.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Medipiel.Competitors.Core;

public abstract class CompetitorAdapterBase : ICompetitorAdapter
{
    protected CompetitorAdapterBase(IConfiguration configuration, ILogger logger, string? userAgent = null)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        HttpClient = new AdapterHttpClient(userAgent);
    }

    public abstract string AdapterId { get; }
    public abstract string Name { get; }
    public abstract Task<AdapterRunResult> RunAsync(AdapterContext context, CancellationToken ct);

    protected IConfiguration Configuration { get; }
    protected ILogger Logger { get; }
    protected AdapterHttpClient HttpClient { get; }

    protected string GetConnectionString(string name)
    {
        var connectionString = Configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Connection string '{name}' not found.");
        }

        return connectionString;
    }

    protected CompetitorDb CreateDb(string connectionString) => new(connectionString);

    protected int GetDelayMs(string key, int fallback)
    {
        var value = Configuration.GetValue<int?>(key);
        if (value is null || value.Value < 0)
        {
            return fallback;
        }

        return value.Value;
    }

    protected async Task<string?> GetHtmlAsync(string url, int delayMs, CancellationToken ct)
    {
        try
        {
            var html = await HttpClient.GetStringAsync(url, delayMs, ct);
            if (html is null)
            {
                Logger.LogWarning("{AdapterId}: empty response for {Url}", AdapterId, url);
            }

            return html;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{AdapterId}: failed to download {Url}", AdapterId, url);
            return null;
        }
    }

    protected static string Combine(string baseUrl, string relative)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return relative;
        }

        return new Uri(new Uri(baseUrl), relative).ToString();
    }

    protected static string NormalizeUrl(string baseUrl, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return new Uri(new Uri(baseUrl), url).ToString();
    }

    protected static decimal? ParseMoney(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
        {
            return null;
        }

        if (decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return null;
    }
}
