using HostAgent.Runtime.Manifests;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HostAgent.Matrix.Users;

public interface IRuntimeStackUserInventorySynchronizer
{
    Task<RuntimeStackUserInventorySynchronizationResult> SynchronizeAsync(
        RuntimeStackManifest manifest,
        CancellationToken ct);
}

public sealed class RuntimeStackUserInventoryReconciliationService
    : IRuntimeStackUserInventorySynchronizer
{
    private readonly MemDbContext _db;
    private readonly ISynapseUserInventoryReader _inventoryReader;
    private readonly ILogger<RuntimeStackUserInventoryReconciliationService> _logger;

    public RuntimeStackUserInventoryReconciliationService(
        MemDbContext db,
        ISynapseUserInventoryReader inventoryReader,
        ILogger<RuntimeStackUserInventoryReconciliationService> logger)
    {
        _db = db;
        _inventoryReader = inventoryReader;
        _logger = logger;
    }

    public async Task<RuntimeStackUserInventorySynchronizationResult> SynchronizeAsync(
        RuntimeStackManifest manifest,
        CancellationToken ct)
    {
        var database = await _db.RuntimeStackDatabases
            .FirstOrDefaultAsync(x => x.RuntimeStackId == manifest.StackId, ct);

        if (database is null)
        {
            throw new RuntimeStackUserInventoryException(
                "runtime_stack_database_not_owned",
                "MEM has no owned Synapse database record for this runtime stack.");
        }

        SynapseUserInventorySnapshot snapshot;
        var attemptedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            snapshot = await _inventoryReader.ReadAsync(
                new RuntimeStackUserInventoryTarget(
                    manifest.StackId,
                    database.DatabaseName),
                ct);

            ValidateSnapshot(snapshot);
        }
        catch (RuntimeStackUserInventoryException ex)
        {
            await RecordFailureAsync(database, manifest.StackId, attemptedAtUtc, ex.Code, ct);
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            const string code = "matrix_user_inventory_unavailable";
            await RecordFailureAsync(database, manifest.StackId, attemptedAtUtc, code, ct);

            throw new RuntimeStackUserInventoryException(
                code,
                "The Matrix user inventory is currently unavailable.",
                ex);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var existing = await _db.RuntimeStackUsers
            .Where(x => x.RuntimeStackId == manifest.StackId)
            .ToListAsync(ct);

        var byMatrixUserId = existing
            .Where(x => !string.IsNullOrWhiteSpace(x.MatrixUserId))
            .GroupBy(x => x.MatrixUserId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Single(), StringComparer.OrdinalIgnoreCase);

        var byUsername = existing
            .GroupBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Single(), StringComparer.OrdinalIgnoreCase);

        var matchedIds = new HashSet<Guid>();
        var insertedCount = 0;
        var updatedCount = 0;

        foreach (var account in snapshot.Accounts)
        {
            byMatrixUserId.TryGetValue(account.MatrixUserId, out var byId);
            byUsername.TryGetValue(account.Username, out var byName);

            if (byId is not null && byName is not null && byId.Id != byName.Id)
            {
                throw new RuntimeStackUserInventoryException(
                    "runtime_stack_user_projection_ambiguous",
                    "The MEM Matrix user projection contains conflicting account identities.");
            }

            var entity = byId ?? byName;
            var created = entity is null;

            if (entity is null)
            {
                entity = new RuntimeStackUserEntity
                {
                    Id = Guid.NewGuid(),
                    RuntimeStackId = manifest.StackId,
                    MatrixInstanceId = manifest.Matrix.InstanceId,
                    Username = account.Username,
                    IsFirstAdmin = false,
                    CreatedAtUtc = account.CreatedAtUtc ?? snapshot.ReadAtUtc.UtcDateTime
                };

                _db.RuntimeStackUsers.Add(entity);
                existing.Add(entity);
                insertedCount++;
            }
            else
            {
                updatedCount++;
            }

            var existingOrigin = RuntimeStackUserProjectionMetadata.ReadOrigin(entity);
            var failedMemCreationWithoutMatrixIdentity =
                !created &&
                string.Equals(entity.Status, "failed", StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(entity.MatrixUserId) &&
                string.Equals(
                    existingOrigin,
                    RuntimeStackUserProjectionMetadata.MemCreated,
                    StringComparison.Ordinal);
            var origin = created
                ? RuntimeStackUserProjectionMetadata.SynapseDiscovered
                : failedMemCreationWithoutMatrixIdentity
                    ? RuntimeStackUserProjectionMetadata.MemCreationUnconfirmed
                    : existingOrigin;

            entity.MatrixInstanceId = manifest.Matrix.InstanceId;
            entity.Username = account.Username;
            entity.MatrixUserId = account.MatrixUserId;
            entity.IsAdmin = account.IsAdmin;
            entity.Status = account.IsDeactivated ? "deactivated" : "active";
            entity.LastError = null;
            entity.UpdatedAtUtc = snapshot.ReadAtUtc.UtcDateTime;
            entity.MatrixSyncedAtUtc = snapshot.ReadAtUtc.UtcDateTime;
            entity.MetadataJson = RuntimeStackUserProjectionMetadata.MergeInventory(
                entity,
                origin,
                snapshot.ReadAtUtc);

            matchedIds.Add(entity.Id);
        }

        var missingCount = 0;

        foreach (var entity in existing.Where(x => !matchedIds.Contains(x.Id)))
        {
            if (entity.Status is "pending" or "failed")
            {
                continue;
            }

            entity.Status = "missing";
            entity.UpdatedAtUtc = snapshot.ReadAtUtc.UtcDateTime;
            entity.MatrixSyncedAtUtc = snapshot.ReadAtUtc.UtcDateTime;
            entity.LastError = null;
            entity.MetadataJson = RuntimeStackUserProjectionMetadata.MergeInventory(
                entity,
                RuntimeStackUserProjectionMetadata.ReadOrigin(entity),
                snapshot.ReadAtUtc);
            missingCount++;
        }

        database.MetadataJson = RuntimeStackUserInventoryMetadata.WriteSuccess(
            database.MetadataJson,
            snapshot);
        database.UpdatedAtUtc = snapshot.ReadAtUtc.UtcDateTime;

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Matrix user inventory synchronized. RuntimeStackId={RuntimeStackId} Users={UserCount} ActiveAdmins={ActiveAdminCount} Inserted={InsertedCount} Updated={UpdatedCount} Missing={MissingCount}",
            manifest.StackId,
            snapshot.Accounts.Count,
            snapshot.ActiveAdminCount,
            insertedCount,
            updatedCount,
            missingCount);

        return new RuntimeStackUserInventorySynchronizationResult(
            RuntimeStackId: manifest.StackId,
            Source: snapshot.Source,
            Accounts: snapshot.Accounts,
            InsertedCount: insertedCount,
            UpdatedCount: updatedCount,
            MissingCount: missingCount,
            SynchronizedAtUtc: snapshot.ReadAtUtc);
    }

    private async Task RecordFailureAsync(
        RuntimeStackDatabaseEntity database,
        Guid runtimeStackId,
        DateTimeOffset attemptedAtUtc,
        string errorCode,
        CancellationToken ct)
    {
        database.MetadataJson = RuntimeStackUserInventoryMetadata.WriteFailure(
            database.MetadataJson,
            SynapsePostgresUserInventoryReader.InventorySource,
            attemptedAtUtc,
            errorCode);
        database.UpdatedAtUtc = attemptedAtUtc.UtcDateTime;

        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Matrix user inventory synchronization failed. RuntimeStackId={RuntimeStackId} Code={Code}",
            runtimeStackId,
            errorCode);
    }

    private static void ValidateSnapshot(SynapseUserInventorySnapshot snapshot)
    {
        var duplicateMatrixId = snapshot.Accounts
            .GroupBy(x => x.MatrixUserId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);

        var duplicateUsername = snapshot.Accounts
            .GroupBy(x => x.Username, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicateMatrixId is not null || duplicateUsername is not null)
        {
            throw new RuntimeStackUserInventoryException(
                "synapse_users_inventory_ambiguous",
                "Synapse returned an ambiguous Matrix user inventory.");
        }
    }
}
