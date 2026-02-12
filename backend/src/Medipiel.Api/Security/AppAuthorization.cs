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

        var extractedRoles = ExtractRoles(user).ToList();

        // Compatibility fallback for identity tokens that do not emit business role claims.
        var displayName = user.FindFirst("name")?.Value;
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            extractedRoles.Add(displayName.Trim());
        }

        var givenName = user.FindFirst("given_name")?.Value;
        var familyName = user.FindFirst("family_name")?.Value;
        if (!string.IsNullOrWhiteSpace(givenName) && !string.IsNullOrWhiteSpace(familyName))
        {
            extractedRoles.Add($"{givenName} {familyName}");
        }

        return extractedRoles
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
            "realm_access.roles",
            "resource_access.roles",
            "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/role"
        };

        foreach (var claim in user.Claims.Where(c =>
                     roleClaimTypes.Contains(c.Type) ||
                     IsStructuredRoleClaim(c.Type)))
        {
            foreach (var value in ParseRoleValue(claim.Value))
            {
                yield return value;
            }
        }
    }

    private static bool IsStructuredRoleClaim(string? claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType))
        {
            return false;
        }

        return claimType.Equals("realm_access", StringComparison.OrdinalIgnoreCase) ||
               claimType.Equals("resource_access", StringComparison.OrdinalIgnoreCase) ||
               claimType.EndsWith(".roles", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ParseRoleValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        var value = raw.Trim();

        if ((value.StartsWith('[') && value.EndsWith(']')) ||
            (value.StartsWith('{') && value.EndsWith('}')))
        {
            string[] jsonRoles = [];
            var jsonParsed = false;

            try
            {
                using var doc = JsonDocument.Parse(value);
                jsonRoles = ExtractRolesFromJson(doc.RootElement).ToArray();
                jsonParsed = true;
            }
            catch
            {
                // Fall through to delimiter-based parsing.
            }

            if (jsonParsed && jsonRoles.Length > 0)
            {
                foreach (var item in jsonRoles)
                {
                    yield return item;
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

    private static IEnumerable<string> ExtractRolesFromJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var str = element.GetString();
                if (!string.IsNullOrWhiteSpace(str))
                {
                    yield return str.Trim();
                }
                yield break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var role in ExtractRolesFromJson(item))
                    {
                        yield return role;
                    }
                }
                yield break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Equals("roles", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.EndsWith(".roles", StringComparison.OrdinalIgnoreCase) ||
                        property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                    {
                        foreach (var role in ExtractRolesFromJson(property.Value))
                        {
                            yield return role;
                        }
                    }
                }
                yield break;

            default:
                yield break;
        }
    }

    private static string NormalizeRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return string.Empty;
        }

        var normalized = role
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();

        return new string(normalized);
    }
}
