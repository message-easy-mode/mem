namespace HostAgent.Runtime.Stacks.Turn;

public static class RuntimeStackTurnStateClassifier
{
    public static RuntimeStackTurnClassification Classify(
        RuntimeStackTurnClassificationInput input)
    {
        var diagnostics = new List<RuntimeStackTurnDiagnosticResponse>();
        var warnings = new List<string>();

        if (!input.MatrixContainerAvailable || !input.MatrixContainerIdentityMatches)
        {
            diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                "turn_matrix_runtime",
                RuntimeStackTurnDiagnosticStatuses.Unavailable,
                "The Matrix runtime recorded by the stack manifest could not be verified."));
            return Unknown(diagnostics, warnings, "The active Matrix runtime could not be inspected safely.");
        }

        if (!input.MatrixContainerRunning)
        {
            diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                "turn_matrix_running",
                RuntimeStackTurnDiagnosticStatuses.Unavailable,
                "The Matrix container is not running, so its effective TURN state is unavailable."));
            return Unknown(diagnostics, warnings, "The Matrix container is not running.");
        }

        if (input.Live is null)
        {
            diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                "turn_config_read",
                RuntimeStackTurnDiagnosticStatuses.Unavailable,
                "The effective Synapse TURN configuration could not be read."));
            return Unknown(diagnostics, warnings, "The live Synapse configuration could not be inspected.");
        }

        if (!input.Live.Supported)
        {
            diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                input.Live.ProblemCode ?? "turn_config_unsupported",
                RuntimeStackTurnDiagnosticStatuses.Unavailable,
                input.Live.Detail ?? "The Synapse TURN configuration uses an unsupported representation."));
            return Unknown(diagnostics, warnings, "MEM will not guess the TURN state from an unsupported Synapse configuration.");
        }

        diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
            "turn_config_read",
            RuntimeStackTurnDiagnosticStatuses.Passed,
            "The effective Synapse configuration was read successfully."));

        bool? metadataUrisMatch = input.Metadata.Recorded
            ? SetsEqual(input.Live.TurnUris, input.Metadata.TurnUris)
            : null;
        bool? platformUrisMatch = input.PlatformTurnUris.Count > 0
            ? SetsEqual(input.Live.TurnUris, input.PlatformTurnUris)
            : null;
        bool? secretMatchesPlatform = input.Live.CredentialMechanism == "inline-shared-secret" &&
                                    input.PlatformSecretPresent &&
                                    !string.IsNullOrWhiteSpace(input.PlatformSharedSecret)
            ? string.Equals(input.Live.SharedSecretValue, input.PlatformSharedSecret, StringComparison.Ordinal)
            : null;

        if (!input.Live.AnyTurnSettings)
        {
            diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                "turn_live_settings",
                RuntimeStackTurnDiagnosticStatuses.Passed,
                "Synapse contains no TURN settings."));

            if (input.Metadata.Configured == true)
            {
                diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                    "turn_metadata_drift",
                    RuntimeStackTurnDiagnosticStatuses.Failed,
                    "Persisted runtime metadata says TURN is configured, but the live Synapse configuration does not."));
                warnings.Add("Persisted TURN metadata does not match the live Synapse configuration.");
                return new RuntimeStackTurnClassification(
                    RuntimeStackTurnStates.Drift,
                    RuntimeStackTurnManagementKinds.MemManaged,
                    metadataUrisMatch,
                    platformUrisMatch,
                    secretMatchesPlatform,
                    diagnostics,
                    warnings,
                    "TURN configuration drift was detected.");
            }

            return new RuntimeStackTurnClassification(
                RuntimeStackTurnStates.NotConnected,
                RuntimeStackTurnManagementKinds.None,
                metadataUrisMatch,
                platformUrisMatch,
                secretMatchesPlatform,
                diagnostics,
                warnings,
                "This stack is not configured to use a TURN service.");
        }

        if (input.Live.TurnUris.Count == 0 || !input.Live.SharedSecretPresent)
        {
            diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                "turn_live_settings_incomplete",
                RuntimeStackTurnDiagnosticStatuses.Failed,
                input.Live.TurnUris.Count == 0
                    ? "TURN-related settings exist, but no TURN URIs are configured."
                    : "TURN URIs exist, but no supported shared-secret mechanism is configured."));

            if (input.Metadata.Recorded || input.Live.MemManagedMarkerPresent)
            {
                warnings.Add("The MEM-recorded TURN configuration is incomplete.");
                return new RuntimeStackTurnClassification(
                    RuntimeStackTurnStates.Drift,
                    RuntimeStackTurnManagementKinds.MemManaged,
                    metadataUrisMatch,
                    platformUrisMatch,
                    secretMatchesPlatform,
                    diagnostics,
                    warnings,
                    "The recorded MEM TURN configuration is incomplete.");
            }

            return new RuntimeStackTurnClassification(
                RuntimeStackTurnStates.External,
                RuntimeStackTurnManagementKinds.ExternalObserved,
                metadataUrisMatch,
                platformUrisMatch,
                secretMatchesPlatform,
                diagnostics,
                warnings,
                "Synapse contains TURN settings that are not represented as a complete MEM-managed association.");
        }

        diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
            "turn_live_settings",
            RuntimeStackTurnDiagnosticStatuses.Passed,
            $"Synapse contains {input.Live.TurnUris.Count} TURN URI value(s) and a supported credential mechanism."));

        var metadataClaimsExternal = input.Metadata.Configured == true &&
                                     string.Equals(
                                         input.Metadata.Management,
                                         RuntimeStackTurnManagementKinds.ExternalObserved,
                                         StringComparison.OrdinalIgnoreCase);

        if (secretMatchesPlatform == false)
        {
            diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                "turn_platform_secret_match",
                metadataClaimsExternal
                    ? RuntimeStackTurnDiagnosticStatuses.Warning
                    : RuntimeStackTurnDiagnosticStatuses.Failed,
                metadataClaimsExternal
                    ? "The preserved external TURN credential does not match the current platform Coturn secret, as expected for an external association."
                    : "The live Synapse shared secret does not match the protected platform Coturn secret."));
        }
        else if (secretMatchesPlatform == true)
        {
            diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                "turn_platform_secret_match",
                RuntimeStackTurnDiagnosticStatuses.Passed,
                "The live Synapse shared secret matches the protected platform Coturn secret."));
        }
        else
        {
            diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                "turn_platform_secret_match",
                RuntimeStackTurnDiagnosticStatuses.Warning,
                "The platform shared secret could not be compared with the live Synapse credential mechanism."));
        }

        var memOwnedLiveConfiguration = input.Live.MemManagedMarkerPresent ||
                                        (platformUrisMatch == true && secretMatchesPlatform == true);

        if (input.Metadata.Configured == true)
        {
            var metadataSource = input.Metadata.ConfigurationSource?.Trim();
            var metadataClaimsPlatform = string.Equals(
                metadataSource,
                "platform-coturn",
                StringComparison.OrdinalIgnoreCase);
            if (metadataUrisMatch == false ||
                (metadataClaimsPlatform &&
                 (secretMatchesPlatform == false || platformUrisMatch == false)))
            {
                diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                    "turn_metadata_drift",
                    RuntimeStackTurnDiagnosticStatuses.Failed,
                    "Persisted TURN metadata does not match the live Synapse or claimed platform Coturn configuration."));
                warnings.Add("Persisted TURN metadata does not match the live configuration.");
                return new RuntimeStackTurnClassification(
                    RuntimeStackTurnStates.Drift,
                    metadataClaimsExternal
                        ? RuntimeStackTurnManagementKinds.ExternalObserved
                        : RuntimeStackTurnManagementKinds.MemManaged,
                    metadataUrisMatch,
                    platformUrisMatch,
                    secretMatchesPlatform,
                    diagnostics,
                    warnings,
                    "TURN configuration drift was detected.");
            }

            if (memOwnedLiveConfiguration)
            {
                diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                    "turn_metadata_match",
                    RuntimeStackTurnDiagnosticStatuses.Passed,
                    metadataClaimsExternal
                        ? "The preserved TURN settings now exactly match the current MEM platform and can be adopted without rewriting Synapse."
                        : "Persisted runtime metadata matches the live MEM-managed TURN configuration."));
                return new RuntimeStackTurnClassification(
                    RuntimeStackTurnStates.Connected,
                    RuntimeStackTurnManagementKinds.MemManaged,
                    metadataUrisMatch,
                    platformUrisMatch,
                    secretMatchesPlatform,
                    diagnostics,
                    warnings,
                    metadataClaimsExternal
                        ? "The preserved TURN configuration exactly matches the current MEM platform and is ready for metadata-only adoption."
                        : "This stack is connected to MEM-managed TURN.");
            }

            diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                "turn_external_observed",
                RuntimeStackTurnDiagnosticStatuses.Warning,
                metadataClaimsExternal
                    ? "The live TURN settings match the explicitly preserved external stack metadata."
                    : "The live TURN settings match recorded stack metadata but cannot be proven to belong to the current MEM-managed Coturn service."));
            return new RuntimeStackTurnClassification(
                RuntimeStackTurnStates.External,
                RuntimeStackTurnManagementKinds.ExternalObserved,
                metadataUrisMatch,
                platformUrisMatch,
                secretMatchesPlatform,
                diagnostics,
                warnings,
                "Synapse is configured for a TURN service that MEM does not currently manage.");
        }

        if (input.Metadata.Configured == false || input.Metadata.Recorded || memOwnedLiveConfiguration)
        {
            diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
                "turn_metadata_drift",
                RuntimeStackTurnDiagnosticStatuses.Failed,
                input.Metadata.Recorded
                    ? "Persisted runtime metadata says TURN is not configured, but live TURN settings exist."
                    : "Live settings look MEM-managed, but no durable stack TURN association is recorded."));
            warnings.Add("Live TURN settings and persisted runtime metadata disagree.");
            return new RuntimeStackTurnClassification(
                RuntimeStackTurnStates.Drift,
                RuntimeStackTurnManagementKinds.MemManaged,
                metadataUrisMatch,
                platformUrisMatch,
                secretMatchesPlatform,
                diagnostics,
                warnings,
                "TURN configuration drift was detected.");
        }

        diagnostics.Add(new RuntimeStackTurnDiagnosticResponse(
            "turn_external_observed",
            RuntimeStackTurnDiagnosticStatuses.Warning,
            "Synapse contains TURN settings that are not represented by MEM-managed runtime metadata."));
        return new RuntimeStackTurnClassification(
            RuntimeStackTurnStates.External,
            RuntimeStackTurnManagementKinds.ExternalObserved,
            metadataUrisMatch,
            platformUrisMatch,
            secretMatchesPlatform,
            diagnostics,
            warnings,
            "Synapse is configured for an external or otherwise unmanaged TURN service.");
    }

    private static RuntimeStackTurnClassification Unknown(
        IReadOnlyList<RuntimeStackTurnDiagnosticResponse> diagnostics,
        IReadOnlyList<string> warnings,
        string detail) =>
        new(
            RuntimeStackTurnStates.Unknown,
            RuntimeStackTurnManagementKinds.Unknown,
            LiveUrisMatchMetadata: null,
            LiveUrisMatchPlatform: null,
            SharedSecretMatchesPlatform: null,
            diagnostics,
            warnings,
            detail);

    private static bool SetsEqual(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) =>
        left.Select(NormalizeUri).OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(
            right.Select(NormalizeUri).OrderBy(value => value, StringComparer.Ordinal),
            StringComparer.Ordinal);

    private static string NormalizeUri(string value) => value.Trim().ToLowerInvariant();
}
