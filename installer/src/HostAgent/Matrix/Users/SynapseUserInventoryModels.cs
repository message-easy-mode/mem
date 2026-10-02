using System.Text.Json;
using System.Text.Json.Nodes;
using Infrastructure.Data.Entities;

namespace HostAgent.Matrix.Users;

public static class RuntimeStackUserInventoryStates
{
    public const string NotSynchronized = "not_synchronized";
    public const string Synchronized = "synchronized";
    public const string Failed = "failed";
}

public sealed record RuntimeStackUserInventoryTarget(
    Guid RuntimeStackId,
    string DatabaseName);

public sealed record SynapseUserInventoryAccount(
    string MatrixUserId,
    string Username,
    bool IsAdmin,
    bool IsDeactivated,
    DateTime? CreatedAtUtc);

public sealed record SynapseUserInventorySnapshot(
    string Source,
    IReadOnlyList<SynapseUserInventoryAccount> Accounts,
    DateTimeOffset ReadAtUtc)
{
    public int ActiveAdminCount => Accounts.Count(x => x.IsAdmin && !x.IsDeactivated);
}

public sealed record RuntimeStackUserInventorySynchronizationResult(
    Guid RuntimeStackId,
    string Source,
    IReadOnlyList<SynapseUserInventoryAccount> Accounts,
    int InsertedCount,
    int UpdatedCount,
    int MissingCount,
    DateTimeOffset SynchronizedAtUtc)
{
    public int UserCount => Accounts.Count;
    public int ActiveAdminCount => Accounts.Count(x => x.IsAdmin && !x.IsDeactivated);
}

public sealed record RuntimeStackUserInventoryState(
    string Status,
    string? Source,
    DateTime? LastAttemptedAtUtc,
    DateTime? LastSynchronizedAtUtc,
    int? UserCount,
    int? ActiveAdminCount,
    string? ErrorCode)
{
    public static RuntimeStackUserInventoryState NotSynchronized() => new(
        RuntimeStackUserInventoryStates.NotSynchronized,
        null,
        null,
        null,
        null,
        null,
        null);
}

public sealed class RuntimeStackUserInventoryException : Exception
{
    public RuntimeStackUserInventoryException(
        string code,
        string safeDetail,
        Exception? innerException = null)
        : base(safeDetail, innerException)
    {
        Code = code;
        SafeDetail = safeDetail;
    }

    public string Code { get; }
    public string SafeDetail { get; }
}

public sealed class RuntimeStackUserConflictException : Exception
{
    public RuntimeStackUserConflictException(string code, string safeDetail)
        : base(safeDetail)
    {
        Code = code;
        SafeDetail = safeDetail;
    }

    public string Code { get; }
    public string SafeDetail { get; }
}

public static class RuntimeStackUserCreationPolicy
{
    public static void EnsureAllowed(
        RuntimeStackUserInventorySynchronizationResult inventory,
        string username,
        bool isFirstAdmin)
    {
        if (inventory.Accounts.Any(x =>
                string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)))
        {
            throw new RuntimeStackUserConflictException(
                "matrix_user_already_exists",
                $"Matrix user '{username}' already exists in Synapse.");
        }

        if (isFirstAdmin && inventory.ActiveAdminCount > 0)
        {
            throw new RuntimeStackUserConflictException(
                "matrix_admin_already_exists",
                "An active Matrix administrator already exists in Synapse.");
        }
    }
}

public static class RuntimeStackUserInventoryMetadata
{
    private const string PropertyName = "matrixUserInventory";

    public static RuntimeStackUserInventoryState Read(string? metadataJson)
    {
        var root = ParseRoot(metadataJson);

        if (root[PropertyName] is not JsonObject inventory)
        {
            return RuntimeStackUserInventoryState.NotSynchronized();
        }

        var status = inventory["status"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(status))
        {
            return RuntimeStackUserInventoryState.NotSynchronized();
        }

        return new RuntimeStackUserInventoryState(
            Status: status,
            Source: inventory["source"]?.GetValue<string>(),
            LastAttemptedAtUtc: ReadUtc(inventory["lastAttemptedAtUtc"]),
            LastSynchronizedAtUtc: ReadUtc(inventory["lastSynchronizedAtUtc"]),
            UserCount: ReadInt(inventory["userCount"]),
            ActiveAdminCount: ReadInt(inventory["activeAdminCount"]),
            ErrorCode: inventory["errorCode"]?.GetValue<string>());
    }

    public static string WriteSuccess(
        string? metadataJson,
        SynapseUserInventorySnapshot snapshot)
    {
        var root = ParseRoot(metadataJson);
        root[PropertyName] = new JsonObject
        {
            ["status"] = RuntimeStackUserInventoryStates.Synchronized,
            ["source"] = snapshot.Source,
            ["lastAttemptedAtUtc"] = snapshot.ReadAtUtc.UtcDateTime,
            ["lastSynchronizedAtUtc"] = snapshot.ReadAtUtc.UtcDateTime,
            ["userCount"] = snapshot.Accounts.Count,
            ["activeAdminCount"] = snapshot.ActiveAdminCount,
            ["errorCode"] = null
        };

        return root.ToJsonString(JsonOptions());
    }

    public static string WriteFailure(
        string? metadataJson,
        string source,
        DateTimeOffset attemptedAtUtc,
        string errorCode)
    {
        var root = ParseRoot(metadataJson);
        var previous = root[PropertyName] as JsonObject;

        root[PropertyName] = new JsonObject
        {
            ["status"] = RuntimeStackUserInventoryStates.Failed,
            ["source"] = source,
            ["lastAttemptedAtUtc"] = attemptedAtUtc.UtcDateTime,
            ["lastSynchronizedAtUtc"] = previous?["lastSynchronizedAtUtc"]?.DeepClone(),
            ["userCount"] = previous?["userCount"]?.DeepClone(),
            ["activeAdminCount"] = previous?["activeAdminCount"]?.DeepClone(),
            ["errorCode"] = errorCode
        };

        return root.ToJsonString(JsonOptions());
    }

    private static JsonObject ParseRoot(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(metadataJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static DateTime? ReadUtc(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<DateTime>(out var dateTime))
        {
            return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }

        return DateTime.TryParse(node.ToString(), out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;
    }

    private static int? ReadInt(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<int>(out var result))
        {
            return result;
        }

        return int.TryParse(node?.ToString(), out var parsed)
            ? parsed
            : null;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true
    };
}

public static class RuntimeStackUserProjectionMetadata
{
    public const string MemCreated = "mem-created";
    public const string MemCreationUnconfirmed = "mem-creation-unconfirmed";
    public const string SynapseDiscovered = "synapse-discovered";

    public static string ReadOrigin(RuntimeStackUserEntity entity)
    {
        var root = Parse(entity.MetadataJson);
        var origin = root["origin"]?.GetValue<string>();

        if (!string.IsNullOrWhiteSpace(origin))
        {
            return origin;
        }

        return root["registrationSecretSource"] is not null || entity.IsFirstAdmin
            ? MemCreated
            : SynapseDiscovered;
    }

    public static string MergeInventory(
        RuntimeStackUserEntity entity,
        string origin,
        DateTimeOffset synchronizedAtUtc)
    {
        var root = Parse(entity.MetadataJson);
        root["origin"] = origin;
        root["inventorySource"] = "synapse-postgres";
        root["lastInventorySynchronizedAtUtc"] = synchronizedAtUtc.UtcDateTime;

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject Parse(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(metadataJson) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }
}
