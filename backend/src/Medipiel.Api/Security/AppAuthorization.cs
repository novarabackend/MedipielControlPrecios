using System.Security.Claims;
using System.Text.Json;

namespace Medipiel.Api.Security;

public static class AppAuthorization
{
    public const string AllowedRolesPolicy = "AllowedRolesPolicy";

    public static readonly string[] AllowedRoles =
    [
        "Mercadeo precios",
        "Administrator"
    ];

    public static bool HasRequiredRole(ClaimsPrincipal user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var allowed = AllowedRoles
            .Select(NormalizeRole)
            .ToHashSet(StringComparer.Ordinal);

        return ExtractRoles(user)
            .Select(NormalizeRole)
            .Any(allowed.Contains);
    }

    private static IEnumerable<string> ExtractRoles(ClaimsPrincipal user)
    {
        var roleClaimTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ClaimTypes.Role,
            "role",
            "roles",
            "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/role"
        };

        foreach (var claim in user.Claims.Where(c => roleClaimTypes.Contains(c.Type)))
        {
            foreach (var value in ParseRoleValue(claim.Value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> ParseRoleValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        var value = raw.Trim();

        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            string[]? array = null;

            try
            {
                array = JsonSerializer.Deserialize<string[]>(value);
            }
            catch
            {
                // Fall through to delimiter-based parsing.
            }

            if (array is not null)
            {
                foreach (var item in array.Where(v => !string.IsNullOrWhiteSpace(v)))
                {
                    yield return item.Trim();
                }

                yield break;
            }
        }

        var separators = new[] { ',', ';' };
        var parts = value.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            yield break;
        }

        foreach (var part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
            {
                yield return part.Trim();
            }
        }
    }

    private static string NormalizeRole(string role)
        => role.Trim().ToLowerInvariant();
}
