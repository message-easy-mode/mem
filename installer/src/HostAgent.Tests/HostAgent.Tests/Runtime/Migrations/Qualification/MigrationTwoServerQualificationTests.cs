using HostAgent.Runtime.Migrations.Qualification;

namespace HostAgent.Tests.Runtime.Migrations.Qualification;

public sealed class MigrationTwoServerQualificationTests
{
    [Fact]
    public void Source_evidence_hash_is_deterministic_and_content_bound()
    {
        var payload = CreatePayload();
        var same = CreatePayload();
        var changed = payload with { FreezeAttemptId = "freeze-other" };

        Assert.Equal(
            MigrationTwoServerQualificationService.ComputeSourcePayloadSha256(payload),
            MigrationTwoServerQualificationService.ComputeSourcePayloadSha256(same));
        Assert.NotEqual(
            MigrationTwoServerQualificationService.ComputeSourcePayloadSha256(payload),
            MigrationTwoServerQualificationService.ComputeSourcePayloadSha256(changed));
    }

    [Fact]
    public void Source_evidence_rejects_development_or_thawed_writer_state()
    {
        var payload = CreatePayload();
        MigrationTwoServerQualificationService.ValidateSourceEvidenceProof(payload);

        Assert.Throws<InvalidDataException>(() =>
            MigrationTwoServerQualificationService.ValidateSourceEvidenceProof(
                payload with { DevelopmentExternalControlPlane = true }));

        var thawed = payload with
        {
            Containers = payload.Containers.Select(item =>
                item.WriterContainer
                    ? item with
                    {
                        CurrentlyRunning = true,
                        CurrentState = "running",
                        CurrentRestartPolicy = "unless-stopped",
                        FrozenStatePreserved = false,
                    }
                    : item).ToArray(),
        };
        Assert.Throws<InvalidDataException>(() =>
            MigrationTwoServerQualificationService.ValidateSourceEvidenceProof(thawed));
    }

    [Fact]
    public void Source_evidence_must_bind_to_exact_accepted_migration()
    {
        var payload = CreatePayload();
        var expected = new MigrationTwoServerQualificationExpectedBindings(
            MigrationId: payload.IntakeId,
            SourceMigrationId: payload.MigrationId,
            PackageRevisionId: payload.PackageRevisionId,
            EncryptedPackageSha256: payload.EncryptedPackageSha256,
            EncryptedPackageBytes: payload.EncryptedPackageBytes,
            SourceArchiveSha256: payload.SourceArchiveSha256,
            SourceFingerprint: payload.SourceFingerprint,
            MatrixServerName: payload.MatrixServerName);

        MigrationTwoServerQualificationService.ValidateBindings(payload, expected);

        Assert.Throws<InvalidDataException>(() =>
            MigrationTwoServerQualificationService.ValidateBindings(
                payload with { PackageRevisionId = "mpr_other" },
                expected));
    }

    [Fact]
    public void Qualification_requires_distinct_machine_and_docker_engine_identities()
    {
        var source = CreateHost('a', 'b', "legacy-host", "legacy-docker");
        var target = CreateHost('c', 'd', "target-host", "target-docker");

        MigrationTwoServerQualificationService.ValidateDistinctHostIdentities(source, target);

        Assert.Throws<InvalidDataException>(() =>
            MigrationTwoServerQualificationService.ValidateDistinctHostIdentities(
                source,
                target with { MachineIdSha256 = source.MachineIdSha256 }));
        Assert.Throws<InvalidDataException>(() =>
            MigrationTwoServerQualificationService.ValidateDistinctHostIdentities(
                source,
                target with { DockerEngineIdSha256 = source.DockerEngineIdSha256 }));
    }

    [Fact]
    public void Source_envelope_rejects_payload_tampering()
    {
        var payload = CreatePayload();
        var envelope = new MigrationTwoServerSourceEvidenceEnvelope(
            MigrationTwoServerQualificationService.SourceEvidenceSchemaVersion,
            MigrationTwoServerQualificationService.ComputeSourcePayloadSha256(payload),
            payload);

        MigrationTwoServerQualificationService.ValidateSourceEvidenceEnvelope(envelope);

        Assert.Throws<InvalidDataException>(() =>
            MigrationTwoServerQualificationService.ValidateSourceEvidenceEnvelope(
                envelope with { Payload = payload with { MatrixServerName = "other.example.test" } }));
    }

    private static MigrationTwoServerSourceEvidencePayload CreatePayload() =>
        new(
            QualificationAttemptId: "mm01e-source-test",
            GeneratedAtUtc: new DateTime(2026, 7, 20, 11, 0, 0, DateTimeKind.Utc),
            MigrationId: "source-migration",
            IntakeId: "mig_target",
            PackageRevisionId: "mpr_final",
            EncryptedPackageSha256: new string('1', 64),
            EncryptedPackageBytes: 12345,
            SourceArchiveSha256: new string('2', 64),
            FreezeAttemptId: "freeze-test",
            FreezePlanId: "plan-test",
            FreezePlanSha256: new string('3', 64),
            SourceFingerprint: new string('4', 64),
            SourceStackSlug: "legacy-stack",
            MatrixServerName: "matrix.example.test",
            SourceFrozen: true,
            PublicRoutingMutationOccurred: false,
            DevelopmentExternalControlPlane: false,
            SourceHost: CreateHost('a', 'b', "legacy-host", "legacy-docker"),
            Containers:
            [
                new MigrationTwoServerSourceContainerEvidence(
                    Role: "matrix",
                    ContainerId: "matrix-container-id",
                    ContainerName: "legacy-matrix",
                    ImageId: "sha256:matrix",
                    WriterContainer: true,
                    WasRunningBeforeFreeze: true,
                    CurrentState: "exited",
                    CurrentlyRunning: false,
                    CurrentRestartPolicy: "no",
                    IdentityMatched: true,
                    FrozenStatePreserved: true),
                new MigrationTwoServerSourceContainerEvidence(
                    Role: "postgres",
                    ContainerId: "postgres-container-id",
                    ContainerName: "legacy-postgres",
                    ImageId: "sha256:postgres",
                    WriterContainer: false,
                    WasRunningBeforeFreeze: true,
                    CurrentState: "running",
                    CurrentlyRunning: true,
                    CurrentRestartPolicy: "unless-stopped",
                    IdentityMatched: true,
                    FrozenStatePreserved: true),
            ]);

    private static MigrationTwoServerHostIdentity CreateHost(
        char machineHash,
        char dockerHash,
        string machineName,
        string dockerName) =>
        new(
            MachineIdSha256: new string(machineHash, 64),
            MachineName: machineName,
            OperatingSystem: "Linux",
            Architecture: "X64",
            DockerEngineIdSha256: new string(dockerHash, 64),
            DockerName: dockerName,
            DockerServerVersion: "27.0.0");
}
