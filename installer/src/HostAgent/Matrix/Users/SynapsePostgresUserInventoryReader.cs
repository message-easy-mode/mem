using System.Text.Json;
using HostAgent.Runtime.Databases;

namespace HostAgent.Matrix.Users;

public interface ISynapseUserInventoryReader
{
    Task<SynapseUserInventorySnapshot> ReadAsync(
        RuntimeStackUserInventoryTarget target,
        CancellationToken ct);
}

public sealed class SynapsePostgresUserInventoryReader : ISynapseUserInventoryReader
{
    public const string InventorySource = "synapse-postgres";

    private const string SchemaQuery =
        "SELECT column_name " +
        "FROM information_schema.columns " +
        "WHERE table_schema = 'public' AND table_name = 'users' " +
        "ORDER BY ordinal_position;";

    private readonly IRuntimeStackPostgresQueryExecutor _queryExecutor;

    public SynapsePostgresUserInventoryReader(
        IRuntimeStackPostgresQueryExecutor queryExecutor)
    {
        _queryExecutor = queryExecutor;
    }

    public async Task<SynapseUserInventorySnapshot> ReadAsync(
        RuntimeStackUserInventoryTarget target,
        CancellationToken ct)
    {
        try
        {
            var schemaResult = await _queryExecutor.QueryAsync(
                target.DatabaseName,
                SchemaQuery,
                ct);

            var columns = ParseLines(schemaResult.Stdout)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!columns.Contains("name") || !columns.Contains("admin"))
            {
                throw new RuntimeStackUserInventoryException(
                    "synapse_users_schema_unsupported",
                    "The Synapse users table does not expose the account identity and administrator columns required for safe inventory reconciliation.");
            }

            var inventoryResult = await _queryExecutor.QueryAsync(
                target.DatabaseName,
                BuildInventoryQuery(columns),
                ct);

            var accounts = new List<SynapseUserInventoryAccount>();

            foreach (var line in ParseLines(inventoryResult.Stdout))
            {
                SynapseInventoryRow? row;

                try
                {
                    row = JsonSerializer.Deserialize<SynapseInventoryRow>(
                        line,
                        JsonOptions());
                }
                catch (JsonException ex)
                {
                    throw new RuntimeStackUserInventoryException(
                        "synapse_users_inventory_invalid",
                        "Synapse returned an invalid Matrix user inventory row.",
                        ex);
                }

                if (string.IsNullOrWhiteSpace(row?.MatrixUserId))
                {
                    throw new RuntimeStackUserInventoryException(
                        "synapse_users_inventory_invalid",
                        "Synapse returned a Matrix user inventory row without an account identity.");
                }

                if (MatrixManagedRecoveryAuthorityService.IsManagedRecoveryUser(row.MatrixUserId))
                {
                    continue;
                }

                accounts.Add(new SynapseUserInventoryAccount(
                    MatrixUserId: row.MatrixUserId.Trim(),
                    Username: ExtractUsername(row.MatrixUserId),
                    IsAdmin: row.IsAdmin,
                    IsDeactivated: row.IsDeactivated,
                    CreatedAtUtc: ToCreatedAtUtc(row.CreatedAtUnixMs)));
            }

            var duplicateUsername = accounts
                .GroupBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(x => x.Count() > 1);

            if (duplicateUsername is not null)
            {
                throw new RuntimeStackUserInventoryException(
                    "synapse_users_inventory_ambiguous",
                    "Synapse returned more than one account for the same local Matrix username.");
            }

            return new SynapseUserInventorySnapshot(
                Source: InventorySource,
                Accounts: accounts.OrderBy(x => x.Username).ToArray(),
                ReadAtUtc: DateTimeOffset.UtcNow);
        }
        catch (RuntimeStackUserInventoryException)
        {
            throw;
        }
        catch (RuntimeStackPostgresQueryException ex)
        {
            throw new RuntimeStackUserInventoryException(
                ex.Code,
                ex.SafeDetail,
                ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimeStackUserInventoryException(
                "matrix_user_inventory_unavailable",
                "The Matrix user inventory is currently unavailable.",
                ex);
        }
    }

    private static string BuildInventoryQuery(IReadOnlySet<string> columns)
    {
        var adminExpression = columns.Contains("admin")
            ? "CASE WHEN lower(COALESCE(admin::text, '0')) IN ('1','true','t','yes','y') THEN true ELSE false END"
            : "false";

        var deactivatedExpression = columns.Contains("deactivated")
            ? "CASE WHEN lower(COALESCE(deactivated::text, '0')) IN ('1','true','t','yes','y') THEN true ELSE false END"
            : "false";

        var creationExpression = columns.Contains("creation_ts")
            ? "creation_ts"
            : "NULL";

        return
            "SELECT json_build_object(" +
            "'matrixUserId', name, " +
            $"'isAdmin', {adminExpression}, " +
            $"'isDeactivated', {deactivatedExpression}, " +
            $"'createdAtUnixMs', {creationExpression}" +
            ")::text " +
            "FROM public.users " +
            "ORDER BY name;";
    }

    private static IEnumerable<string> ParseLines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string ExtractUsername(string matrixUserId)
    {
        var value = matrixUserId.Trim();

        if (value.StartsWith('@'))
        {
            value = value[1..];
        }

        var serverSeparator = value.IndexOf(':');
        var localpart = serverSeparator >= 0
            ? value[..serverSeparator]
            : value;

        if (string.IsNullOrWhiteSpace(localpart))
        {
            throw new RuntimeStackUserInventoryException(
                "synapse_users_inventory_invalid",
                "Synapse returned a Matrix account with an invalid local username.");
        }

        return localpart;
    }

    private static DateTime? ToCreatedAtUtc(long? unixMilliseconds)
    {
        if (!unixMilliseconds.HasValue || unixMilliseconds.Value < 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset
                .FromUnixTimeMilliseconds(unixMilliseconds.Value)
                .UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record SynapseInventoryRow(
        string MatrixUserId,
        bool IsAdmin,
        bool IsDeactivated,
        long? CreatedAtUnixMs);
}
