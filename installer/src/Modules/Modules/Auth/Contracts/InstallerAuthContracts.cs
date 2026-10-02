namespace Modules.Auth.Contracts;

public sealed record UnlockInstallerRequest(string? Token);

public sealed record InstallerAuthSessionResponse(
    bool Authenticated,
    string? DisplayName);