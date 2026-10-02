using System.Security.Claims;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Shared.Domains.Renewal;

namespace Modules.Operator.Dashboard;

/// <summary>
/// Server-side composition for the Home dashboard. The service projects
/// existing durable MEM state and narrow safe observations into one bounded
/// response. It performs no mutations and does not invoke API endpoints over
/// HTTP.
/// </summary>
public sealed class DashboardOverviewService
{
    private const int MaximumStackItems = 5;
    private static readonly TimeSpan CertificateExpiringWindow = TimeSpan.FromDays(30);

    private readonly MemDbContext _db;
    private readonly IDashboardRuntimeProbe _runtimeProbe;
    private readonly DashboardActivityReader _activityReader;
    private readonly TimeProvider _timeProvider;
    private readonly DomainCertificateRenewalProjectionService? _renewalProjection;

    public DashboardOverviewService(
        MemDbContext db,
        IDashboardRuntimeProbe runtimeProbe,
        DashboardActivityReader activityReader,
        TimeProvider timeProvider,
        DomainCertificateRenewalProjectionService? renewalProjection = null)
    {
        _db = db;
        _runtimeProbe = runtimeProbe;
        _activityReader = activityReader;
        _timeProvider = timeProvider;
        _renewalProjection = renewalProjection;
    }

    public async Task<DashboardOverviewResponse> GetOverviewAsync(
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var generatedAtUtc = _timeProvider.GetUtcNow();
        var capabilities = DashboardCapabilityResolver.Resolve(principal);

        var stackRows = await _db.RuntimeStacks
            .AsNoTracking()
            .Where(x => x.Status == null || x.Status != "destroyed")
            .Where(x => x.LastVerifiedStatus == null || x.LastVerifiedStatus != "destroyed")
            .OrderBy(x => x.Slug)
            .Select(x => new StackRow(
                x.Id,
                x.Slug,
                x.LastVerifiedStatus,
                x.LastVerifiedAtUtc,
                x.MatrixPublicBaseUrl,
                x.ElementPublicBaseUrl,
                x.ServiceInstances
                    .Where(service => service.ServiceKey == "matrix")
                    .OrderByDescending(service => service.UpdatedAtUtc)
                    .Select(service => service.ServerName)
                    .FirstOrDefault(),
                x.ServiceInstances
                    .Where(service => service.ServiceKey == "matrix")
                    .OrderByDescending(service => service.UpdatedAtUtc)
                    .Select(service => service.PublicHost)
                    .FirstOrDefault()))
            .ToListAsync(ct);

        var catalogRows = await _db.BackupCatalogEntries
            .AsNoTracking()
            .Select(x => new CatalogRow(
                x.SourceStackSlug,
                x.MatrixServerName,
                x.MatrixHost,
                x.PayloadState,
                x.IntegrityStatus,
                x.PayloadBytes,
                x.CapturedAtUtc))
            .ToListAsync(ct);

        var restoreRows = await _db.RestoreAttempts
            .AsNoTracking()
            .Select(x => new RestoreRow(
                x.RestoreSessionId,
                x.Status,
                x.WarningCount,
                x.ErrorCount,
                x.UpdatedAtUtc,
                x.LastEventAtUtc))
            .ToListAsync(ct);

        var domain = await _db.Domains
            .AsNoTracking()
            .Where(x => x.IsMainPlatformDomain)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new DomainRow(x.Id, x.BaseDomain, x.ActiveCertificateId))
            .FirstOrDefaultAsync(ct);

        CertificateRow? certificate = null;
        if (domain is not null)
        {
            // The global main-platform certificate is the public-access authority.
            // Prefer it over a stale Domain.ActiveCertificateId pointer so Dashboard,
            // Certificate Tools, and the main-domain projection cannot disagree after
            // legacy/migration state drift. The active pointer remains a bounded
            // compatibility fallback when no main certificate is recorded.
            certificate = await _db.Certificates
                .AsNoTracking()
                .Where(x =>
                    x.DomainId == domain.Id &&
                    x.IsMainPlatformCertificate &&
                    x.Status != "Deleted")
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => new CertificateRow(
                    x.CommonName,
                    x.IsStaging,
                    x.IsActive,
                    x.Status,
                    x.ExpiresAtUtc))
                .FirstOrDefaultAsync(ct);

