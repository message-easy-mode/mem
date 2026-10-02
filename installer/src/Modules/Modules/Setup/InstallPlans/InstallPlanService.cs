using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Setup;
using Modules.Setup.InstallRuns;
using Modules.Setup.HostChecks;

namespace Modules.Setup.InstallPlans;

public sealed class InstallPlanService
{
    private static readonly SemaphoreSlim BeginSetupGate = new(1, 1);
    private static readonly SemaphoreSlim RunInstallGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly MemDbContext _db;
    private readonly IInstallRunCoordinator _runCoordinator;
    private readonly ILogger<InstallPlanService> _logger;
    private readonly InstallProgressStore? _progressStore;

    public InstallPlanService(
        MemDbContext db,
        ILogger<InstallPlanService> logger,
        IInstallRunCoordinator runCoordinator,
        InstallProgressStore? progressStore = null)
    {
        _db = db;
        _logger = logger;
        _runCoordinator = runCoordinator;
        _progressStore = progressStore;
    }

    public Task<InstallPlanResponse> CreateAsync(CancellationToken cancellationToken) =>
        BeginSetupAsync(cancellationToken);

    public async Task<InstallPlanResponse> BeginSetupAsync(CancellationToken cancellationToken)
    {
        await BeginSetupGate.WaitAsync(cancellationToken);
        try
        {
            var completedInstallationId = await _db.Installations
                .AsNoTracking()
                .Where(x => x.Status == InstallationStatuses.Succeeded)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (completedInstallationId.HasValue)
            {
                throw new InvalidOperationException(
                    $"First-time setup cannot begin because installation '{completedInstallationId.Value}' has already completed.");
            }

            var current = await _db.Installations
                .OrderByDescending(x => x.UpdatedAtUtc)
                .FirstOrDefaultAsync(
                    x => InstallationStatuses.ActiveFirstTimeSetup.Contains(x.Status),
                    cancellationToken);

            if (current is not null)
            {
                _logger.LogInformation(
                    "Reusing active first-time setup authority {InstallationId} with status {InstallationStatus}",
                    current.Id,
                    current.Status);

                return ToResponse(current);
            }

            var now = DateTime.UtcNow;
            var config = InstallPlanFactory.CreateDefault();

            var entity = new InstallationEntity
            {
                Id = Guid.NewGuid(),
                Status = InstallationStatuses.Draft,
                ConfigJson = SerializeConfig(config),
                FrozenConfigJson = null,
                LastError = null,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            _db.Installations.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Created durable first-time setup authority {InstallationId}",
                entity.Id);

            return ToResponse(entity);
        }
        finally
        {
            BeginSetupGate.Release();
        }
    }

    public async Task<InstallPlanResponse?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var entity = await _db.Installations
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(x =>
                InstallationStatuses.ActiveFirstTimeSetup.Contains(x.Status),
                cancellationToken);

