namespace Shared.Diagnostics;

public static class MemDiagnosticCodes
{
    public const string StoreDisabled = "diagnostics.store_disabled";
    public const string StoreWriteFailed = "diagnostics.store_write_failed";
    public const string StoreReadFailed = "diagnostics.store_read_failed";
    public const string EventMalformed = "diagnostics.event_malformed";
    public const string EventOversized = "diagnostics.event_oversized";
    public const string CursorInvalid = "diagnostics.cursor_invalid";
    public const string RetentionFailed = "diagnostics.retention_failed";
    public const string SupportReportSizeTruncated = "diagnostics.support_report_size_truncated";
    public const string SupportReportDockerLogTruncated = "diagnostics.support_report_docker_log_truncated";
    public const string SelfTestLocalRecorderUnavailable = "diagnostics.self_test.local_recorder_unavailable";
    public const string SelfTestLocalRecorderNoWriteObserved = "diagnostics.self_test.local_recorder_no_write_observed";
    public const string SelfTestSafeEventWriteFailed = "diagnostics.self_test.safe_event_write_failed";
    public const string SelfTestSafeEventReadBackFailed = "diagnostics.self_test.safe_event_read_back_failed";
    public const string SelfTestCorrelationMismatch = "diagnostics.self_test.correlation_mismatch";
    public const string DockerEvidenceDisabled = "diagnostics.docker_evidence_disabled";
    public const string DockerEvidenceResourceNotResolved = "diagnostics.docker_evidence_resource_not_resolved";
    public const string DockerEvidenceContainerNotFound = "diagnostics.docker_evidence_container_not_found";
    public const string DockerEvidenceTimedOut = "diagnostics.docker_evidence_timed_out";
    public const string DockerEvidenceReadFailed = "diagnostics.docker_evidence_read_failed";
    public const string DockerEvidenceTruncated = "diagnostics.docker_evidence_truncated";
    public const string DockerEvidenceSensitiveOperationalMetadata =
        "diagnostics.docker_evidence_sensitive_operational_metadata";
}
