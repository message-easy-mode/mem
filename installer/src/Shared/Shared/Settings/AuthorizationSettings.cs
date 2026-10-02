using System;

namespace Shared.Settings;

public class AuthorizationSettings
{
    public string[] AdminRoles { get; init; } = Array.Empty<string>();
    public string[] StaffRoles { get; init; } = Array.Empty<string>();
    // Add more as you need:
    // public string[] AccountingRoles { get; init; } = Array.Empty<string>();
}