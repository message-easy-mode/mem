using System.Security.Claims;
using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;

namespace Modules.Operator.Diagnostics.Services;

public sealed record SeqBootstrapProgressDocument(
    IReadOnlyList<DiagnosticsSeqBootstrapOperationCheck> Checks,
    IReadOnlyList<string> Warnings);

public sealed class DiagnosticsSeqBootstrapExecutionService(
    SeqBootstrapReviewStore reviewStore,
    SeqBootstrapOperationQueue queue,
    MemDbContext db,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan OperationLock = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] CheckCodes =
    [
        "approved_image",
        "storage",
        "password_hash",
        "administrator_secret",
        "bootstrap_state",
        "docker_runtime",
        "seq_health",
        "ingestion_connection",
        "event_delivery"
    ];

    public async Task<DiagnosticsSeqBootstrapExecuteResponse> QueueAsync(
        DiagnosticsSeqBootstrapExecuteRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);

        var review = reviewStore.GetRequired(request.ReviewId);
        if (review.AdministratorPasswordRequired)
        {
            ValidatePassword(
                request.AdministratorPassword,
                request.AdministratorPasswordConfirmation);
            if (!string.IsNullOrEmpty(request.ConnectionAdministratorPassword))
            {
                throw new SeqOperationException(
                    "seq_connection_administrator_password_not_required",
                    StatusCodes.Status409Conflict,
                    "The new Seq administrator password will be reused transiently for MEM connection setup. Refresh Seq setup before continuing.");
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(request.AdministratorPassword) ||
                !string.IsNullOrEmpty(request.AdministratorPasswordConfirmation))
            {
                throw new SeqOperationException(
                    "seq_administrator_password_not_required",
                    StatusCodes.Status409Conflict,
                    "An administrator password hash is already configured. Refresh Seq setup before continuing.");
            }

            ValidateCurrentAdministratorPassword(
                request.ConnectionAdministratorPassword);
        }

        var now = timeProvider.GetUtcNow();
        var nowUtc = now.UtcDateTime;
        var active = await db.RuntimeOperations
            .AsNoTracking()
            .AnyAsync(
                entity => (entity.Status == "queued" || entity.Status == "running") &&
                          entity.Operation.StartsWith("seq.") &&
                          (entity.LockedUntilUtc == null || entity.LockedUntilUtc > nowUtc),
                cancellationToken);
        if (active)
        {
            throw new SeqOperationException(
                "seq_operation_in_progress",
                StatusCodes.Status409Conflict,
                "Another Seq operation is already running. Wait for it to finish before starting guided setup.");
        }

        var entity = new RuntimeOperationEntity
        {
            Id = Guid.NewGuid(),
            RuntimeStackId = null,
            RestoreAttemptId = null,
            Operation = "seq.bootstrap",
            Status = "queued",
            RequestedBy = ResolveOperatorName(principal),
            RequestedByUserId = ResolveOperatorId(principal),
            RequestedAtUtc = nowUtc,
            StartedAtUtc = null,
            CurrentStep = "reviewed",
            AttemptCount = 1,
            LockedUntilUtc = now.Add(OperationLock).UtcDateTime,
            InputJson = JsonSerializer.Serialize(
                new
                {
                    operation = "seq.bootstrap",
                    reviewId = review.ReviewId,
                    enableEventDelivery = review.EnableEventDelivery
                },
                JsonOptions),
            EvidenceJson = JsonSerializer.Serialize(
                InitialProgress(),
                JsonOptions),
            HostMutationLevel = "container-create",
            RequiresConfirmation = true,
            ConfirmedAtUtc = nowUtc
        };

        db.RuntimeOperations.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        var password = review.AdministratorPasswordRequired
            ? request.AdministratorPassword.ToCharArray()
            : [];
        var connectionPassword = review.CurrentAdministratorPasswordRequired
            ? request.ConnectionAdministratorPassword.ToCharArray()
            : [];
        var workItem = new SeqBootstrapWorkItem(
            entity.Id,
            review,
            password,
            entity.RequestedByUserId,
            entity.RequestedBy ?? "mem-operator",
            connectionPassword);
        if (!queue.TryEnqueue(workItem))
        {
            workItem.Dispose();
            entity.Status = "failed";
            entity.CurrentStep = "failed";
            entity.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            entity.LockedUntilUtc = null;
            entity.LastError = "seq_bootstrap_queue_unavailable";
            entity.ResultJson = JsonSerializer.Serialize(
                new { status = "failed", warningCode = entity.LastError },
                JsonOptions);
            await db.SaveChangesAsync(CancellationToken.None);
            throw new SeqOperationException(
                "seq_bootstrap_queue_unavailable",
                StatusCodes.Status503ServiceUnavailable,
                "The Seq setup worker is busy. Wait for the current operation to finish and review setup again.");
        }

        reviewStore.Remove(review.ReviewId);
        return new DiagnosticsSeqBootstrapExecuteResponse(
            SchemaVersion: 1,
            OperationId: entity.Id,
            Status: "queued",
            AcceptedAtUtc: now);
    }

    public async Task<DiagnosticsSeqBootstrapOperationResponse> GetOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var entity = await db.RuntimeOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == operationId && item.Operation == "seq.bootstrap",
                cancellationToken)
            ?? throw new SeqOperationException(
                "seq_bootstrap_operation_not_found",
                StatusCodes.Status404NotFound,
                "The requested Seq setup operation was not found.");

        var progress = ReadProgress(entity.EvidenceJson);
        return new DiagnosticsSeqBootstrapOperationResponse(
            SchemaVersion: 1,
            OperationId: entity.Id,
            Status: entity.Status,
            CurrentStep: entity.CurrentStep ?? "unknown",
            RequestedAtUtc: new DateTimeOffset(
                DateTime.SpecifyKind(entity.RequestedAtUtc, DateTimeKind.Utc)),
            StartedAtUtc: ToOffset(entity.StartedAtUtc),
            CompletedAtUtc: ToOffset(entity.CompletedAtUtc),
            Checks: progress.Checks,
            WarningCode: entity.LastError,
            Warnings: progress.Warnings);
    }

    internal static SeqBootstrapProgressDocument InitialProgress() =>
        new(
            CheckCodes
                .Select(code => new DiagnosticsSeqBootstrapOperationCheck(
                    code,
                    "not-started"))
                .ToArray(),
            []);

    internal static SeqBootstrapProgressDocument ReadProgress(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return InitialProgress();
        }

        try
        {
            return JsonSerializer.Deserialize<SeqBootstrapProgressDocument>(
                       json,
                       JsonOptions)
                   ?? InitialProgress();
        }
        catch (JsonException)
        {
            return InitialProgress() with
            {
                Warnings = ["seq_bootstrap_progress_invalid"]
            };
        }
    }

    private static void ValidatePassword(string password, string confirmation)
    {
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            throw new SeqOperationException(
                "seq_administrator_password_mismatch",
                StatusCodes.Status400BadRequest,
                "The Seq administrator password confirmation does not match.");
        }

        if (string.IsNullOrWhiteSpace(password) ||
            password.Length is < 14 or > 256 ||
            password.Contains('\0') ||
            password.Contains('\r') ||
            password.Contains('\n') ||
            password.Distinct().Take(4).Count() < 4)
        {
            throw new SeqOperationException(
                "seq_administrator_password_weak",
                StatusCodes.Status400BadRequest,
                "Use a Seq administrator password with at least 14 characters and four distinct characters.");
        }
    }

    private static void ValidateCurrentAdministratorPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length > 256)
        {
            throw new SeqOperationException(
                "seq_connection_administrator_password_required",
                StatusCodes.Status400BadRequest,
                "Enter the current Seq administrator password so MEM can provision and verify its dedicated ingestion credential.");
        }
    }

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

    private static string ResolveOperatorName(ClaimsPrincipal principal) =>
        principal.Identity?.Name?.Trim() is { Length: > 0 } name
            ? name
            : "mem-operator";

    private static Guid? ResolveOperatorId(ClaimsPrincipal principal) =>
        Guid.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            out var parsed)
            ? parsed
            : null;
}