            if (certificate is null && domain.ActiveCertificateId is Guid activeCertificateId)
            {
                certificate = await _db.Certificates
                    .AsNoTracking()
                    .Where(x => x.Id == activeCertificateId)
                    .Select(x => new CertificateRow(
                        x.CommonName,
                        x.IsStaging,
                        x.IsActive,
                        x.Status,
                        x.ExpiresAtUtc))
                    .FirstOrDefaultAsync(ct);
            }
        }

        DomainCertificateRenewalProjection? renewalProjection = null;
        if (domain is not null && _renewalProjection is not null)
        {
            renewalProjection = await _renewalProjection.GetAsync(domain.Id, ct);
        }

        var docker = await _runtimeProbe.ObserveDockerAsync(ct);
        var host = await _runtimeProbe.ObserveHostAsync(ct);
        var services = await _runtimeProbe.ObservePlatformServicesAsync(ct);
        var ingress = await _runtimeProbe.ObserveIngressAsync(ct);

        var stacks = BuildStacks(stackRows);
        var recovery = BuildRecovery(stackRows, catalogRows, restoreRows);
        var platform = BuildPlatform(docker, services);
        var publicAccess = BuildPublicAccess(
            domain,
            certificate,
            renewalProjection,
            ingress,
            generatedAtUtc);
        var onboarding = BuildOnboarding(stacks, capabilities);

        var activity = await BuildActivityAsync(ct);
        var hero = BuildHero(platform, publicAccess, stacks, recovery, capabilities);
        var notices = BuildNotices(hero, platform, publicAccess, recovery);

        return new DashboardOverviewResponse(
            Source: "control-plane",
            GeneratedAtUtc: generatedAtUtc,
            SuggestedRefreshSeconds: 30,
            Capabilities: capabilities,
            Hero: hero,
            Onboarding: onboarding,
            Platform: platform,
            PublicAccess: publicAccess,
            Stacks: stacks,
            Recovery: recovery,
            Host: BuildHost(host),
            Activity: activity,
            Notices: notices);
    }

    private async Task<DashboardActivitySummary> BuildActivityAsync(CancellationToken ct)
    {
        try
        {
            var items = await _activityReader.ReadRecentAsync(ct);
            return new DashboardActivitySummary(
                items.Count == 0 ? DashboardStates.Empty : DashboardStates.Available,
                items);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new DashboardActivitySummary(
                DashboardStates.Unavailable,
                []);
        }
    }

    private static DashboardStacksSummary BuildStacks(
        IReadOnlyList<StackRow> rows)
    {
        var summaries = rows
            .Select(row => new DashboardStackSummary(
                row.Id.ToString("D"),
                row.Slug,
                new DashboardLastVerification(
                    ToLastVerificationState(row.LastVerifiedStatus),
                    ToUtc(row.LastVerifiedAtUtc)),
                row.MatrixPublicBaseUrl,
                row.ElementPublicBaseUrl))
            .ToArray();

        return new DashboardStacksSummary(
            Total: summaries.Length,
            HealthyLastVerifiedCount: summaries.Count(x => x.LastVerification.State == DashboardStates.Passed),
            AttentionCount: summaries.Count(x => x.LastVerification.State == DashboardStates.Failed),
            Items: summaries.Take(MaximumStackItems).ToArray(),
            Truncated: summaries.Length > MaximumStackItems);
    }

    private static DashboardRecoverySummary BuildRecovery(
        IReadOnlyList<StackRow> stacks,
        IReadOnlyList<CatalogRow> catalogRows,
        IReadOnlyList<RestoreRow> restoreRows)
    {
        var validCatalogRows = catalogRows
            .Where(x =>
                string.Equals(x.PayloadState, "available", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.IntegrityStatus, "valid", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Recovery belongs to the Matrix identity being protected, not merely to a
        // mutable MEM stack slug. Standard Recreate intentionally permits a new MEM
        // slug while preserving the Matrix signing/server identity. Count a managed
        // stack as covered when any valid backup matches that stable identity. Slug
        // equality remains only as a legacy fallback when one side lacks identity
        // metadata; it must not override a known identity mismatch.
        var coveredStackCount = stacks.Count(stack =>
            validCatalogRows.Any(catalog => CatalogCoversStack(catalog, stack)));

        var activeRestoreRows = restoreRows
            .Where(x => IsActiveRestore(x.Status))
            .ToArray();

        var attentionRestoreRows = restoreRows
            .Where(RequiresRestoreAttention)
            .OrderByDescending(x => x.LastEventAtUtc ?? x.UpdatedAtUtc)
            .ThenBy(x => x.RestoreSessionId, StringComparer.Ordinal)
            .ToArray();

        var catalogEntryCount = catalogRows.Count;
        var availableCatalogEntryCount = catalogRows.Count(x =>
            string.Equals(x.PayloadState, "available", StringComparison.OrdinalIgnoreCase));
        var validCatalogEntryCount = catalogRows.Count(x =>
            string.Equals(x.IntegrityStatus, "valid", StringComparison.OrdinalIgnoreCase));
        var warningCatalogEntryCount = catalogRows.Count(x =>
            string.Equals(x.IntegrityStatus, "warning", StringComparison.OrdinalIgnoreCase));
        var invalidCatalogEntryCount = catalogRows.Count(x =>
            string.Equals(x.IntegrityStatus, "invalid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.PayloadState, "failed", StringComparison.OrdinalIgnoreCase));

        var state = ResolveRecoveryState(
            stacks.Count,
            coveredStackCount);

        var payloadSizes = catalogRows
            .Where(x => x.PayloadBytes.HasValue)
            .Select(x => x.PayloadBytes!.Value)
            .ToArray();

        return new DashboardRecoverySummary(
            State: state,
            ManagedStackCount: stacks.Count,
            StacksWithValidRecoveryPointCount: coveredStackCount,
            CatalogEntryCount: catalogEntryCount,
            AvailableCatalogEntryCount: availableCatalogEntryCount,
            ValidCatalogEntryCount: validCatalogEntryCount,
            WarningCatalogEntryCount: warningCatalogEntryCount,
            InvalidCatalogEntryCount: invalidCatalogEntryCount,
            LatestCapturedAtUtc: catalogRows
                .Where(x => x.CapturedAtUtc.HasValue)
                .Select(x => x.CapturedAtUtc)
                .OrderByDescending(x => x)
                .Select(ToUtc)
                .FirstOrDefault(),
            TotalPayloadBytes: payloadSizes.Length == 0 ? null : payloadSizes.Sum(),
            ActiveRestoreCount: activeRestoreRows.Length,
            AttentionRestoreCount: attentionRestoreRows.Length,
            PriorityRestoreSessionId: attentionRestoreRows.FirstOrDefault()?.RestoreSessionId);
    }

    private static DashboardHostSummary BuildHost(DashboardHostObservation observation) =>
        new(
            State: observation.State,
            ObservedAtUtc: observation.ObservedAtUtc,
            UnavailableReasonCode: observation.UnavailableReasonCode,
            OperatingSystem: observation.OperatingSystem,
            Architecture: observation.Architecture,
            CpuCount: observation.CpuCount,
            MemoryTotalBytes: observation.MemoryTotalBytes,
            DockerServerVersion: observation.DockerServerVersion,
            ContainerCount: observation.ContainerCount,
            ImageCount: observation.ImageCount,
            Disk: observation.Disk is null
                ? null
                : new DashboardDiskUsage(
                    observation.Disk.UsedBytes,
                    observation.Disk.TotalBytes,
                    observation.Disk.Scope),
            StorageUnavailableReasonCode: observation.StorageUnavailableReasonCode);

    private static DashboardPlatformSummary BuildPlatform(
        DashboardDockerObservation docker,
        IReadOnlyList<DashboardPlatformServiceObservation> observations)
    {
        var services = observations
            .OrderBy(x => ServiceOrder(x.Key))
            .Select(x => new DashboardServiceSummary(
                x.Key,
                x.Requirement,
                x.State,
                x.ObservedAtUtc))
            .ToArray();

        var required = services
            .Where(x => x.Requirement == DashboardStates.Required)
            .ToArray();

        var state = docker.State == DashboardStates.Unavailable
            ? DashboardStates.Unavailable
            : required.Any(x => x.State is DashboardStates.NotDeployed or DashboardStates.Stopped or DashboardStates.Degraded)
                ? DashboardStates.Degraded
                : required.Any(x => x.State == DashboardStates.Unknown)
                    ? DashboardStates.Unknown
                    : required.Any(x => x.State == DashboardStates.VerificationLimited)
                        ? DashboardStates.VerificationLimited
                        : required.All(x => x.State == DashboardStates.Running)
                            ? DashboardStates.Ready
                            : DashboardStates.Unknown;

        return new DashboardPlatformSummary(
            State: state,
            Services: services,
            RequiredServiceCount: required.Length,
            RunningRequiredServiceCount: required.Count(x =>
                x.State is DashboardStates.Running or DashboardStates.VerificationLimited),
            Docker: new DashboardDockerSummary(docker.State, docker.ObservedAtUtc));
    }

    private static DashboardPublicAccessSummary BuildPublicAccess(
        DomainRow? domain,
        CertificateRow? certificate,
        DomainCertificateRenewalProjection? renewal,
        DashboardIngressObservation ingress,
        DateTimeOffset now)
    {
        var certificateSummary = BuildCertificateSummary(certificate, renewal, now);
        var state = domain is null || certificateSummary.State == DashboardStates.Missing
            ? DashboardStates.NotConfigured
            : certificateSummary.State is DashboardStates.Expired or DashboardStates.Expiring or DashboardStates.Staging
                ? DashboardStates.Attention
                : ingress.State is DashboardStates.Unavailable or DashboardStates.Degraded
                    ? DashboardStates.Attention
                    : ingress.State == DashboardStates.Unknown
                        ? DashboardStates.Unknown
                        : (certificateSummary.State == DashboardStates.Valid ||
                           certificateSummary.State == DashboardStates.Renewing) &&
                          ingress.State == DashboardStates.Ready
                            ? DashboardStates.Ready
                            : DashboardStates.Unknown;

        return new DashboardPublicAccessSummary(
            State: state,
            MainDomain: domain?.BaseDomain,
            Certificate: certificateSummary,
            Ingress: new DashboardIngressSummary(ingress.State, ingress.ObservedAtUtc),
            LastPlatformRouteVerificationAtUtc: null);
    }

    private static DashboardCertificateSummary BuildCertificateSummary(
        CertificateRow? certificate,
        DomainCertificateRenewalProjection? renewal,
        DateTimeOffset now)
    {
        if (certificate is null || !certificate.IsActive)
        {
            return new DashboardCertificateSummary(
                DashboardStates.Missing,
                null,
                null);
        }

        if (certificate.IsStaging)
        {
            return new DashboardCertificateSummary(
                DashboardStates.Staging,
                certificate.CommonName,
                ToUtc(certificate.ExpiresAtUtc));
        }

        if (!certificate.ExpiresAtUtc.HasValue)
        {
            return new DashboardCertificateSummary(
                DashboardStates.Unknown,
                certificate.CommonName,
                null,
                renewal?.OperationalStatus,
                ToUtc(renewal?.NextAutomaticAttemptAtUtc));
        }

        var expiresAtUtc = ToUtc(certificate.ExpiresAtUtc);
        var state = ResolveCertificateState(
            expiresAtUtc!.Value,
            renewal?.OperationalStatus,
            now);

        return new DashboardCertificateSummary(
            state,
            certificate.CommonName,
            expiresAtUtc,
            renewal?.OperationalStatus,
            ToUtc(renewal?.NextAutomaticAttemptAtUtc));
    }

    internal static string ResolveCertificateState(
        DateTimeOffset expiresAtUtc,
        string? renewalOperationalStatus,
        DateTimeOffset now)
    {
        if (expiresAtUtc <= now)
        {
            return DashboardStates.Expired;
        }

        if (expiresAtUtc > now.Add(CertificateExpiringWindow))
        {
            return DashboardStates.Valid;
        }

        return IsHealthyRenewalState(renewalOperationalStatus)
            ? DashboardStates.Renewing
            : DashboardStates.Expiring;
    }

    private static bool IsHealthyRenewalState(string? state) =>
        state is
            DomainRenewalOperationalStatuses.Scheduled or
            DomainRenewalOperationalStatuses.Queued or
            DomainRenewalOperationalStatuses.Running or
            DomainRenewalOperationalStatuses.AwaitingActivation;

    private static DashboardOnboarding BuildOnboarding(
        DashboardStacksSummary stacks,
        DashboardCapabilities capabilities)
    {
        if (stacks.Total > 0)
        {
            return new DashboardOnboarding(
                State: "not_needed",
                CompletedStepCodes: ["create_chat_server"],
                NextSteps: []);
        }

        var action = capabilities.CanOperate
            ? new DashboardAction(DashboardActions.CreateChatServer)
            : null;

        return new DashboardOnboarding(
            State: capabilities.CanOperate ? DashboardStates.Available : "blocked",
            CompletedStepCodes: [],
            NextSteps:
            [
                new DashboardOnboardingStep(
                    "create_chat_server",
                    capabilities.CanOperate ? DashboardStates.Available : "blocked",
                    action)
            ]);
    }

    private static DashboardHero BuildHero(
        DashboardPlatformSummary platform,
        DashboardPublicAccessSummary publicAccess,
        DashboardStacksSummary stacks,
        DashboardRecoverySummary recovery,
        DashboardCapabilities capabilities)
    {
        if (recovery.AttentionRestoreCount > 0 &&
            !string.IsNullOrWhiteSpace(recovery.PriorityRestoreSessionId))
        {
            return new DashboardHero(
                DashboardStates.Attention,
                "restore_needs_attention",
                new DashboardAction(
                    DashboardActions.OpenRestoreWorkspace,
                    RestoreSessionId: recovery.PriorityRestoreSessionId));
        }

        if (platform.State is DashboardStates.Unavailable or DashboardStates.Degraded or DashboardStates.Unknown)
        {
            return new DashboardHero(
                platform.State == DashboardStates.Unavailable
                    ? DashboardStates.Unavailable
                    : DashboardStates.Attention,
                platform.State == DashboardStates.Unavailable
                    ? "platform_unavailable"
                    : "platform_needs_attention",
                new DashboardAction(
                    platform.Docker.State == DashboardStates.Unavailable
                        ? DashboardActions.OpenDiagnostics
                        : DashboardActions.OpenServices));
        }

        if (recovery.State is DashboardStates.Attention or DashboardStates.NoRecoveryPoint or DashboardStates.PartialCoverage)
        {
            return new DashboardHero(
                DashboardStates.Attention,
                "recovery_needed",
                new DashboardAction(DashboardActions.OpenBackups));
        }

        if (publicAccess.State is DashboardStates.NotConfigured or DashboardStates.Attention or DashboardStates.Unknown)
        {
            return new DashboardHero(
                DashboardStates.Attention,
                "platform_needs_attention",
                new DashboardAction(DashboardActions.ManageDomains));
        }

        if (stacks.Total == 0)
        {
            return new DashboardHero(
                DashboardStates.Ready,
                "create_first_chat_server",
                capabilities.CanOperate
                    ? new DashboardAction(DashboardActions.CreateChatServer)
                    : null);
        }

        return new DashboardHero(
            DashboardStates.Ready,
            "platform_ready",
            null);
    }

    private static IReadOnlyList<DashboardNotice> BuildNotices(
        DashboardHero hero,
        DashboardPlatformSummary platform,
        DashboardPublicAccessSummary publicAccess,
        DashboardRecoverySummary recovery)
    {
        var notices = new List<DashboardNotice>();

        if (hero.HeadlineCode != "restore_needs_attention" && recovery.AttentionRestoreCount > 0)
        {
            notices.Add(new DashboardNotice(
                "restore-needs-attention",
                "danger",
                "restore_needs_attention",
                string.IsNullOrWhiteSpace(recovery.PriorityRestoreSessionId)
                    ? new DashboardAction(DashboardActions.OpenRestores)
                    : new DashboardAction(
                        DashboardActions.OpenRestoreWorkspace,
                        RestoreSessionId: recovery.PriorityRestoreSessionId)));
        }

        if (hero.HeadlineCode != "recovery_needed" &&
            recovery.State is DashboardStates.NoRecoveryPoint or DashboardStates.PartialCoverage or DashboardStates.Attention)
        {
            notices.Add(new DashboardNotice(
                "recovery-coverage",
                "warning",
                "recovery_needed",
                new DashboardAction(DashboardActions.OpenBackups)));
        }

        var stagingCertificateNeedsExplanation =
            publicAccess.Certificate.State == DashboardStates.Staging;

        if ((hero.HeadlineCode != "platform_needs_attention" || stagingCertificateNeedsExplanation) &&
            publicAccess.State is DashboardStates.NotConfigured or DashboardStates.Attention or DashboardStates.Unknown)
        {
            notices.Add(new DashboardNotice(
                "public-access",
                "warning",
                stagingCertificateNeedsExplanation
                    ? "public_access_staging_certificate"
                    : "public_access_needs_attention",
                new DashboardAction(DashboardActions.ManageDomains)));
        }

        if (hero.HeadlineCode is not "platform_unavailable" and not "platform_needs_attention" &&
            platform.State is DashboardStates.Unavailable or DashboardStates.Degraded or DashboardStates.Unknown)
        {
            notices.Add(new DashboardNotice(
                "platform-services",
                "danger",
                "platform_needs_attention",
                new DashboardAction(DashboardActions.OpenServices)));
        }

        return notices.Take(2).ToArray();
    }

    private static bool CatalogCoversStack(CatalogRow catalog, StackRow stack)
    {
        var stackIdentities = StableMatrixIdentities(
            stack.MatrixServerName,
            stack.MatrixPublicHost,
            HostFromAbsoluteUrl(stack.MatrixPublicBaseUrl));
        var catalogIdentities = StableMatrixIdentities(
            catalog.MatrixServerName,
            catalog.MatrixHost);

        if (stackIdentities.Count > 0 && catalogIdentities.Count > 0)
        {
            return stackIdentities.Overlaps(catalogIdentities);
        }

        return !string.IsNullOrWhiteSpace(catalog.SourceStackSlug) &&
               string.Equals(
                   catalog.SourceStackSlug.Trim(),
                   stack.Slug.Trim(),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> StableMatrixIdentities(params string?[] values)
    {
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var normalized = NormalizeMatrixIdentity(value);
            if (normalized is not null)
            {
                identities.Add(normalized);
            }
        }

        return identities;
    }

    private static string? NormalizeMatrixIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host.TrimEnd('.').ToLowerInvariant();
        }

        return trimmed.TrimEnd('.').ToLowerInvariant();
    }

    private static string? HostFromAbsoluteUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return NormalizeMatrixIdentity(uri.Host);
    }

    private static string ResolveRecoveryState(
        int stackCount,
        int coveredStackCount)
    {
        if (stackCount == 0)
        {
            return DashboardStates.NotApplicable;
        }

        if (coveredStackCount == 0)
        {
            return DashboardStates.NoRecoveryPoint;
        }

        return coveredStackCount == stackCount
            ? DashboardStates.Covered
            : DashboardStates.PartialCoverage;
    }

    private static bool IsActiveRestore(string status) =>
        !IsTerminalRestore(status) &&
        !string.Equals(status, "needs-attention", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresRestoreAttention(RestoreRow row)
    {
        if (IsTerminalRestore(row.Status))
        {
            return false;
        }

        return row.ErrorCount > 0 ||
               string.Equals(row.Status, "needs-attention", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTerminalRestore(string status) =>
        status.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("cancelled", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("abandoned", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("superseded", StringComparison.OrdinalIgnoreCase);

    private static string ToLastVerificationState(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return DashboardStates.Unknown;
        }

        return status.Trim().ToLowerInvariant() switch
        {
            "passed" or "healthy" or "ready" or "created" => DashboardStates.Passed,
            "failed" or "degraded" or "unhealthy" or "error" or "destroyed" => DashboardStates.Failed,
            _ => DashboardStates.Unknown
        };
    }

    private static int ServiceOrder(string key) => key switch
    {
        "postgres" => 0,
        "npm_ingress" => 1,
        "coturn" => 2,
        _ => 99
    };

    private static DateTimeOffset? ToUtc(DateTime? value) =>
        value.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
            : null;

    private sealed record StackRow(
        Guid Id,
        string Slug,
        string? LastVerifiedStatus,
        DateTime? LastVerifiedAtUtc,
        string? MatrixPublicBaseUrl,
        string? ElementPublicBaseUrl,
        string? MatrixServerName,
        string? MatrixPublicHost);

    private sealed record CatalogRow(
        string? SourceStackSlug,
        string? MatrixServerName,
        string? MatrixHost,
        string PayloadState,
        string IntegrityStatus,
        long? PayloadBytes,
        DateTime? CapturedAtUtc);

    private sealed record RestoreRow(
        string RestoreSessionId,
        string Status,
        int WarningCount,
        int ErrorCount,
        DateTime UpdatedAtUtc,
        DateTime? LastEventAtUtc);

    private sealed record DomainRow(
        Guid Id,
        string BaseDomain,
        Guid? ActiveCertificateId);

    private sealed record CertificateRow(
        string CommonName,
        bool IsStaging,
        bool IsActive,
        string Status,
        DateTime? ExpiresAtUtc);
}
