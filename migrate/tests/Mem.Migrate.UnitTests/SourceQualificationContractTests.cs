using Mem.Migrate.Core.Qualification;
using Mem.Migrate.Legacy.V010.Qualification;

namespace Mem.Migrate.UnitTests;

public sealed class SourceQualificationContractTests
{
    [Fact]
    public void Source_evidence_hash_is_deterministic_and_content_bound()
    {
        var payload = CreatePayload("source-a");
        var same = CreatePayload("source-a");
        var changed = CreatePayload("source-b");

        Assert.Equal(
            SourceQualificationRenderer.ComputePayloadSha256(payload),
            SourceQualificationRenderer.ComputePayloadSha256(same));
        Assert.NotEqual(
            SourceQualificationRenderer.ComputePayloadSha256(payload),
            SourceQualificationRenderer.ComputePayloadSha256(changed));
        Assert.Equal(
            "mem.migration.two-server-source-evidence.v1",
            SourceQualificationRenderer.EvidenceSchemaVersion);
    }

    private static SourceQualificationPayload CreatePayload(string fingerprint) =>
        new(
            QualificationAttemptId: "mm01e-source-proof",
            GeneratedAtUtc: new DateTime(2026, 7, 20, 1, 0, 0, DateTimeKind.Utc),
            MigrationId: "migration-proof",
            IntakeId: "mig-proof",
            PackageRevisionId: "mpr-final",
            EncryptedPackageSha256: new string('1', 64),
            EncryptedPackageBytes: 42,
            SourceArchiveSha256: new string('2', 64),
            FreezeAttemptId: "mm06b-proof",
            FreezePlanId: "mm06a-proof",
            FreezePlanSha256: new string('3', 64),
            SourceFingerprint: fingerprint,
            SourceStackSlug: "legacy-stack",
            MatrixServerName: "matrix.example.test",
            SourceFrozen: true,
            PublicRoutingMutationOccurred: false,
            DevelopmentExternalControlPlane: false,
            SourceHost: new QualificationHostIdentity(
                new string('4', 64),
                "source-host",
                "Linux",
                "X64",
                new string('5', 64),
                "source-docker",
                "27.0"),
            Containers: []);
}
