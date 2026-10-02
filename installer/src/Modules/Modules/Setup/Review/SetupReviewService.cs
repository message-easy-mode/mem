using System.Text.Json;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;
using Modules.Setup.Secrets;
using Modules.Setup.Platform.Coturn;
using Shared.Diagnostics;

namespace Modules.Setup.Review;

public sealed class SetupReviewService
{
    private const string PlatformNetworkName = "mem-gateway";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly MemDbContext _db;
    private readonly IInstallationSecretStore _secretStore;
    private readonly NpmAdminCredentialService _npmAdminCredentialService;
    private readonly ILogger<SetupReviewService> _logger;
    private readonly IMemDiagnosticEventWriter? _diagnostics;

    public SetupReviewService(
        MemDbContext db,
        IInstallationSecretStore secretStore,
        NpmAdminCredentialService npmAdminCredentialService,
        ILogger<SetupReviewService> logger,
        IMemDiagnosticEventWriter? diagnostics = null)
    {
        _db = db;
        _secretStore = secretStore;
        _npmAdminCredentialService = npmAdminCredentialService;
        _logger = logger;
        _diagnostics = diagnostics;
    }

    public async Task<SetupReviewResponse> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var installation = await CurrentReviewInstallationAsync(cancellationToken);
        if (installation is null)
        {
            return Empty("No active first-time Setup plan is available for Review.", "SetupAuthorityMissing");
        }

