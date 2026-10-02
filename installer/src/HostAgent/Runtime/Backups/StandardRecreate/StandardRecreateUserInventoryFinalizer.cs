using HostAgent.Matrix.Users;
using HostAgent.Runtime.Manifests;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Backups.StandardRecreate;

public sealed class StandardRecreateUserInventoryFinalizer
{
    public const string CheckCode = "production-recreate.matrix-users.synchronized";

    private const string RetryWarning =
        "The restored Matrix accounts remain in Synapse, but MEM could not synchronize the Users inventory. Retry synchronization from the restored stack's Users area.";

    private readonly IRuntimeStackUserInventorySynchronizer _synchronizer;
    private readonly ILogger<StandardRecreateUserInventoryFinalizer> _logger;

    public StandardRecreateUserInventoryFinalizer(
        IRuntimeStackUserInventorySynchronizer synchronizer,
        ILogger<StandardRecreateUserInventoryFinalizer> logger)
    {
        _synchronizer = synchronizer;
        _logger = logger;
    }

    public async Task<StandardRecreateUserInventoryFinalizationResult> FinalizeAsync(
        RuntimeStackManifest manifest,
        long? restoredDatabaseUserCount,
        CancellationToken ct)
    {
        try
        {
            var synchronized = await _synchronizer.SynchronizeAsync(manifest, ct);
            var summary = new StandardRecreateUserInventorySummary(
                Status: RuntimeStackUserInventoryStates.Synchronized,
                Source: synchronized.Source,
                UserCount: synchronized.UserCount,
                ActiveAdminCount: synchronized.ActiveAdminCount,
                SynchronizedAtUtc: synchronized.SynchronizedAtUtc,
                ErrorCode: null);

            var countMismatch = restoredDatabaseUserCount.HasValue &&
                                restoredDatabaseUserCount.Value != synchronized.UserCount;

            return new StandardRecreateUserInventoryFinalizationResult(
                Summary: summary,
                Check: new StandardRecreateCheck(
                    Code: CheckCode,
                    Severity: "info",
                    Passed: true,
                    Message: "Recovered Matrix user inventory was synchronized into MEM.",
                    Detail: BuildDetail(summary)),
                Warning: countMismatch
                    ? $"The restored database reported {restoredDatabaseUserCount.Value} Matrix users, but authoritative synchronization returned {synchronized.UserCount}. The stack remains restored; retry synchronization from the Users area and review the database if the mismatch continues."
                    : null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (RuntimeStackUserInventoryException ex)
        {
            _logger.LogWarning(
                "Standard Recreate user inventory synchronization needs retry. RuntimeStackId={RuntimeStackId} Code={Code}",
                manifest.StackId,
                ex.Code);

            return Failed(ex.Code);
        }
        catch (Exception ex)
        {
            const string code = "matrix_user_inventory_unavailable";

            _logger.LogError(
                ex,
                "Standard Recreate user inventory synchronization failed unexpectedly. RuntimeStackId={RuntimeStackId}",
                manifest.StackId);

            return Failed(code);
        }
    }

    private static StandardRecreateUserInventoryFinalizationResult Failed(string errorCode)
    {
        var summary = new StandardRecreateUserInventorySummary(
            Status: RuntimeStackUserInventoryStates.Failed,
            Source: SynapsePostgresUserInventoryReader.InventorySource,
            UserCount: null,
            ActiveAdminCount: null,
            SynchronizedAtUtc: null,
            ErrorCode: errorCode);

        return new StandardRecreateUserInventoryFinalizationResult(
            Summary: summary,
            Check: new StandardRecreateCheck(
                Code: CheckCode,
                Severity: "warning",
                Passed: false,
                Message: "The restored Matrix user inventory still needs synchronization in MEM.",
                Detail: BuildDetail(summary)),
            Warning: RetryWarning);
    }

    private static string BuildDetail(StandardRecreateUserInventorySummary summary)
    {
        var values = new List<string>
        {
            $"status={summary.Status}"
        };

        if (!string.IsNullOrWhiteSpace(summary.Source))
        {
            values.Add($"source={summary.Source}");
        }

        if (summary.UserCount.HasValue)
        {
            values.Add($"userCount={summary.UserCount.Value}");
        }

        if (summary.ActiveAdminCount.HasValue)
        {
            values.Add($"activeAdminCount={summary.ActiveAdminCount.Value}");
        }

        if (summary.SynchronizedAtUtc.HasValue)
        {
            values.Add($"synchronizedAtUtc={summary.SynchronizedAtUtc.Value:O}");
        }

        if (!string.IsNullOrWhiteSpace(summary.ErrorCode))
        {
            values.Add($"errorCode={summary.ErrorCode}");
        }

        return string.Join("; ", values);
    }
}

public sealed record StandardRecreateUserInventoryFinalizationResult(
    StandardRecreateUserInventorySummary Summary,
    StandardRecreateCheck Check,
    string? Warning);