        return entity is null ? null : ToResponse(entity);
    }

    public async Task<InstallPlanResponse?> GetAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var entity = await _db.Installations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == installationId, cancellationToken);

        return entity is null ? null : ToResponse(entity);
    }

    public async Task<bool> RecordPreflightSnapshotAsync(
        HostCheckRunResponse run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        var entity = await _db.Installations
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(
                x => x.Status == InstallationStatuses.Draft ||
                     x.Status == InstallationStatuses.Ready,
                cancellationToken);

        if (entity is null)
        {
            return false;
        }

        var allChecks = run.Groups.SelectMany(group => group.Checks).ToArray();
        var blockingIssues = allChecks.Count(check =>
            check.Blocking &&
            check.Status is HostCheckStatus.Fail or
                HostCheckStatus.Warning or
                HostCheckStatus.Unknown or
                HostCheckStatus.Unavailable);

        var snapshot = new PreflightSetupConfig(
            RunId: run.Id,
            CompletedAtUtc: run.CompletedAtUtc ?? DateTimeOffset.UtcNow,
            RunStatus: run.Status.ToString(),
            Passed: run.Summary.Passed,
            Warnings: run.Summary.Warnings,
            Failed: run.Summary.Failed,
            Skipped: run.Summary.Skipped,
            Unavailable: run.Summary.Unavailable,
            Unknown: run.Summary.Unknown,
            BlockingIssueCount: blockingIssues,
            WarningCheckKeys: allChecks
                .Where(check => check.Status == HostCheckStatus.Warning)
                .Select(check => check.Key)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray(),
            UnavailableCheckKeys: allChecks
                .Where(check => check.Status == HostCheckStatus.Unavailable)
                .Select(check => check.Key)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray());

        var config = DeserializeConfig(entity.ConfigJson) with
        {
            Preflight = snapshot,
            Review = null
        };

        entity.ConfigJson = SerializeConfig(config);
        entity.FrozenConfigJson = null;
        if (entity.Status == InstallationStatuses.Ready)
        {
            entity.Status = InstallationStatuses.Draft;
        }
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Persisted host-check snapshot {RunId} into first-time setup authority {InstallationId}. Passed={Passed} Warnings={Warnings} Failed={Failed} Unavailable={Unavailable}",
            run.Id,
            entity.Id,
            snapshot.Passed,
            snapshot.Warnings,
            snapshot.Failed,
            snapshot.Unavailable);

        return true;
    }

    public async Task<RemoveInstallPlanResponse> RemoveAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var entity = await _db.Installations
            .FirstOrDefaultAsync(x => x.Id == installationId, cancellationToken);

        if (entity is null)
        {
            return new RemoveInstallPlanResponse(
                installationId,
                Success: false,
                Message: "Installation was not found.");
        }

        if (entity.Status == InstallationStatuses.Running)
        {
            return new RemoveInstallPlanResponse(
                installationId,
                Success: false,
                Message: "Running installations cannot be removed.");
        }

        _db.Installations.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return new RemoveInstallPlanResponse(
            installationId,
            Success: true,
            Message: "Installation draft removed.");
    }

    public async Task<InstallPlanResponse?> UpdateGeneralAsync(
        Guid installationId,
        UpdateGeneralConfigRequest request,
        CancellationToken cancellationToken)
    {
        return await UpdateConfigAsync(
            installationId,
            config => config with
            {
                General = new GeneralSetupConfig(
                    Mode: NormalizeMode(request.Mode),
                    InstallName: NormalizeNullable(request.InstallName),
                    PublicBaseHostname: NormalizeNullable(request.PublicBaseHostname))
            },
            cancellationToken);
    }

    public async Task<InstallPlanResponse?> UpdatePlatformAsync(
        Guid installationId,
        PlatformSetupConfig request,
        CancellationToken cancellationToken)
    {
        return await UpdateConfigAsync(
            installationId,
            config => config with
            {
                Platform = NormalizePlatform(request)
            },
            cancellationToken);
    }

    public async Task<InstallPlanResponse?> UpdateSupportToolsAsync(
        Guid installationId,
        SupportToolsSetupConfig request,
        CancellationToken cancellationToken)
    {
        return await UpdateConfigAsync(
            installationId,
            config => config with
            {
                SupportTools = NormalizeSupportTools(request)
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<InstallStepExecutionResponse>> GetStepsAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var steps = await _db.InstallationStepExecutions
            .AsNoTracking()
            .Where(x => x.InstallationId == installationId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(cancellationToken);

        var progressByStepId = new Dictionary<Guid, InstallProgressSnapshot>();
        if (_progressStore is not null)
        {
            var history = await _progressStore.GetHistoryAsync(
                installationId,
                cancellationToken);

            foreach (var stepHistory in history
                         .GroupBy(x => x.StepId)
                         .Select(group => group
                             .OrderByDescending(x => x.AttemptNumber)
                             .ThenByDescending(x => x.LastActivityAtUtc)
                             .First()))
            {
                progressByStepId[stepHistory.StepId] = stepHistory;
            }
        }

        return steps
            .Select(x => new InstallStepExecutionResponse(
                x.Id,
                x.StepName,
                x.Sequence,
                x.Status,
                x.Message,
                x.ErrorMessage,
                x.AttemptCount,
                SetupUtcDateTime.ToOffset(x.StartedAtUtc),
                SetupUtcDateTime.ToOffset(x.CompletedAtUtc),
                Progress: ResolveCompatibleProgress(
                    x.Status,
                    progressByStepId.GetValueOrDefault(x.Id))))
            .ToList();
    }

    private static InstallProgressSnapshot? ResolveCompatibleProgress(
        string authoritativeStepStatus,
        InstallProgressSnapshot? progress)
    {
        if (progress is null)
        {
            return null;
        }

        return string.Equals(
            authoritativeStepStatus,
            progress.StepStatus,
            StringComparison.OrdinalIgnoreCase)
            ? progress
            : null;
    }

    public async Task<RunInstallPlanResponse?> RunAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        await RunInstallGate.WaitAsync(cancellationToken);
        try
        {
            return await RunLockedAsync(installationId, cancellationToken);
        }
        finally
        {
            RunInstallGate.Release();
        }
    }

    private async Task<RunInstallPlanResponse?> RunLockedAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var entity = await _db.Installations
                .FirstOrDefaultAsync(x => x.Id == installationId, cancellationToken);

            if (entity is null)
            {
                return null;
            }

            if (entity.Status == InstallationStatuses.Running)
            {
                var queueResult = _runCoordinator.Queue(installationId);

                return new RunInstallPlanResponse(
                    entity.Id,
                    Accepted: true,
                    entity.Status,
                    queueResult.AlreadyOwned
                        ? "Installation is already running under the server-owned worker."
                        : "Installation execution has been re-queued from durable Running state.");
            }

            if (entity.Status == InstallationStatuses.Succeeded)
            {
                return new RunInstallPlanResponse(
                    entity.Id,
                    Accepted: true,
                    entity.Status,
                    "Installation has already completed.");
            }

            if (entity.Status == InstallationStatuses.Draft ||
                string.IsNullOrWhiteSpace(entity.FrozenConfigJson))
            {
                return new RunInstallPlanResponse(
                    entity.Id,
                    Accepted: false,
                    entity.Status,
                    "Review acceptance is required before platform installation can start.");
            }

            var frozen = DeserializeConfig(entity.FrozenConfigJson);
            if (!SetupReviewPlanFingerprint.Matches(frozen))
            {
                return new RunInstallPlanResponse(
                    entity.Id,
                    Accepted: false,
                    entity.Status,
                    "The frozen installation plan does not match its Review fingerprint. Return to Review and accept the plan again.");
            }

            var now = DateTime.UtcNow;
            entity.Status = InstallationStatuses.Running;
            entity.StartedAtUtc ??= now;
            entity.CompletedAtUtc = null;
            entity.LastError = null;
            entity.UpdatedAtUtc = now;

            var hasSteps = await _db.InstallationStepExecutions
                .AnyAsync(x => x.InstallationId == entity.Id, cancellationToken);

            if (!hasSteps)
            {
                AddInitialSteps(entity.Id);
            }
            else
            {
                await ResetRetryableStepsAsync(entity.Id, cancellationToken);
            }

            try
            {
                await _db.SaveChangesAsync(cancellationToken);

                var queueResult = _runCoordinator.Queue(installationId);

                return new RunInstallPlanResponse(
                    entity.Id,
                    Accepted: true,
                    entity.Status,
                    queueResult.AlreadyOwned
                        ? "Installation is already owned by the server worker."
                        : "Installation workflow has started under the server-owned worker.");
            }
            catch (DbUpdateConcurrencyException) when (attempt == 1)
            {
                _db.ChangeTracker.Clear();
            }
        }

        var current = await _db.Installations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == installationId, cancellationToken);

        if (current is not null && current.Status == InstallationStatuses.Running)
        {
            _runCoordinator.Queue(installationId);
        }

        return current is null
            ? null
            : new RunInstallPlanResponse(
                current.Id,
                Accepted: current.Status != InstallationStatuses.Draft && !string.IsNullOrWhiteSpace(current.FrozenConfigJson),
                current.Status,
                "Installation state changed while starting. Current state has been returned.");
    }

    private async Task<InstallPlanResponse?> UpdateConfigAsync(
        Guid installationId,
        Func<InstallPlan, InstallPlan> update,
        CancellationToken cancellationToken)
    {
        var entity = await _db.Installations
            .FirstOrDefaultAsync(x => x.Id == installationId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        if (entity.Status != InstallationStatuses.Draft && entity.Status != InstallationStatuses.Ready)
        {
            throw new InvalidOperationException(
                $"Installation '{installationId}' cannot be edited while status is '{entity.Status}'.");
        }

        var currentConfig = DeserializeConfig(entity.ConfigJson);
        var nextConfig = update(currentConfig) with { Review = null };

        entity.ConfigJson = SerializeConfig(nextConfig);
        entity.FrozenConfigJson = null;
        if (entity.Status == InstallationStatuses.Ready)
        {
            entity.Status = InstallationStatuses.Draft;
        }
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return ToResponse(entity);
    }

    private void AddInitialSteps(Guid installationId)
    {
        var steps = InstallStepNames.InitialSteps;

        for (var i = 0; i < steps.Count; i++)
        {
            _db.InstallationStepExecutions.Add(new InstallationStepExecutionEntity
            {
                Id = Guid.NewGuid(),
                InstallationId = installationId,
                StepName = steps[i],
                Sequence = i + 1,
                Status = InstallationStepStatuses.Pending,
                Message = null,
                ErrorMessage = null,
                AttemptCount = 0,
                StartedAtUtc = null,
                CompletedAtUtc = null
            });
        }
    }

    private static InstallPlan DeserializeConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return InstallPlanFactory.CreateDefault();
        }

        var parsed = JsonSerializer.Deserialize<InstallPlan>(json, JsonOptions);

        if (parsed is null)
        {
            return InstallPlanFactory.CreateDefault();
        }

        return parsed with
        {
            Platform = NormalizePlatform(parsed.Platform),
            PublicAccess = parsed.PublicAccess ?? InstallPlanFactory.CreateDefault().PublicAccess
        };
    }

    private static string SerializeConfig(InstallPlan config)
    {
        return JsonSerializer.Serialize(config, JsonOptions);
    }

    private static InstallPlanResponse ToResponse(InstallationEntity entity)
    {
        return new InstallPlanResponse(
            entity.Id,
            entity.Status,
            entity.ConfigJson,
            entity.FrozenConfigJson,
            entity.LastError,
            SetupUtcDateTime.ToOffset(entity.CreatedAtUtc),
            SetupUtcDateTime.ToOffset(entity.UpdatedAtUtc),
            SetupUtcDateTime.ToOffset(entity.StartedAtUtc),
            SetupUtcDateTime.ToOffset(entity.CompletedAtUtc));
    }

    private static string NormalizeMode(string mode)
    {
        return mode switch
        {
            "Easy" => "Easy",
            "Advanced" => "Advanced",
            "InspectOnly" => "InspectOnly",
            _ => "Easy"
        };
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static PlatformSetupConfig NormalizePlatform(PlatformSetupConfig value)
    {
        return value with
        {
            Postgres = value.Postgres with
            {
                ContainerName = Required(value.Postgres.ContainerName, "mem-postgres"),
                DatabaseName = Required(value.Postgres.DatabaseName, "mem"),
                Username = Required(value.Postgres.Username, "postgres"),
                VolumeName = Required(value.Postgres.VolumeName, "mem_postgres_data")
            },
            Ingress = value.Ingress with
            {
                Provider = value.Ingress.Provider == "Npm" ? "Npm" : "Npm",
                ContainerName = Required(value.Ingress.ContainerName, "mem-npm"),
                HttpPort = NormalizePort(value.Ingress.HttpPort, 80),
                HttpsPort = NormalizePort(value.Ingress.HttpsPort, 443),
                AdminPort = NormalizePort(value.Ingress.AdminPort, 81)
            },
            MemApi = value.MemApi with
            {
                Enabled = false,
                ContainerName = Required(value.MemApi.ContainerName, "mem-api"),
                Image = Required(value.MemApi.Image, "mem-api:local"),
                InternalPort = NormalizePort(value.MemApi.InternalPort, 7000)
            },
            MemWeb = value.MemWeb with
            {
                Enabled = false,
                ContainerName = Required(value.MemWeb.ContainerName, "mem-web"),
                Image = Required(value.MemWeb.Image, "mem-web:local"),
                InternalPort = NormalizePort(value.MemWeb.InternalPort, 3000)
            }
        };
    }

    private static SupportToolsSetupConfig NormalizeSupportTools(SupportToolsSetupConfig value)
    {
        return value with
        {
            Seq = value.Seq with
            {
                ContainerName = Required(value.Seq.ContainerName, "mem-seq"),
                HostPort = NormalizePort(value.Seq.HostPort, 5341)
            },
            PgAdmin = value.PgAdmin with
            {
                ContainerName = Required(value.PgAdmin.ContainerName, "mem-pgadmin"),
                HostPort = NormalizePort(value.PgAdmin.HostPort, 5050)
            },
            Portainer = value.Portainer with
            {
                ContainerName = Required(value.Portainer.ContainerName, "portainer"),
                HostPort = NormalizePort(value.Portainer.HostPort, 9443)
            }
        };
    }

    private static string Required(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int NormalizePort(int value, int fallback)
    {
        return value is >= 1 and <= 65535 ? value : fallback;
    }

    private async Task ResetRetryableStepsAsync(
    Guid installationId,
    CancellationToken cancellationToken)
    {
        var steps = await _db.InstallationStepExecutions
            .Where(x => x.InstallationId == installationId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(cancellationToken);

        var firstRetryableStep = steps
            .Where(x =>
                x.Status == InstallationStepStatuses.Failed ||
                x.Status == InstallationStepStatuses.WaitingForUser ||
                x.Status == InstallationStepStatuses.Running)
            .OrderBy(x => x.Sequence)
            .FirstOrDefault();

        if (firstRetryableStep is null)
        {
            return;
        }

        foreach (var step in steps.Where(x => x.Sequence >= firstRetryableStep.Sequence))
        {
            step.Status = InstallationStepStatuses.Pending;
            step.Message = null;
            step.ErrorMessage = null;
            step.StartedAtUtc = null;
            step.CompletedAtUtc = null;
        }
    }
}