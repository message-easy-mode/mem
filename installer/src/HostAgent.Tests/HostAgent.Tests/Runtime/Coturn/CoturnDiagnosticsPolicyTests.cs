using System.Globalization;
using HostAgent.Runtime.Coturn;

namespace HostAgent.Tests.Runtime.Coturn;

public sealed class CoturnDiagnosticsPolicyTests
{
    [Theory]
    [InlineData(null, CoturnDiagnosticsPolicy.DefaultLogTail)]
    [InlineData(1, CoturnDiagnosticsPolicy.MinimumLogTail)]
    [InlineData(100, 100)]
    [InlineData(999, CoturnDiagnosticsPolicy.MaximumLogTail)]
    public void Log_tail_is_bounded_for_browser_diagnostics(int? requested, int expected)
    {
        Assert.Equal(expected, CoturnDiagnosticsPolicy.NormalizeLogTail(requested));
    }

    [Fact]
    public void Temporary_rest_credential_is_short_lived_and_does_not_reuse_the_shared_secret()
    {
        var now = new DateTimeOffset(2026, 7, 26, 9, 30, 0, TimeSpan.Zero);
        const string sharedSecret = "platform-shared-secret";

        var credential = CoturnDiagnosticsPolicy.CreateTemporaryCredential(
            sharedSecret,
            now);

        Assert.Equal(now.AddMinutes(2), credential.ExpiresAtUtc);
        Assert.Equal("1785058320:mem-platform-check", credential.Username);
        Assert.Equal("nYfWbg0UZogxkuwXgveohrwS7ls=", credential.Password);
        Assert.StartsWith(
            credential.ExpiresAtUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            credential.Username,
            StringComparison.Ordinal);
        Assert.EndsWith(":mem-platform-check", credential.Username, StringComparison.Ordinal);
        Assert.NotEqual(sharedSecret, credential.Password);
        Assert.DoesNotContain(sharedSecret, credential.Username, StringComparison.Ordinal);
        Assert.DoesNotContain(sharedSecret, credential.Password, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostic_text_is_bounded_and_redacts_secret_assignments_and_temporary_credentials()
    {
        const string exactSecret = "exact-secret-value";
        var lines = Enumerable.Range(0, 260)
            .Select(index => $"line-{index}")
            .Concat(
            [
                "static-auth-secret=server-secret",
                "password: client-password",
                "turnutils_uclient -u 1780000000:matrix-user -w temporary-password turn.example.test",
                "session user 1780000001:standalone-user",
                $"exact value {exactSecret}"
            ]);

        var result = CoturnDiagnosticsPolicy.SanitizeDiagnosticText(
            string.Join("\n", lines),
            exactSecret);

        Assert.True(result.Truncated);
        Assert.InRange(result.ReturnedLines, 1, CoturnDiagnosticsPolicy.MaximumDiagnosticLines);
        Assert.DoesNotContain("server-secret", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("client-password", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("1780000000:matrix-user", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("temporary-password", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(exactSecret, result.Content, StringComparison.Ordinal);
        Assert.Contains("[redacted]", result.Content, StringComparison.Ordinal);
        Assert.Contains("[redacted-turn-user]", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostic_text_strips_docker_multiplex_headers_ansi_and_control_characters()
    {
        var framed =
            "\u0001\u0000\u0000\u0000\u0000\u0000\u0000\u001c" +
            "\u001b[33m0: WARNING: first line\u001b[0m\n" +
            "\u0002\u0000\u0000\u0000\u0000\u0000\u0000\u0020" +
            "0: INFO: second line\n";

        var result = CoturnDiagnosticsPolicy.SanitizeDiagnosticText(framed);

        Assert.Equal(
            "0: WARNING: first line\n0: INFO: second line",
            result.Content);
        Assert.All(result.Content, character =>
        {
            if (character is not '\n' and not '\t')
            {
                Assert.False(char.IsControl(character));
            }
        });
        Assert.DoesNotContain("\u001b", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Overall_check_status_preserves_failed_warning_and_passed_distinctions()
    {
        var allocationPassed = new CoturnAllocationProbeResponse(
            CoturnCheckStatuses.Passed,
            "udp",
            "passed",
            null);

        Assert.Equal(
            CoturnCheckStatuses.Passed,
            CoturnDiagnosticsPolicy.SummarizeStatus(
            [
                new CoturnCheckItem("container", CoturnCheckStatuses.Passed, "passed")
            ],
            allocationPassed));

        Assert.Equal(
            CoturnCheckStatuses.Warning,
            CoturnDiagnosticsPolicy.SummarizeStatus(
            [
                new CoturnCheckItem("evidence", CoturnCheckStatuses.Warning, "warning")
            ],
            allocationPassed));

        Assert.Equal(
            CoturnCheckStatuses.Failed,
            CoturnDiagnosticsPolicy.SummarizeStatus(
            [
                new CoturnCheckItem("dns", CoturnCheckStatuses.Failed, "failed")
            ],
            allocationPassed));
    }

    [Fact]
    public void Probe_runtime_dns_warning_with_not_run_allocation_summarizes_as_warning()
    {
        var allocationNotRun = new CoturnAllocationProbeResponse(
            CoturnCheckStatuses.NotRun,
            "udp",
            "allocation was not run because probe DNS was unavailable",
            null);

        var status = CoturnDiagnosticsPolicy.SummarizeStatus(
        [
            new CoturnCheckItem(
                "probe-dns",
                CoturnCheckStatuses.Warning,
                "probe runtime DNS unavailable")
        ],
        allocationNotRun);

        Assert.Equal(CoturnCheckStatuses.Warning, status);
    }

    [Fact]
    public void Public_automatic_relay_evidence_passes_without_claiming_external_reachability()
    {
        var allocation = PublicRelayProbe();
        var check = CoturnRuntimeService.BuildExternalIpCheck(null, allocation);

        Assert.Equal("external-ip", check.Key);
        Assert.Equal(CoturnCheckStatuses.Passed, check.Status);
        Assert.Equal("relay-auto-public", check.Code);
        Assert.Contains("independent external test", check.Detail!, StringComparison.Ordinal);
        Assert.Equal(CoturnCheckStatuses.Passed,
            CoturnDiagnosticsPolicy.SummarizeStatus([check], allocation));
    }

    [Fact]
    public void Explicit_public_ip_requires_matching_allocation_evidence()
    {
        var check = CoturnRuntimeService.BuildExternalIpCheck("8.8.8.8", PublicRelayProbe());
        Assert.Equal(CoturnCheckStatuses.Passed, check.Status);
        Assert.Equal("relay-explicit-public", check.Code);
        Assert.Contains("independent external test", check.Detail!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CoturnCheckStatuses.Failed, CoturnCheckStatuses.Failed)]
    [InlineData(CoturnCheckStatuses.NotRun, CoturnCheckStatuses.Warning)]
    public void Automatic_external_ip_mode_preserves_real_allocation_failures_and_uncertainty(
        string allocationStatus,
        string expectedStatus)
    {
        var allocation = PublicRelayProbe() with { Status = allocationStatus };
        var check = CoturnRuntimeService.BuildExternalIpCheck(null, allocation);
        Assert.Equal(expectedStatus, CoturnDiagnosticsPolicy.SummarizeStatus([check], allocation));
    }

    [Fact]
    public void Legacy_passed_mode_only_evidence_is_stale_even_before_its_time_limit()
    {
        var now = DateTimeOffset.Parse("2026-09-23T00:00:00Z");
        var old = SampleCheck(now) with
        {
            Checks = [new CoturnCheckItem("external-ip", "passed", "automatic mode selected")]
        };
        var result = CoturnDiagnosticsPolicy.EvaluateFreshness(old, now.AddSeconds(1));
        Assert.False(result.Fresh);
        Assert.Equal(CoturnCheckFreshnessStatuses.Stale, result.Freshness);
    }

    private static CoturnAllocationProbeResponse PublicRelayProbe() =>
        new(CoturnCheckStatuses.Passed, "udp", "allocation passed", null)
        {
            RelayAddressEvidence = CoturnRelayAddressPolicy.Parse(
                "0: (25): INFO: IPv4. Received relay addr: 8.8.8.8:49168")
        };

    [Fact]
    public void Functional_check_is_fresh_for_thirty_minutes_then_becomes_stale()
    {
        var checkedAt = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
        var result = SampleCheck(checkedAt);

        var fresh = CoturnDiagnosticsPolicy.EvaluateFreshness(
            result,
            checkedAt.AddMinutes(30));
        var stale = CoturnDiagnosticsPolicy.EvaluateFreshness(
            result,
            checkedAt.AddMinutes(30).AddSeconds(1));

        Assert.True(fresh.Fresh);
        Assert.Equal(CoturnCheckFreshnessStatuses.Fresh, fresh.Freshness);
        Assert.Equal(checkedAt.AddMinutes(30), fresh.FreshUntilUtc);

        Assert.False(stale.Fresh);
        Assert.Equal(CoturnCheckFreshnessStatuses.Stale, stale.Freshness);
        Assert.Equal(checkedAt.AddMinutes(30), stale.FreshUntilUtc);
    }

    [Fact]
    public void Runtime_change_invalidates_otherwise_fresh_functional_check_evidence()
    {
        var checkedAt = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
        var result = SampleCheck(checkedAt);

        var changed = CoturnDiagnosticsPolicy.EvaluateFreshness(
            result,
            checkedAt.AddMinutes(5),
            runtimeChanged: true);

        Assert.False(changed.Fresh);
        Assert.Equal(
            CoturnCheckFreshnessStatuses.RuntimeChanged,
            changed.Freshness);
        Assert.Equal(
            checkedAt.AddMinutes(30),
            changed.FreshUntilUtc);
    }

    [Fact]
    public void Functional_check_freshness_distinguishes_not_checked_from_unavailable_evidence()
    {
        var now = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);

        var notChecked = CoturnDiagnosticsPolicy.EvaluateFreshness(
            result: null,
            now);
        var unavailable = CoturnDiagnosticsPolicy.EvaluateFreshness(
            result: null,
            now,
            evidenceUnavailable: true);

        Assert.Equal(
            CoturnCheckFreshnessStatuses.NotChecked,
            notChecked.Freshness);
        Assert.False(notChecked.Fresh);

        Assert.Equal(
            CoturnCheckFreshnessStatuses.Unavailable,
            unavailable.Freshness);
        Assert.False(unavailable.Fresh);
    }

    [Fact]
    public void Functional_check_runtime_identity_is_invalidated_by_restart_or_replacement()
    {
        var checkedAt = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
        var result = SampleCheck(checkedAt);
        var startedAt = result.RuntimeStartedAtUtc;

        Assert.False(CoturnRuntimeService.RuntimeChangedSinceCheck(
            result,
            currentContainerId: result.RuntimeContainerId,
            currentOwned: true,
            currentStartedAtUtc: startedAt,
            currentRestartCount: result.RuntimeRestartCount,
            currentInspectionAvailable: true));

        Assert.True(CoturnRuntimeService.RuntimeChangedSinceCheck(
            result,
            currentContainerId: result.RuntimeContainerId,
            currentOwned: true,
            currentStartedAtUtc: startedAt,
            currentRestartCount: 1,
            currentInspectionAvailable: true));

        Assert.True(CoturnRuntimeService.RuntimeChangedSinceCheck(
            result,
            currentContainerId: result.RuntimeContainerId,
            currentOwned: true,
            currentStartedAtUtc: startedAt?.AddSeconds(5),
            currentRestartCount: result.RuntimeRestartCount,
            currentInspectionAvailable: true));

        Assert.True(CoturnRuntimeService.RuntimeChangedSinceCheck(
            result,
            currentContainerId: "replacement-container",
            currentOwned: true,
            currentStartedAtUtc: startedAt,
            currentRestartCount: result.RuntimeRestartCount,
            currentInspectionAvailable: true));
    }

    [Fact]
    public void Functional_check_without_a_previous_container_is_invalidated_when_coturn_is_later_deployed()
    {
        var checkedAt = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
        var result = SampleCheck(checkedAt) with
        {
            RuntimeContainerId = null,
            RuntimeStartedAtUtc = null,
            RuntimeRestartCount = null
        };

        Assert.True(CoturnRuntimeService.RuntimeChangedSinceCheck(
            result,
            currentContainerId: "new-container",
            currentOwned: true,
            currentStartedAtUtc: checkedAt,
            currentRestartCount: 0,
            currentInspectionAvailable: true));

        Assert.False(CoturnRuntimeService.RuntimeChangedSinceCheck(
            result,
            currentContainerId: null,
            currentOwned: false,
            currentStartedAtUtc: null,
            currentRestartCount: null,
            currentInspectionAvailable: false));

        Assert.True(CoturnRuntimeService.RuntimeChangedSinceCheck(
            result,
            currentContainerId: "foreign-container",
            currentOwned: false,
            currentStartedAtUtc: null,
            currentRestartCount: null,
            currentInspectionAvailable: false));
    }

    [Fact]
    public void Failed_functional_check_creates_a_coturn_incident_when_no_prior_incident_exists()
    {
        var current = SampleCheck(new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero)) with
        {
            Status = CoturnCheckStatuses.Failed,
            Allocation = new CoturnAllocationProbeResponse(
                CoturnCheckStatuses.Failed,
                "udp",
                "failed",
                null)
        };

        var plan = CoturnDiagnosticsPolicy.PlanFunctionalCheckDiagnostic(
            current,
            previous: null);

        Assert.Equal(CoturnFunctionalCheckEventCodes.Failed, plan.EventCode);
        Assert.Equal("error", plan.Severity);
        Assert.True(plan.CreateIncident);
        Assert.Null(plan.IncidentId);
        Assert.False(plan.Recovered);
    }

    [Fact]
    public void Failed_functional_check_reuses_the_previous_failed_incident()
    {
        var checkedAt = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
        var previous = SampleCheck(checkedAt.AddMinutes(-5)) with
        {
            Status = CoturnCheckStatuses.Failed,
            IncidentId = "inc_coturn_example"
        };
        var current = SampleCheck(checkedAt) with
        {
            Status = CoturnCheckStatuses.Failed
        };

        var plan = CoturnDiagnosticsPolicy.PlanFunctionalCheckDiagnostic(
            current,
            previous);

        Assert.Equal(CoturnFunctionalCheckEventCodes.Failed, plan.EventCode);
        Assert.Equal("inc_coturn_example", plan.IncidentId);
        Assert.False(plan.CreateIncident);
    }

    [Fact]
    public void Passed_functional_check_records_recovery_against_the_previous_failed_incident()
    {
        var checkedAt = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
        var previous = SampleCheck(checkedAt.AddMinutes(-5)) with
        {
            Status = CoturnCheckStatuses.Failed,
            IncidentId = "inc_coturn_example"
        };
        var current = SampleCheck(checkedAt);

        var plan = CoturnDiagnosticsPolicy.PlanFunctionalCheckDiagnostic(
            current,
            previous);

        Assert.Equal(CoturnFunctionalCheckEventCodes.Recovered, plan.EventCode);
        Assert.Equal("information", plan.Severity);
        Assert.Equal("inc_coturn_example", plan.IncidentId);
        Assert.False(plan.CreateIncident);
        Assert.True(plan.Recovered);
    }

    [Fact]
    public void New_failure_after_a_recovered_pass_creates_a_new_incident()
    {
        var checkedAt = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
        var previous = SampleCheck(checkedAt.AddMinutes(-5)) with
        {
            Status = CoturnCheckStatuses.Passed,
            IncidentId = "inc_previous_recovered_failure"
        };
        var current = SampleCheck(checkedAt) with
        {
            Status = CoturnCheckStatuses.Failed
        };

        var plan = CoturnDiagnosticsPolicy.PlanFunctionalCheckDiagnostic(
            current,
            previous);

        Assert.Equal(CoturnFunctionalCheckEventCodes.Failed, plan.EventCode);
        Assert.Null(plan.IncidentId);
        Assert.True(plan.CreateIncident);
        Assert.False(plan.Recovered);
    }

    [Fact]
    public void Warning_after_a_failed_check_does_not_reopen_the_failure_incident()
    {
        var checkedAt = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
        var previous = SampleCheck(checkedAt.AddMinutes(-5)) with
        {
            Status = CoturnCheckStatuses.Failed,
            IncidentId = "inc_coturn_example"
        };
        var current = SampleCheck(checkedAt) with
        {
            Status = CoturnCheckStatuses.Warning,
            Allocation = new CoturnAllocationProbeResponse(
                CoturnCheckStatuses.Passed,
                "udp",
                "allocation passed while another verification remains incomplete",
                null)
        };

        var plan = CoturnDiagnosticsPolicy.PlanFunctionalCheckDiagnostic(
            current,
            previous);

        Assert.Equal(CoturnFunctionalCheckEventCodes.Warning, plan.EventCode);
        Assert.Equal("warning", plan.Severity);
        Assert.Null(plan.IncidentId);
        Assert.False(plan.CreateIncident);
        Assert.False(plan.Recovered);
    }

    [Fact]
    public void Passing_check_after_a_warning_records_normal_pass_without_creating_an_incident()
    {
        var checkedAt = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
        var previous = SampleCheck(checkedAt.AddMinutes(-5)) with
        {
            Status = CoturnCheckStatuses.Warning,
            IncidentId = null
        };
        var current = SampleCheck(checkedAt) with
        {
            Status = CoturnCheckStatuses.Passed
        };

        var plan = CoturnDiagnosticsPolicy.PlanFunctionalCheckDiagnostic(
            current,
            previous);

        Assert.Equal(CoturnFunctionalCheckEventCodes.Passed, plan.EventCode);
        Assert.Equal("information", plan.Severity);
        Assert.Null(plan.IncidentId);
        Assert.False(plan.CreateIncident);
        Assert.False(plan.Recovered);
    }

    [Fact]
    public void Failure_after_a_warning_creates_a_new_failure_incident()
    {
        var checkedAt = new DateTimeOffset(2026, 8, 22, 8, 0, 0, TimeSpan.Zero);
        var previous = SampleCheck(checkedAt.AddMinutes(-5)) with
        {
            Status = CoturnCheckStatuses.Warning,
            IncidentId = null
        };
        var current = SampleCheck(checkedAt) with
        {
            Status = CoturnCheckStatuses.Failed,
            Allocation = new CoturnAllocationProbeResponse(
                CoturnCheckStatuses.Failed,
                "udp",
                "allocation failed again",
                null)
        };

        var plan = CoturnDiagnosticsPolicy.PlanFunctionalCheckDiagnostic(
            current,
            previous);

        Assert.Equal(CoturnFunctionalCheckEventCodes.Failed, plan.EventCode);
        Assert.Equal("error", plan.Severity);
        Assert.Null(plan.IncidentId);
        Assert.True(plan.CreateIncident);
        Assert.False(plan.Recovered);
    }

    [Fact]
    public void Diagnostic_browser_contracts_do_not_expose_credentials_or_host_secret_paths()
    {
        var propertyNames = typeof(CoturnCheckResponse)
            .GetProperties()
            .Concat(typeof(CoturnLatestCheckResponse).GetProperties())
            .Concat(typeof(CoturnLogsResponse).GetProperties())
            .Concat(typeof(CoturnAllocationProbeResponse).GetProperties())
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SecretValue", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SecretPath", StringComparison.OrdinalIgnoreCase));
    }

    private static CoturnCheckResponse SampleCheck(DateTimeOffset checkedAt) =>
        new(
            Source: "control-plane",
            Status: CoturnCheckStatuses.Passed,
            CheckedAtUtc: checkedAt,
            FreshUntilUtc: checkedAt.AddSeconds(
                CoturnDiagnosticsPolicy.FunctionalCheckFreshForSeconds),
            ContainerState: "running",
            Readiness: "ready",
            PublicHost: "turn.example.test",
            RuntimeContainerId: "coturn-container-1",
            RuntimeStartedAtUtc: checkedAt.AddMinutes(-10),
            RuntimeRestartCount: 0,
            Checks:
            [
                new CoturnCheckItem(
                    "container",
                    CoturnCheckStatuses.Passed,
                    "running"),
                CoturnRuntimeService.BuildExternalIpCheck(null, PublicRelayProbe())
            ],
            Allocation: new CoturnAllocationProbeResponse(
                CoturnCheckStatuses.Passed,
                "udp",
                "passed",
                null),
            Warnings: [],
            Detail: "passed",
            EvidencePersisted: true,
            IncidentId: null);

}
