using HostAgent.Runtime.Coturn;

namespace HostAgent.Tests.Runtime.Coturn;

public sealed class CoturnRuntimePolicyTests
{
    [Fact]
    public void Production_configuration_uses_required_security_policy_and_hides_secret_from_command()
    {
        const string secret = "turn-secret-value";

        var configuration = CoturnRuntimePolicy.RenderConfiguration(
            "example.test",
            secret);
        var command = CoturnRuntimePolicy.BuildContainerCommand("203.0.113.10");

        Assert.True(CoturnRuntimePolicy.HasRequiredSecurityPolicy(configuration));
        Assert.Contains("static-auth-secret=turn-secret-value", configuration, StringComparison.Ordinal);
        Assert.Contains("no-tcp-relay", configuration, StringComparison.Ordinal);
        Assert.Contains("no-multicast-peers", configuration, StringComparison.Ordinal);
        Assert.Contains("no-tls", configuration, StringComparison.Ordinal);
        Assert.Contains("no-dtls", configuration, StringComparison.Ordinal);
        Assert.Contains("pidfile=/var/tmp/turnserver.pid", configuration, StringComparison.Ordinal);
        Assert.Contains("proc-user=nobody", configuration, StringComparison.Ordinal);
        Assert.Contains("proc-group=nogroup", configuration, StringComparison.Ordinal);
        Assert.Contains("user-quota=12", configuration, StringComparison.Ordinal);
        Assert.Contains("total-quota=1200", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("lt-cred-mech", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("no-cli", configuration, StringComparison.Ordinal);
        Assert.DoesNotContain("external-ip=", configuration, StringComparison.Ordinal);
        Assert.Equal("-c", command[0]);
        Assert.Equal("/etc/coturn/turnserver.conf", command[1]);
        Assert.Equal(
            "--allowed-peer-ip=$(hostname -i | awk '{print $1}')",
            command[2]);
        Assert.Equal("--external-ip=203.0.113.10", command[3]);
        Assert.DoesNotContain("getent ahostsv4", string.Join(' ', command), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, string.Join(' ', command), StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_conflicting_or_deprecated_authentication_lines_do_not_satisfy_the_release_policy()
    {
        var configuration = CoturnRuntimePolicy.RenderConfiguration(
            "example.test",
            "turn-secret-value");

        Assert.False(CoturnRuntimePolicy.HasRequiredSecurityPolicy(
            configuration + "lt-cred-mech\n"));
        Assert.False(CoturnRuntimePolicy.HasRequiredSecurityPolicy(
            configuration + "no-cli\n"));
    }

    [Fact]
    public void Missing_external_ip_explicitly_invokes_the_approved_image_detector_in_the_overridden_command()
    {
        var command = CoturnRuntimePolicy.BuildContainerCommand(null);

        Assert.Equal(
            new[]
            {
                "-c",
                CoturnRuntimePolicy.ContainerConfigPath,
                "--allowed-peer-ip=$(hostname -i | awk '{print $1}')",
                "--external-ip=$(detect-external-ip)"
            },
            command);
        Assert.DoesNotContain("getent ahostsv4", string.Join(' ', command), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("$(detect-external-ip)")]
    [InlineData("8.8.8.8; touch /tmp/not-allowed")]
    [InlineData("8.8.8.8\n--no-auth")]
    [InlineData("8.8.8.8/172.18.0.5")]
    [InlineData("203.0.113.10:3478")]
    [InlineData("203.0.113.10/24")]
    [InlineData("2001:db8::1%eth0")]
    public void External_ip_rejects_free_form_or_scoped_values(string value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            CoturnRuntimePolicy.NormalizeExternalIp(value));
    }

    [Fact]
    public void Running_without_relay_range_is_setup_incomplete()
    {
        var result = CoturnRuntimePolicy.Evaluate(ReadyObservation() with
        {
            RelayPortsPublished = false
        });

        Assert.False(result.Ready);
        Assert.Equal("running", result.ContainerState);
        Assert.Equal("setup-incomplete", result.Readiness);
        Assert.False(CoturnRuntimePolicy.CanProvideSynapseConfiguration(result));
    }

    [Fact]
    public void Running_with_full_release_posture_is_ready_for_stack_configuration()
    {
        var result = CoturnRuntimePolicy.Evaluate(ReadyObservation());

        Assert.True(result.Ready);
        Assert.Equal("ready", result.Readiness);
        Assert.Equal("ok", result.Status);
        Assert.Equal(CoturnOperatorStatuses.RuntimeReady, result.OperatorStatus);
        Assert.True(result.RuntimeExact);
        Assert.True(CoturnRuntimePolicy.CanProvideSynapseConfiguration(result));
    }


    [Fact]
    public void Exact_runtime_with_restricted_protected_evidence_is_verification_limited_not_repair_required()
    {
        var result = CoturnRuntimePolicy.Evaluate(ReadyObservation() with
        {
            ProtectedEvidenceAccess = CoturnProtectedEvidenceAccess.Restricted,
            SecretPresent = false,
            ConfigPresent = false,
            ConfigHashMatches = false,
            SecurityPolicyApplied = false
        });

        Assert.False(result.Ready);
        Assert.True(result.RuntimeExact);
        Assert.Equal("verification-limited", result.Readiness);
        Assert.Equal("protected_evidence_restricted", result.Status);
        Assert.Equal(CoturnOperatorStatuses.VerificationLimited, result.OperatorStatus);
        Assert.False(CoturnRuntimePolicy.CanProvideSynapseConfiguration(result));
    }

    [Fact]
    public void Available_protected_evidence_with_missing_setup_still_requires_repair()
    {
        var result = CoturnRuntimePolicy.Evaluate(ReadyObservation() with
        {
            ProtectedEvidenceAccess = CoturnProtectedEvidenceAccess.Available,
            SecretPresent = false
        });

        Assert.False(result.Ready);
        Assert.True(result.RuntimeExact);
        Assert.Equal("setup-incomplete", result.Readiness);
        Assert.Equal("setup_incomplete", result.Status);
        Assert.Equal(CoturnOperatorStatuses.RepairRequired, result.OperatorStatus);
    }

    [Fact]
    public void Exact_runtime_with_unavailable_protected_evidence_is_unknown_not_repair_required()
    {
        var result = CoturnRuntimePolicy.Evaluate(ReadyObservation() with
        {
            ProtectedEvidenceAccess = CoturnProtectedEvidenceAccess.Unavailable,
            SecretPresent = false,
            ConfigPresent = false,
            ConfigHashMatches = false,
            SecurityPolicyApplied = false
        });

        Assert.False(result.Ready);
        Assert.True(result.RuntimeExact);
        Assert.Equal("unknown", result.Readiness);
        Assert.Equal("protected_evidence_unavailable", result.Status);
        Assert.Equal(CoturnOperatorStatuses.Unknown, result.OperatorStatus);
    }

    [Fact]
    public void Running_without_exact_docker_inspection_is_unknown_and_not_ready()
    {
        var result = CoturnRuntimePolicy.Evaluate(ReadyObservation() with
        {
            DockerInspectionAvailable = false
        });

        Assert.False(result.Ready);
        Assert.False(result.RuntimeExact);
        Assert.Equal("unknown", result.Readiness);
        Assert.Equal("inspection_unavailable", result.Status);
        Assert.Equal(CoturnOperatorStatuses.Unknown, result.OperatorStatus);
    }

    [Fact]
    public void Restart_policy_drift_requires_repair_even_when_container_is_running()
    {
        var result = CoturnRuntimePolicy.Evaluate(ReadyObservation() with
        {
            RestartPolicyMatches = false
        });

        Assert.False(result.Ready);
        Assert.False(result.RuntimeExact);
        Assert.Equal("repair-required", result.Readiness);
        Assert.Equal("runtime_drift", result.Status);
        Assert.Equal(CoturnOperatorStatuses.RepairRequired, result.OperatorStatus);
    }

    [Fact]
    public void Docker_runtime_failure_is_needs_attention_not_healthy()
    {
        var result = CoturnRuntimePolicy.Evaluate(ReadyObservation() with
        {
            RuntimeStateHealthy = false
        });

        Assert.False(result.Ready);
        Assert.False(result.RuntimeExact);
        Assert.Equal("degraded", result.Readiness);
        Assert.Equal("runtime_degraded", result.Status);
        Assert.Equal(CoturnOperatorStatuses.NeedsAttention, result.OperatorStatus);
    }

    [Fact]
    public void Domain_drift_is_not_ready()
    {
        var result = CoturnRuntimePolicy.Evaluate(ReadyObservation() with
        {
            DomainDriftDetected = true
        });

        Assert.False(result.Ready);
        Assert.Equal("needs_reconfigure", result.Status);
        Assert.Equal(CoturnOperatorStatuses.RepairRequired, result.OperatorStatus);
    }

    [Fact]
    public void Unowned_name_collision_is_unavailable()
    {
        var result = CoturnRuntimePolicy.Evaluate(ReadyObservation() with
        {
            OwnershipVerified = false
        });

        Assert.False(result.Ready);
        Assert.Equal("collision", result.ContainerState);
        Assert.Equal("unavailable", result.Readiness);
        Assert.Equal(CoturnOperatorStatuses.Conflict, result.OperatorStatus);
    }

    [Fact]
    public void Ownership_requires_both_platform_and_coturn_labels()
    {
        Assert.True(CoturnRuntimePolicy.IsOwned(new Dictionary<string, string>
        {
            ["mem.component"] = "platform",
            ["mem.service"] = "coturn"
        }));

        Assert.False(CoturnRuntimePolicy.IsOwned(new Dictionary<string, string>
        {
            ["mem.component"] = "platform"
        }));

        Assert.False(CoturnRuntimePolicy.IsOwned(new Dictionary<string, string>()));
    }

    [Fact]
    public void Browser_response_contract_does_not_expose_secret_path()
    {
        var propertyNames = typeof(CoturnRuntimeResponse)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("SecretStoragePath", propertyNames);
        Assert.Contains("SecretPresent", propertyNames);
        Assert.Contains("SecretStorage", propertyNames);
        Assert.Contains("ProtectedEvidenceAccess", propertyNames);

        var dockerEvidencePropertyNames = typeof(CoturnDockerRuntimeEvidence)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("ConfigMountSource", dockerEvidencePropertyNames);
        Assert.DoesNotContain("HostPath", dockerEvidencePropertyNames);
        Assert.DoesNotContain("Command", dockerEvidencePropertyNames);
        Assert.Contains("ConfigMountSourceMatches", dockerEvidencePropertyNames);
        Assert.Contains("CommandMatches", dockerEvidencePropertyNames);
    }

    private static CoturnRuntimeObservation ReadyObservation() => new(
        ContainerExists: true,
        Running: true,
        OwnershipVerified: true,
        DockerInspectionAvailable: true,
        RuntimeStateHealthy: true,
        RestartPolicyMatches: true,
        NetworkAttachmentMatches: true,
        NetworkAliasesMatch: true,
        ConfigMountMatches: true,
        CommandMatches: true,
        StartupUserMatches: true,
        ProtectedEvidenceAccess: CoturnProtectedEvidenceAccess.Available,
        ImageApproved: true,
        SecretPresent: true,
        ConfigPresent: true,
        ConfigHashMatches: true,
        SecurityPolicyApplied: true,
        RelayPortsPublished: true,
        DomainDriftDetected: false);
}