        return await BuildResponseAsync(installation, cancellationToken);
    }

    public async Task<SetupReviewResponse> AcceptAsync(CancellationToken cancellationToken)
    {
        var installation = await CurrentReviewInstallationAsync(cancellationToken);
        if (installation is null)
        {
            return Empty("No active first-time Setup plan is available for Review.", "SetupAuthorityMissing");
        }

        var current = await BuildResponseAsync(installation, cancellationToken);
        if (current.ReviewAccepted)
        {
            return current;
        }

        if (!current.CanAccept)
        {
            return current with
            {
                Message = "Review cannot be accepted until the listed blockers are resolved.",
                ErrorCode = "SetupReviewBlocked"
            };
        }

        var config = DeserializeConfig(installation.ConfigJson);
        var intent = config with { Review = null };
        var planSha256 = SetupReviewPlanFingerprint.Compute(intent);
        var reviewedAtUtc = DateTime.UtcNow;
        var reviewed = intent with
        {
            Review = new ReviewSetupConfig(
                AcceptedAtUtc: reviewedAtUtc,
                PlanSha256: planSha256)
        };
        var frozenJson = JsonSerializer.Serialize(reviewed, JsonOptions);

        installation.ConfigJson = frozenJson;
        installation.FrozenConfigJson = frozenJson;
        installation.Status = InstallationStatuses.Ready;
        installation.LastError = null;
        installation.UpdatedAtUtc = reviewedAtUtc;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Accepted first-time Setup review. InstallationId={InstallationId} PlanSha256={PlanSha256} ExternalMutation={ExternalMutation}",
            installation.Id,
            planSha256,
            "none");

        await _diagnostics.TryWriteWorkflowEventAsync(new MemDiagnosticWriteRequest(
            Severity: MemDiagnosticSeverities.Information,
            EventCode: "setup.review.accepted",
            Source: "api.setup-review",
            Feature: "setup",
            Stage: "review",
            Message: "The first-time Setup plan was reviewed and frozen for installation.",
            CreateIncident: false,
            Resource: new MemDiagnosticResource(
                Kind: "installation",
                Id: installation.Id.ToString("D"),
                DisplayName: "MEM installation",
                WorkspacePath: "/setup/review"),
            Observed: new Dictionary<string, string?>
            {
                ["status"] = InstallationStatuses.Ready,
                ["reviewAccepted"] = "true"
            },
            Details: new Dictionary<string, string?>
            {
                ["planSha256"] = planSha256,
                ["externalMutation"] = "none"
            },
            Retryable: false));

        return await BuildResponseAsync(installation, cancellationToken);
    }

    private async Task<SetupReviewResponse> BuildResponseAsync(
        Infrastructure.Data.Entities.InstallationEntity installation,
        CancellationToken cancellationToken)
    {
        var config = DeserializeConfig(installation.ConfigJson);
        var blockers = new List<string>();

        var preflight = config.Preflight;
        var preflightReady = preflight is not null &&
            preflight.BlockingIssueCount == 0 &&
            (string.Equals(preflight.RunStatus, "Succeeded", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(preflight.RunStatus, "SucceededWithWarnings", StringComparison.OrdinalIgnoreCase));

        if (!preflightReady)
        {
            blockers.Add(preflight is null
                ? "Run Server Checks before accepting Review."
                : "Resolve blocking Server Check findings before accepting Review.");
        }

        var publicAccess = config.PublicAccess;
        var preparation = publicAccess.Preparation;
        var domainValidated = preparation is not null &&
            string.Equals(preparation.Status, "Validated", StringComparison.OrdinalIgnoreCase) &&
            preparation.ProviderAccessConfirmed &&
            !string.IsNullOrWhiteSpace(publicAccess.Zone) &&
            !string.IsNullOrWhiteSpace(publicAccess.Domain) &&
            !string.IsNullOrWhiteSpace(publicAccess.AcmeEmail);

        if (!domainValidated)
        {
            blockers.Add("Validate the Domain plan before accepting Review.");
        }

        var dnsCredentialStored = await _secretStore.ExistsAsync(
            installation.Id,
            InstallationSecretNames.DnsCategory,
            InstallationSecretNames.DesecProviderToken,
            cancellationToken);
        if (!dnsCredentialStored)
        {
            blockers.Add("The protected DNS credential is unavailable. Revalidate the Domain plan.");
        }

        var postgresCredentialStored = await _secretStore.ExistsAsync(
            installation.Id,
            InstallationSecretNames.PlatformCategory,
            InstallationSecretNames.PostgresPassword,
            cancellationToken);
        if (!postgresCredentialStored)
        {
            blockers.Add("The protected Postgres credential is unavailable. Revalidate the Domain plan.");
        }

        var npmAdminCredential = await _npmAdminCredentialService.GetForInstallationAsync(
            installation.Id,
            cancellationToken);
        if (!npmAdminCredential.CredentialStored)
        {
            blockers.Add("Choose and protect the Nginx Proxy Manager administrator credential before accepting Review.");
        }

        if (!config.Platform.Postgres.Enabled)
        {
            blockers.Add("Postgres must be enabled for the base MEM platform.");
        }

        if (!config.Platform.Ingress.Enabled ||
            !string.Equals(config.Platform.Ingress.Provider, "Npm", StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add("NPM ingress must be enabled for the base MEM platform.");
        }

        var frozen = !string.IsNullOrWhiteSpace(installation.FrozenConfigJson);
        var reviewAccepted = installation.Status == InstallationStatuses.Ready &&
            frozen &&
            string.Equals(installation.ConfigJson, installation.FrozenConfigJson, StringComparison.Ordinal) &&
            SetupReviewPlanFingerprint.Matches(config);

        var supportTools = new List<string>();
        if (config.SupportTools.Portainer.Enabled) supportTools.Add("Portainer");
        if (config.SupportTools.Seq.Enabled) supportTools.Add("Seq");
        if (config.SupportTools.PgAdmin.Enabled) supportTools.Add("pgAdmin");

        var plannedActions = new List<string>
        {
            $"Create or verify Docker network '{PlatformNetworkName}' and attach the containerized Control Plane for managed-service communication.",
            $"Create or reuse Postgres volume '{config.Platform.Postgres.VolumeName}'.",
            $"Start Postgres container '{config.Platform.Postgres.ContainerName}'.",
            $"Start or reuse NPM ingress container '{config.Platform.Ingress.ContainerName}'.",
            $"Create the DNS-01 challenge for '{publicAccess.Domain}' only after installation starts.",
            $"Request a Let's Encrypt {(publicAccess.UseStaging ? "staging" : "production")} wildcard certificate.",
            "Store and validate the issued certificate, then register it as the main platform certificate.",
            "Import the platform certificate into NPM.",
            $"Install or reuse one shared platform TURN service '{PlatformCoturnSetupDefaults.ContainerName}' for all MEM-managed Matrix stacks.",
            $"Publish {PlatformCoturnSetupDefaults.TurnPort}/tcp, {PlatformCoturnSetupDefaults.TurnPort}/udp, and UDP relay range {PlatformCoturnSetupDefaults.RelayMinPort}-{PlatformCoturnSetupDefaults.RelayMaxPort} directly from the shared Coturn runtime.",
            "Generate and protect one platform TURN shared secret and protected Coturn configuration without returning either to the browser.",
            "Run final platform verification before Setup handoff."
        };

        if (supportTools.Count > 0)
        {
            plannedActions.Insert(
                plannedActions.Count - 1,
                $"Start or verify selected support tools: {string.Join(", ", supportTools)}.");
        }

        var willNotChange = new[]
        {
            "The private MEM Control Plane exposure.",
            "Unrelated Docker containers, volumes, networks, or applications.",
            "DNS records before the reviewed installation is explicitly started.",
            "Protected DNS, Postgres, NPM administrator, or TURN shared-secret values in browser-visible plan JSON.",
            "One shared platform Coturn runtime serves MEM-managed Matrix stacks; installation does not create a separate Coturn container for each stack.",
            "TURN traffic through NPM; Coturn publishes its listener and relay ports directly."
        };

        return new SetupReviewResponse(
            InstallationId: installation.Id,
            InstallationStatus: installation.Status,
            CanAccept: !reviewAccepted && blockers.Count == 0 && InstallationStatuses.IsPlanning(installation.Status),
            ReviewAccepted: reviewAccepted,
            Message: reviewAccepted
                ? "Review accepted. The exact server-owned plan is frozen and ready for installation."
                : blockers.Count == 0
                    ? "Review the server-owned plan. Accepting Review freezes this exact intent without changing external systems."
                    : "Review is not ready yet. Resolve the listed blockers first.",
            ErrorCode: blockers.Count == 0 ? null : "SetupReviewBlocked",
            PlanSha256: config.Review?.PlanSha256,
            ReviewedAtUtc: config.Review?.AcceptedAtUtc,
            Preflight: new SetupReviewPreflightSummary(
                Available: preflight is not null,
                Ready: preflightReady,
                RunId: preflight?.RunId,
                CompletedAtUtc: preflight?.CompletedAtUtc,
                Passed: preflight?.Passed ?? 0,
                Warnings: preflight?.Warnings ?? 0,
                Failed: preflight?.Failed ?? 0,
                Skipped: preflight?.Skipped ?? 0,
                Unavailable: preflight?.Unavailable ?? 0,
                Unknown: preflight?.Unknown ?? 0,
                BlockingIssueCount: preflight?.BlockingIssueCount ?? 0),
            Domain: new SetupReviewDomainSummary(
                Validated: domainValidated,
                BaseDomain: publicAccess.Zone,
                WildcardCertificate: publicAccess.Domain,
                DnsProvider: publicAccess.DnsProvider,
                AcmeEmail: publicAccess.AcmeEmail,
                CertificateEnvironment: publicAccess.UseStaging ? "Let's Encrypt staging" : "Let's Encrypt production",
                ProviderAccessConfirmed: preparation?.ProviderAccessConfirmed ?? false,
                ProviderCredentialStored: dnsCredentialStored),
            NpmAdministrator: new SetupReviewNpmAdministratorSummary(
                AdministratorEmail: npmAdminCredential.AdministratorEmail,
                CredentialStored: npmAdminCredential.CredentialStored,
                VerifiedAtUtc: npmAdminCredential.VerifiedAtUtc),
            Platform: new SetupReviewPlatformSummary(
                NetworkName: PlatformNetworkName,
                PostgresContainerName: config.Platform.Postgres.ContainerName,
                PostgresVolumeName: config.Platform.Postgres.VolumeName,
                NpmContainerName: config.Platform.Ingress.ContainerName,
                NpmHttpPort: config.Platform.Ingress.HttpPort,
                NpmHttpsPort: config.Platform.Ingress.HttpsPort,
                NpmAdminPort: config.Platform.Ingress.AdminPort,
                CoturnContainerName: PlatformCoturnSetupDefaults.ContainerName,
                CoturnPublicHost: PlatformCoturnSetupDefaults.PublicHost(publicAccess.Zone),
                CoturnTurnPort: PlatformCoturnSetupDefaults.TurnPort,
                CoturnRelayPortRange: $"{PlatformCoturnSetupDefaults.RelayMinPort}-{PlatformCoturnSetupDefaults.RelayMaxPort}/udp",
                EnabledSupportTools: supportTools),
            PlannedActions: plannedActions,
            WillNotChange: willNotChange,
            Blockers: blockers);
    }

    private async Task<Infrastructure.Data.Entities.InstallationEntity?> CurrentReviewInstallationAsync(
        CancellationToken cancellationToken) =>
        await _db.Installations
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(
                x => x.Status == InstallationStatuses.Draft ||
                     x.Status == InstallationStatuses.Ready,
                cancellationToken);

    private static InstallPlan DeserializeConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return InstallPlanFactory.CreateDefault();
        }

        return JsonSerializer.Deserialize<InstallPlan>(json, JsonOptions)
            ?? InstallPlanFactory.CreateDefault();
    }

    private static SetupReviewResponse Empty(string message, string errorCode) =>
        new(
            InstallationId: null,
            InstallationStatus: "Unavailable",
            CanAccept: false,
            ReviewAccepted: false,
            Message: message,
            ErrorCode: errorCode,
            PlanSha256: null,
            ReviewedAtUtc: null,
            Preflight: new SetupReviewPreflightSummary(false, false, null, null, 0, 0, 0, 0, 0, 0, 0),
            Domain: new SetupReviewDomainSummary(false, string.Empty, string.Empty, "desec", string.Empty, string.Empty, false, false),
            NpmAdministrator: new SetupReviewNpmAdministratorSummary(null, false, null),
            Platform: new SetupReviewPlatformSummary(
                PlatformNetworkName,
                "mem-postgres",
                "mem_postgres_data",
                "mem-npm",
                80,
                443,
                81,
                PlatformCoturnSetupDefaults.ContainerName,
                string.Empty,
                PlatformCoturnSetupDefaults.TurnPort,
                $"{PlatformCoturnSetupDefaults.RelayMinPort}-{PlatformCoturnSetupDefaults.RelayMaxPort}/udp",
                []),
            PlannedActions: [],
            WillNotChange: [],
            Blockers: [message]);
}
