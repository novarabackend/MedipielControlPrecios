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

        raw = System.Net.WebUtility.HtmlDecode(raw);

        // Keep only numeric + separators so we can correctly interpret:
        // - "$90.700"      -> 90700
        // - "$90.700,00"   -> 90700.00
        // - "$36.989,00"   -> 36989.00
        // The previous implementation removed all separators and would treat cents as part of the integer
        // (e.g. "36.989,00" -> "3698900" -> 3,698,900), which is wrong for COP pricing.
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        var lastDot = cleaned.LastIndexOf('.');
        var lastComma = cleaned.LastIndexOf(',');
        var lastSep = Math.Max(lastDot, lastComma);

        // We only treat a separator as decimal point when there are exactly 2 digits after it.
        // Otherwise it is assumed to be a thousands separator and removed.
        char? decimalSep = null;
        if (lastSep >= 0 && cleaned.Length - lastSep - 1 == 2)
        {
            decimalSep = cleaned[lastSep];
        }

        var sb = new System.Text.StringBuilder(cleaned.Length);
        for (var i = 0; i < cleaned.Length; i++)
        {
            var ch = cleaned[i];
            if (char.IsDigit(ch))
            {
                sb.Append(ch);
                continue;
            }

            if (decimalSep.HasValue && i == lastSep && ch == decimalSep.Value)
            {
                sb.Append('.');
            }
        }

        var normalized = sb.ToString();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return null;
    }
}
