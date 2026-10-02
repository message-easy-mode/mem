using System;

namespace Shared.Security;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string? UserId { get; }        // from "sub"
    string? Username { get; }      // "preferred_username"
    string? Email { get; }

    IReadOnlyCollection<string> RealmRoles { get; }
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> ClientRoles { get; }

    bool HasRealmRole(string role);
    bool HasClientRole(string clientId, string role);
}