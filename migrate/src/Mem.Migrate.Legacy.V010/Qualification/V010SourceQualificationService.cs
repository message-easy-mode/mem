using System.Security.Cryptography;
using System.Text.Json;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Qualification;

namespace Mem.Migrate.Legacy.V010.Qualification;

public sealed class V010SourceQualificationService(
    ISourceQualificationEnvironmentProbe environmentProbe,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<SourceQualificationReport> RunAsync(
        SourceQualificationOptions rawOptions,
        CancellationToken cancellationToken)
    {
        var options = rawOptions.Normalize();
        var planReport = await ReadAsync<CutoverPreparationReport>(
            options.CutoverPlanReportPath,
            "Cutover plan report",
            cancellationToken);
        var freeze = await ReadAsync<CutoverFreezeReport>(
            options.FreezeReportPath,
            "Source-freeze report",
            cancellationToken);
        var capture = await ReadAsync<CaptureReport>(
            options.CaptureReportPath,
            "Final capture report",
            cancellationToken);
        var package = await ReadAsync<PackageForIntakeReport>(
            options.PackageReportPath,
            "Final package report",
            cancellationToken);

        ValidatePlan(planReport);
        ValidateFreeze(planReport, freeze);
        var selectedStack = planReport.Plan.Stacks.Single();
        ValidateCapture(selectedStack, freeze, capture);
        await ValidatePackageAsync(options, selectedStack, capture, package, cancellationToken);

        var observation = await environmentProbe.ObserveAsync(
            planReport.Plan,
            options.DockerCommand,
            options.CommandTimeoutSeconds,
            cancellationToken);
        var containers = BuildContainerEvidence(planReport.Plan, freeze, observation);
        var stack = selectedStack;
        var generatedAtUtc = _timeProvider.GetUtcNow();
        var payload = new SourceQualificationPayload(
            options.QualificationAttemptId!,
            generatedAtUtc.UtcDateTime,
            package.MigrationId,
            package.IntakeId,
            package.PackageRevisionId!,
            package.EncryptedPackageSha256.ToLowerInvariant(),
            package.EncryptedPackageBytes,
            package.SourceArchiveSha256.ToLowerInvariant(),
            freeze.FreezeAttemptId,
            freeze.PlanId,
            freeze.PlanHash.ToLowerInvariant(),
            freeze.SourceFingerprint.ToLowerInvariant(),
            stack.Slug,
            stack.MatrixServerName,
            freeze.SourceFrozen,
            freeze.PublicRoutingMutationOccurred,
            planReport.Plan.DevelopmentExternalControlPlane,
            observation.Host,
            containers);
        var report = new SourceQualificationReport(
            Schema: "mem-two-server-source-qualification-report",
            SchemaVersion: 1,
            Status: "QualifiedSourceEvidence",
            QualificationAttemptId: options.QualificationAttemptId!,
            GeneratedAtUtc: generatedAtUtc,
            MigrationId: package.MigrationId,
            PackageRevisionId: package.PackageRevisionId!,
            EncryptedPackageSha256: package.EncryptedPackageSha256.ToLowerInvariant(),
            FreezeAttemptId: freeze.FreezeAttemptId,
            FreezePlanId: freeze.PlanId,
            SourceFingerprint: freeze.SourceFingerprint.ToLowerInvariant(),
            SourceStackSlug: stack.Slug,
            MatrixServerName: stack.MatrixServerName,
            SourceFrozen: true,
            DistinctHostEvidenceReady: true,
            DevelopmentExternalControlPlane: false,
            SourceHost: observation.Host,
            Containers: containers,
            EvidencePath: string.Empty,
            EvidenceSha256: string.Empty,
            JsonPath: string.Empty,
            MarkdownPath: string.Empty,
            Warnings: [],
            NextSteps:
            [
                "Transfer only the encrypted migration package and this source-evidence envelope to the new MEM 0.2.0 server.",
                "Import the source evidence into the target two-server qualification workflow and prove the target machine and Docker Engine identities are different.",
                "Keep the old source frozen and retained until target acceptance, native baseline backup, and qualification closure are recorded."
            ]);
        return await SourceQualificationRenderer.WriteAsync(
            report,
            payload,
            options.OutputPath,
            cancellationToken);
    }

    private static void ValidatePlan(CutoverPreparationReport report)
    {
        if (!string.Equals(report.Schema, "mem-cutover-preparation-report", StringComparison.Ordinal) ||
            report.SchemaVersion != 1 ||
            !string.Equals(report.Status, "Prepared", StringComparison.Ordinal) ||
            !string.Equals(report.Plan.Schema, "mem-cutover-plan", StringComparison.Ordinal) ||
            report.Plan.SchemaVersion != 2)
        {
            throw new InvalidDataException(
                "The cutover plan report does not use the supported production contract.");
        }

        var actualHash = CutoverPlanHash.Compute(report.Plan);
        if (!string.Equals(actualHash, report.PlanHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The cutover plan hash is invalid.");
        }

        if (report.Plan.DevelopmentExternalControlPlane)
        {
            throw new InvalidDataException(
                "Same-host development external-control-plane evidence cannot qualify the supported two-server topology.");
        }

        if (report.Plan.SourceClassification != AssessmentClassification.ConfirmedSupportedV010 ||
            report.Plan.Stacks.Length != 1)
        {
            throw new InvalidDataException(
                "Two-server qualification currently requires one confirmed MEM v0.1.0 source stack.");
        }
    }

    private static void ValidateFreeze(
        CutoverPreparationReport planReport,
        CutoverFreezeReport freeze)
    {
        if (!string.Equals(freeze.Schema, "mem-cutover-source-freeze-report", StringComparison.Ordinal) ||
            freeze.SchemaVersion != 1 ||
            !string.Equals(freeze.Status, "Frozen", StringComparison.Ordinal) ||
            !freeze.SourceFrozen ||
            freeze.PublicRoutingMutationOccurred)
        {
            throw new InvalidDataException(
                "The source-freeze report is not eligible for production qualification.");
        }

        if (!string.Equals(freeze.PlanId, planReport.Plan.PlanId, StringComparison.Ordinal) ||
            !string.Equals(freeze.PlanHash, planReport.PlanHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                freeze.SourceFingerprint,
                planReport.Plan.SourceFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The source-freeze report is not bound to the reviewed cutover plan.");
        }

        if (freeze.RollbackCheckpoint.SourceContainers.Length !=
            planReport.Plan.SourceContainersToFreeze.Length)
        {
            throw new InvalidDataException(
                "The source-freeze checkpoint does not contain every reviewed writer container.");
        }
    }

    private static void ValidateCapture(
        CutoverStack selectedStack,
        CutoverFreezeReport freeze,
        CaptureReport capture)
    {
        if (!string.Equals(capture.Schema, "mem-migration-capture-receipt", StringComparison.Ordinal) ||
            capture.SchemaVersion != 2 ||
            capture.SourceStackId == Guid.Empty ||
            capture.SourceStackId != selectedStack.SourceStackId ||
            !string.Equals(capture.SourceStackSlug, selectedStack.Slug, StringComparison.Ordinal) ||
            !string.Equals(capture.MatrixServerName, selectedStack.MatrixServerName, StringComparison.Ordinal) ||
            capture.Status != CaptureLifecycleStatus.Completed ||
            capture.RehearsalOnly ||
            capture.SourceChangedDuringCapture ||
            !string.Equals(
                capture.StartSourceFingerprint,
                freeze.SourceFingerprint,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                capture.CompletionSourceFingerprint,
                freeze.SourceFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The final capture report is not bound to the unchanged frozen source.");
        }
    }

    private static async Task ValidatePackageAsync(
        SourceQualificationOptions options,
        CutoverStack selectedStack,
        CaptureReport capture,
        PackageForIntakeReport package,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(package.Schema, "mem-package-for-intake-report", StringComparison.Ordinal) ||
            package.SchemaVersion != 2 ||
            !string.Equals(package.RequestedCaptureKind, "final", StringComparison.Ordinal) ||
            !string.Equals(package.CaptureKind, "final", StringComparison.Ordinal) ||
            !package.SourceFrozen ||
            package.RehearsalOnly ||
            package.StackCount != 1 ||
            package.SourceStackId == Guid.Empty ||
            package.SourceStackId != selectedStack.SourceStackId ||
            package.SourceStackId != capture.SourceStackId ||
            !string.Equals(package.SourceStackSlug, selectedStack.Slug, StringComparison.Ordinal) ||
            !string.Equals(package.SourceStackSlug, capture.SourceStackSlug, StringComparison.Ordinal) ||
            !string.Equals(package.MatrixServerName, selectedStack.MatrixServerName, StringComparison.Ordinal) ||
            !string.Equals(package.MatrixServerName, capture.MatrixServerName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(package.PackageRevisionId))
        {
            throw new InvalidDataException(
                "The package report is not an authoritative final frozen package revision.");
        }

        if (!string.Equals(
                package.SourceArchiveSha256,
                capture.ArchiveSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The final package report is not bound to the final capture archive.");
        }

        if (!string.Equals(
                package.EncryptedPackageSha256,
                options.ExpectedEncryptedPackageSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The final package report does not match the operator-reviewed encrypted package hash.");
        }

        if (!File.Exists(package.EncryptedPackagePath))
        {
            throw new FileNotFoundException(
                "The encrypted final migration package was not found.",
                package.EncryptedPackagePath);
        }

        await using var stream = new FileStream(
            package.EncryptedPackagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            useAsync: true);
        var actualHash = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
        if (!string.Equals(
                actualHash,
                options.ExpectedEncryptedPackageSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The encrypted final migration package bytes do not match the reviewed SHA-256 value.");
        }

        if (stream.Length != package.EncryptedPackageBytes)
        {
            throw new InvalidDataException(
                "The encrypted final migration package size does not match its package report.");
        }
    }

    private static SourceQualificationContainerEvidence[] BuildContainerEvidence(
        CutoverPlanDocument plan,
        CutoverFreezeReport freeze,
        SourceQualificationEnvironmentObservation observation)
    {
        var expected = plan.SourceContainersToFreeze
            .Select(item => new ExpectedContainer(
                item.Role,
                item.ContainerId,
                item.ContainerName,
                item.ImageId,
                WriterContainer: true,
                item.WasRunning))
            .Concat(plan.RetainedSourceContainers.Select(item => new ExpectedContainer(
                item.Role,
                item.ContainerId,
                item.ContainerName,
                item.ImageId,
                WriterContainer: false,
                WasRunning: string.Equals(item.State, "running", StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        var observedById = observation.Containers.ToDictionary(
            item => item.ContainerId,
            StringComparer.Ordinal);
        var evidence = new List<SourceQualificationContainerEvidence>(expected.Length);
        foreach (var item in expected)
        {
            if (!observedById.TryGetValue(item.ContainerId, out var observed))
            {
                throw new InvalidDataException(
                    $"Source qualification did not observe container '{item.ContainerName}'.");
            }

            var identityMatched =
                string.Equals(item.ContainerName, observed.ContainerName, StringComparison.Ordinal) &&
                string.Equals(item.ImageId, observed.ImageId, StringComparison.Ordinal);
            var frozenPreserved = !item.WriterContainer ||
                (!observed.Running &&
                 string.Equals(observed.RestartPolicy, "no", StringComparison.OrdinalIgnoreCase));
            if (!identityMatched)
            {
                throw new InvalidDataException(
                    $"Source container identity changed after freeze: '{item.ContainerName}'.");
            }

            if (!frozenPreserved)
            {
                throw new InvalidDataException(
                    $"Source writer container is no longer frozen and restart-disabled: '{item.ContainerName}'.");
            }

            evidence.Add(new SourceQualificationContainerEvidence(
                item.Role,
                item.ContainerId,
                item.ContainerName,
                item.ImageId,
                item.WriterContainer,
                item.WasRunning,
                observed.State,
                observed.Running,
                observed.RestartPolicy,
                identityMatched,
                frozenPreserved));
        }

        var frozenIds = freeze.Containers.Select(item => item.ContainerId).ToHashSet(StringComparer.Ordinal);
        if (plan.SourceContainersToFreeze.Any(item => !frozenIds.Contains(item.ContainerId)))
        {
            throw new InvalidDataException(
                "The freeze report does not prove every reviewed source writer was frozen.");
        }

        return evidence.ToArray();
    }

    private static async Task<T> ReadAsync<T>(
        string path,
        string description,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{description} was not found.", path);
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<T>(json, CaptureJson.Options)
               ?? throw new InvalidDataException($"{description} could not be read.");
    }

    private sealed record ExpectedContainer(
        string Role,
        string ContainerId,
        string ContainerName,
        string ImageId,
        bool WriterContainer,
        bool WasRunning);
}
