using HostAgent.Matrix.Federation;
using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Manifests;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Stacks.Turn;

public interface IRuntimeStackTurnInspectionService
{
    Task<RuntimeStackTurnInspectionResponse?> InspectAsync(
        string slugOrId,
        CancellationToken ct);
}

public sealed class RuntimeStackTurnInspectionService
    : IRuntimeStackTurnInspectionService
{
    private readonly RuntimeStackManifestStore _manifestStore;
    private readonly SynapseTurnConfigReader _configReader;
    private readonly IRuntimeStackFederationContainerInspector _containerInspector;
    private readonly CoturnRuntimeService _coturnRuntime;
    private readonly CoturnRuntimeFileStore _coturnFiles;
    private readonly ILogger<RuntimeStackTurnInspectionService> _logger;

    public RuntimeStackTurnInspectionService(
        RuntimeStackManifestStore manifestStore,
        SynapseTurnConfigReader configReader,
        IRuntimeStackFederationContainerInspector containerInspector,
        CoturnRuntimeService coturnRuntime,
        CoturnRuntimeFileStore coturnFiles,
        ILogger<RuntimeStackTurnInspectionService> logger)
    {
        _manifestStore = manifestStore;
        _configReader = configReader;
        _containerInspector = containerInspector;
        _coturnRuntime = coturnRuntime;
        _coturnFiles = coturnFiles;
        _logger = logger;
    }

    public async Task<RuntimeStackTurnInspectionResponse?> InspectAsync(
        string slugOrId,
        CancellationToken ct)
    {
        var manifest = await _manifestStore.FindAsync(slugOrId, ct);
        if (manifest is null)
        {
            return null;
        }

        SynapseTurnConfigReadResult? live = null;
        var configProblemCode = default(string);
        var configProblemDetail = default(string);
        var configPath = manifest.Matrix.ConfigPath;
        if (string.IsNullOrWhiteSpace(configPath))
        {
            configProblemCode = "turn_manifest_config_path_missing";
            configProblemDetail = "The Runtime Stack manifest does not contain a Matrix homeserver configuration path.";
        }
        else if (!File.Exists(configPath))
        {
            configProblemCode = "turn_config_missing";
            configProblemDetail = "The Matrix homeserver configuration file was not found at the manifest-owned location.";
        }
        else
        {
            try
            {
                live = await _configReader.ReadAsync(configPath, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex,
                    "TURN inspection could not read the Matrix configuration. StackId={StackId}",
                    manifest.StackId);
                configProblemCode = "turn_config_unavailable";
                configProblemDetail = "The Matrix homeserver configuration could not be read.";
            }
        }

        FederationContainerObservation container;
        try
        {
            container = await _containerInspector.InspectAsync(manifest.Matrix, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "TURN inspection could not inspect the Matrix container. StackId={StackId}",
                manifest.StackId);
            container = new FederationContainerObservation(
                Exists: false,
                Running: false,
                IdentityMatches: false,
                ExpectedNetworkAttached: null,
                DirectHostPortExposed: false,
                ContainerId: null,
                ContainerName: null,
                Image: null,
                ProblemCode: "turn_matrix_container_unavailable",
                Detail: "The Matrix container could not be inspected.");
        }

        CoturnRuntimeResponse? platform = null;
        CoturnRuntimeFiles? platformFiles = null;
        try
        {
            platform = await _coturnRuntime.InspectAsync(ct);
            platformFiles = await _coturnFiles.ReadAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "TURN inspection could not inspect the platform Coturn service. StackId={StackId}",
                manifest.StackId);
        }

        var metadata = ReadMetadata(manifest.Matrix.RuntimeMetadata);
        var classification = RuntimeStackTurnStateClassifier.Classify(
            new RuntimeStackTurnClassificationInput(
                Live: live ?? (configProblemCode is null
                    ? null
                    : new SynapseTurnConfigReadResult(
                        Supported: false,
                        AnyTurnSettings: false,
                        MemManagedMarkerPresent: false,
                        TurnUris: [],
                        CredentialMechanism: "unknown",
                        SharedSecretPresent: false,
                        SharedSecretValue: null,
                        SharedSecretFingerprint: null,
                        SharedSecretPath: null,
                        UserLifetime: null,
                        AllowGuests: null,
                        CommentPublicHost: null,
                        CommentRealm: null,
                        FileSha256: "unavailable",
                        ProblemCode: configProblemCode,
                        Detail: configProblemDetail)),
                Metadata: metadata,
                MatrixContainerAvailable: container.Exists,
                MatrixContainerRunning: container.Running,
                MatrixContainerIdentityMatches: container.IdentityMatches,
                PlatformTurnUris: platform?.TurnUris ?? [],
                PlatformSharedSecret: platformFiles?.SecretValue,
                PlatformSecretPresent: platformFiles?.SecretPresent == true));

        var diagnostics = classification.Diagnostics.ToList();
        if (platform is null)
        {
            diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                "turn_platform_inspection",
                RuntimeStackTurnDiagnosticStatuses.Unavailable,
                "The platform Coturn service could not be inspected."));
        }
        else
        {
            diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                "turn_platform_readiness",
                string.Equals(platform.Readiness, "ready", StringComparison.OrdinalIgnoreCase)
                    ? RuntimeStackTurnDiagnosticStatuses.Passed
                    : RuntimeStackTurnDiagnosticStatuses.Warning,
                string.Equals(platform.Readiness, "ready", StringComparison.OrdinalIgnoreCase)
                    ? "The platform Coturn service is structurally ready."
                    : "The platform Coturn service is not structurally ready."));
        }

        return new RuntimeStackTurnInspectionResponse(
            Source: "control-plane",
            Status: classification.State switch
            {
                RuntimeStackTurnStates.Connected or RuntimeStackTurnStates.NotConnected => "ok",
                RuntimeStackTurnStates.External or RuntimeStackTurnStates.Drift => "attention",
                _ => "unavailable"
            },
            RuntimeStackId: manifest.StackId,
            Slug: manifest.Slug,
            InspectedAtUtc: DateTimeOffset.UtcNow,
            State: classification.State,
            Management: classification.Management,
            LiveConfiguration: live is null
                ? null
                : new RuntimeStackTurnLiveConfigurationResponse(
                    Supported: live.Supported,
                    AnyTurnSettings: live.AnyTurnSettings,
                    MemManagedMarkerPresent: live.MemManagedMarkerPresent,
                    TurnUris: live.TurnUris,
                    CredentialMechanism: live.CredentialMechanism,
                    SharedSecretPresent: live.SharedSecretPresent,
                    SharedSecretMatchesPlatform: classification.SharedSecretMatchesPlatform,
                    UserLifetime: live.UserLifetime,
                    AllowGuests: live.AllowGuests,
                    PublicHost: live.CommentPublicHost ?? TryGetTurnHost(live.TurnUris),
                    Realm: live.CommentRealm,
                    FileSha256: live.FileSha256,
                    ProblemCode: live.ProblemCode,
                    Detail: live.Detail),
            PersistedMetadata: new RuntimeStackTurnPersistedMetadataResponse(
                Recorded: metadata.Recorded,
                Configured: metadata.Configured,
                TurnUris: metadata.TurnUris,
                PublicHost: metadata.PublicHost,
                Realm: metadata.Realm,
                ConfigurationSource: metadata.ConfigurationSource,
                RelayPortsPublished: metadata.RelayPortsPublished,
                SharedSecretPresent: metadata.SharedSecretPresent,
                UserLifetime: metadata.UserLifetime,
                AllowGuests: metadata.AllowGuests,
                MatchesLiveConfiguration: classification.LiveUrisMatchMetadata),
            Platform: platform is null
                ? null
                : new RuntimeStackTurnPlatformResponse(
                    Status: platform.Status,
                    Readiness: platform.Readiness,
                    Running: platform.Running,
                    OwnershipVerified: platform.OwnershipVerified,
                    ImageApproved: platform.ImageApproved,
                    PublicHost: platform.PublicHost,
                    TurnUris: platform.TurnUris,
                    SecretPresent: platform.SecretPresent,
                    RelayPortsPublished: platform.RelayPortsPublished,
                    SecurityPolicyApplied: platform.SecurityPolicyApplied,
                    Detail: platform.Detail),
            MatrixRuntime: new RuntimeStackTurnMatrixRuntimeResponse(
                Exists: container.Exists,
                Running: container.Running,
                IdentityMatches: container.IdentityMatches,
                ProblemCode: !container.Exists || !container.IdentityMatches
                    ? "turn_matrix_runtime_unavailable"
                    : null,
                Detail: !container.Exists || !container.IdentityMatches
                    ? container.Detail
                    : null),
            Diagnostics: diagnostics,
            Warnings: classification.Warnings,
            Detail: classification.Detail);
    }

    private static RuntimeStackTurnMetadataObservation ReadMetadata(
        IReadOnlyDictionary<string, string?> metadata)
    {
        var recordedKeys = new[]
        {
            "turnConfigured",
            "turnPublicHost",
            "turnRealm",
            "turnUris",
            "turnRelayPortsPublished",
            "turnSharedSecretPresent",
            "turnUserLifetime",
            "turnAllowGuests",
            "turnConfigurationSource",
            "turnManagement"
        };

        return new RuntimeStackTurnMetadataObservation(
            Recorded: recordedKeys.Any(metadata.ContainsKey),
            Configured: ReadBool(metadata, "turnConfigured"),
            TurnUris: ReadUris(metadata, "turnUris"),
            PublicHost: ReadValue(metadata, "turnPublicHost"),
            Realm: ReadValue(metadata, "turnRealm"),
            ConfigurationSource: ReadValue(metadata, "turnConfigurationSource"),
            RelayPortsPublished: ReadBool(metadata, "turnRelayPortsPublished"),
            SharedSecretPresent: ReadBool(metadata, "turnSharedSecretPresent"),
            UserLifetime: ReadValue(metadata, "turnUserLifetime"),
            AllowGuests: ReadBool(metadata, "turnAllowGuests"),
            Management: ReadValue(metadata, "turnManagement"));
    }

    private static string? ReadValue(
        IReadOnlyDictionary<string, string?> metadata,
        string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static bool? ReadBool(
        IReadOnlyDictionary<string, string?> metadata,
        string key) =>
        metadata.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : null;

    private static IReadOnlyList<string> ReadUris(
        IReadOnlyDictionary<string, string?> metadata,
        string key) =>
        ReadValue(metadata, key)?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray() ?? [];

    private static string? TryGetTurnHost(IReadOnlyList<string> uris)
    {
        foreach (var value in uris)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (string.Equals(uri.Scheme, "turn", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, "turns", StringComparison.OrdinalIgnoreCase))
            {
                return uri.Host;
            }
        }

        return null;
    }
}
