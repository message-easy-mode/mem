using System;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Shared.Security;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public string? UserId => User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                             ?? User?.FindFirst("sub")?.Value;

    public string? Username => User?.FindFirst("preferred_username")?.Value
                               ?? User?.Identity?.Name;

    public string? Email => User?.FindFirst(ClaimTypes.Email)?.Value
                            ?? User?.FindFirst("email")?.Value;

    private IReadOnlyCollection<string>? _realmRoles;
    public IReadOnlyCollection<string> RealmRoles =>
        _realmRoles ??= ParseRealmRoles();

    private IReadOnlyDictionary<string, IReadOnlyCollection<string>>? _clientRoles;
    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> ClientRoles =>
        _clientRoles ??= ParseClientRoles();

    public bool HasRealmRole(string role) =>
        RealmRoles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public bool HasClientRole(string clientId, string role)
    {
        if (!ClientRoles.TryGetValue(clientId, out var roles))
            return false;

        return roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyCollection<string> ParseRealmRoles()
    {
        var claim = User?.FindFirst("realm_access")?.Value;
        if (string.IsNullOrWhiteSpace(claim))
            return Array.Empty<string>();

        try
        {
            using var doc = JsonDocument.Parse(claim);
            if (doc.RootElement.TryGetProperty("roles", out var rolesEl) &&
                rolesEl.ValueKind == JsonValueKind.Array)
            {
                return rolesEl
                    .EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .ToArray();
            }
        }
        catch
        {
            // swallow + treat as no roles; optionally log
        }

        return Array.Empty<string>();
    }

    private IReadOnlyDictionary<string, IReadOnlyCollection<string>> ParseClientRoles()
    {
        var claim = User?.FindFirst("resource_access")?.Value;
        if (string.IsNullOrWhiteSpace(claim))
            return new Dictionary<string, IReadOnlyCollection<string>>();

        var result = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(claim);
            foreach (var clientProp in doc.RootElement.EnumerateObject())
            {
                if (clientProp.Value.ValueKind != JsonValueKind.Object)
                    continue;

                if (clientProp.Value.TryGetProperty("roles", out var rolesEl) &&
                    rolesEl.ValueKind == JsonValueKind.Array)
                {
                    var roles = rolesEl
                        .EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!)
                        .ToArray();

                    result[clientProp.Name] = roles;
                }
            }
        }
        catch
        {
            // swallow; optionally log
        }

        return result;
    }
}