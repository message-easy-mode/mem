using System.Security.Cryptography;
using System.Text;

namespace HostAgent.Matrix.Federation;

public sealed record CanonicalFederationPolicyRequest(
    string Mode,
    IReadOnlyList<string> Allowlist);

public sealed class FederationPolicyValidationException : InvalidOperationException
{
    public FederationPolicyValidationException(string detail)
        : base(detail)
    {
    }
}

public sealed class FederationReviewBlockedException : InvalidOperationException
{
    public FederationReviewBlockedException(string code, string detail)
        : base(detail)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class FederationPolicyRequestValidator
{
    private readonly FederationDomainValidator _domainValidator;

    public FederationPolicyRequestValidator(FederationDomainValidator domainValidator)
    {
        _domainValidator = domainValidator;
    }

    public CanonicalFederationPolicyRequest Validate(
        RuntimeStackFederationPolicyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var mode = (request.Mode ?? string.Empty).Trim().ToLowerInvariant();
        var submittedAllowlist = request.Allowlist ?? [];

        if (string.Equals(mode, FederationModes.Public, StringComparison.Ordinal))
        {
            if (submittedAllowlist.Count > 0)
            {
                throw new FederationPolicyValidationException(
                    "Public federation requires an empty allowlist.");
            }

            return new CanonicalFederationPolicyRequest(
                FederationModes.Public,
                []);
        }

        if (string.Equals(mode, FederationModes.Restricted, StringComparison.Ordinal))
        {
            var validation = _domainValidator.ValidateMany(
                submittedAllowlist,
                requireAtLeastOne: true);

            if (!validation.Valid)
            {
                throw new FederationPolicyValidationException(
                    validation.Detail ?? "Restricted federation requires valid exact homeserver domains.");
            }

            return new CanonicalFederationPolicyRequest(
                FederationModes.Restricted,
                validation.CanonicalDomains);
        }

        if (string.Equals(mode, FederationModes.LocalOnly, StringComparison.Ordinal))
        {
            if (submittedAllowlist.Count > 0)
            {
                throw new FederationPolicyValidationException(
                    "Local-only federation requires an empty allowlist.");
            }

            return new CanonicalFederationPolicyRequest(
                FederationModes.LocalOnly,
                []);
        }

        throw new FederationPolicyValidationException(
            "Federation mode must be 'public', 'restricted', or 'local_only'.");
    }
}

public static class FederationReviewHash
{
    public static string Compute(
        string stateFingerprint,
        CanonicalFederationPolicyRequest request)
    {
        if (string.IsNullOrWhiteSpace(stateFingerprint))
        {
            throw new ArgumentException(
                "A current federation state fingerprint is required.",
                nameof(stateFingerprint));
        }

        ArgumentNullException.ThrowIfNull(request);

        var canonical = new StringBuilder()
            .Append(stateFingerprint.Trim())
            .Append('\n')
            .Append(request.Mode)
            .Append('\n');

        foreach (var domain in request.Allowlist)
        {
            canonical.Append(domain).Append('\n');
        }

        return "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }
}

public interface IRuntimeStackFederationReviewService
{
    Task<RuntimeStackFederationReviewResponse?> ReviewAsync(
        string slugOrId,
        RuntimeStackFederationPolicyRequest request,
        CancellationToken ct);
}

public sealed class RuntimeStackFederationReviewService
    : IRuntimeStackFederationReviewService
{
    private readonly IRuntimeStackFederationStateService _stateService;
    private readonly FederationPolicyRequestValidator _requestValidator;

    public RuntimeStackFederationReviewService(
        IRuntimeStackFederationStateService stateService,
        FederationPolicyRequestValidator requestValidator)
    {
        _stateService = stateService;
        _requestValidator = requestValidator;
    }

    public async Task<RuntimeStackFederationReviewResponse?> ReviewAsync(
        string slugOrId,
        RuntimeStackFederationPolicyRequest request,
        CancellationToken ct)
    {
        var canonicalRequest = _requestValidator.Validate(request);
        var current = await _stateService.GetAsync(slugOrId, ct);
        if (current is null)
        {
            return null;
        }

        EnsureReviewable(current);

        var currentAllowlist = string.Equals(
                current.Mode,
                FederationModes.Restricted,
                StringComparison.Ordinal)
            ? current.Allowlist.OrderBy(x => x, StringComparer.Ordinal).ToArray()
            : [];

        var added = canonicalRequest.Allowlist
            .Except(currentAllowlist, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var removed = currentAllowlist
            .Except(canonicalRequest.Allowlist, StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var configChangeRequired = !string.Equals(
                current.Mode,
                canonicalRequest.Mode,
                StringComparison.Ordinal) ||
            added.Length > 0 ||
            removed.Length > 0;
        var targetIngress = string.Equals(
                canonicalRequest.Mode,
                FederationModes.LocalOnly,
                StringComparison.Ordinal)
            ? FederationIngressModes.LocalOnly
            : FederationIngressModes.Normal;
        var ingressChangeRequired = !string.Equals(
            current.IngressMode,
            targetIngress,
            StringComparison.Ordinal);
        var noChange = !configChangeRequired && !ingressChangeRequired;

        var warnings = BuildWarnings(canonicalRequest.Mode, noChange);

        var reviewHash = FederationReviewHash.Compute(
            current.StateFingerprint!,
            canonicalRequest);

        return new RuntimeStackFederationReviewResponse(
            Source: "control-plane",
            Status: noChange ? "no_change" : "ready",
            RuntimeStackId: current.RuntimeStackId,
            Slug: current.Slug,
            CurrentMode: current.Mode,
            ProposedMode: canonicalRequest.Mode,
            CurrentAllowlist: currentAllowlist,
            CanonicalAllowlist: canonicalRequest.Allowlist,
            AddedDomains: added,
            RemovedDomains: removed,
            RestartRequired: !noChange,
            IngressChangeRequired: ingressChangeRequired,
            NoChange: noChange,
            Warnings: warnings,
            ConfirmationText: ConfirmationText(canonicalRequest.Mode),
            ReviewHash: reviewHash);
    }

    private static void EnsureReviewable(RuntimeStackFederationStateResponse current)
    {
        if (string.IsNullOrWhiteSpace(current.StateFingerprint))
        {
            throw new FederationReviewBlockedException(
                "federation_state_unavailable",
                "MEM could not establish a stable current-state fingerprint for this stack.");
        }

        if (string.Equals(
                current.ConfigurationState,
                FederationConfigurationStates.CustomUnsupported,
                StringComparison.Ordinal))
        {
            throw new FederationReviewBlockedException(
                "federation_config_custom_unsupported",
                "The current federation configuration uses a custom representation that MEM will not rewrite automatically.");
        }

        if (string.Equals(
                current.ConfigurationState,
                FederationConfigurationStates.Unavailable,
                StringComparison.Ordinal))
        {
            throw new FederationReviewBlockedException(
                "federation_state_unavailable",
                "The current federation state is unavailable and cannot be reviewed safely.");
        }

        if (string.Equals(current.Mode, FederationModes.Unknown, StringComparison.Ordinal))
        {
            throw new FederationReviewBlockedException(
                current.Problems.FirstOrDefault()?.Code ?? "federation_effective_state_mismatch",
                "The current federation mode could not be determined safely.");
        }
    }

    private static IReadOnlyList<FederationReviewWarningResponse> BuildWarnings(
        string mode,
        bool noChange)
    {
        if (noChange)
        {
            return [];
        }

        var warnings = new List<FederationReviewWarningResponse>
        {
            new(
                "federation_existing_rooms_may_be_affected",
                "Existing federated rooms may stop exchanging new events with homeservers that are no longer permitted."),
            new(
                "federation_historical_state_retained",
                "Existing users, rooms, messages, media, signing keys, and historical remote room state are not deleted.")
        };

        if (string.Equals(mode, FederationModes.LocalOnly, StringComparison.Ordinal))
        {
            warnings.Add(new FederationReviewWarningResponse(
                "federation_local_only_client_access_remains_public",
                "Local-only federation does not make the Matrix client endpoint private. Use MEM network or VPN controls if client access must also be restricted."));
        }

        return warnings;
    }

    private static string ConfirmationText(string mode) => mode switch
    {
        FederationModes.Public => "Apply Public federation and restart Matrix.",
        FederationModes.Restricted => "Apply Restricted federation and restart Matrix.",
        FederationModes.LocalOnly => "Apply Local-only federation, update NPM ingress, and restart Matrix.",
        _ => "Apply the reviewed federation policy and restart Matrix."
    };
}
