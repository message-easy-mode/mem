using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Setup.Platform.Coturn;

namespace Modules.Setup.InstallRuns;

public sealed class SetupHandoffService
{
    private readonly MemDbContext _db;
    private readonly ILogger<SetupHandoffService> _logger;
    private readonly InstallProgressReporter? _progressReporter;

    public SetupHandoffService(
        MemDbContext db,
        ILogger<SetupHandoffService> logger,
        InstallProgressReporter? progressReporter = null)
    {
        _db = db;
        _logger = logger;
        _progressReporter = progressReporter;
    }

    public async Task<SetupHandoffResponse?> GetAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var installation = await _db.Installations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == installationId,
                cancellationToken);

        if (installation is null)
        {
            return null;
        }

        var handoffStep = await _db.InstallationStepExecutions
            .AsNoTracking()
            .Where(x =>
                x.InstallationId == installationId &&
                x.StepName == InstallStepNames.CompleteSetupHandoff)
            .OrderByDescending(x => x.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

        var handoffRequired = handoffStep is not null;
        var handoffCompleted = handoffStep is null ||
            string.Equals(
                handoffStep.Status,
                "Succeeded",
                StringComparison.OrdinalIgnoreCase);

        var domain = await _db.Domains
            .Include(x => x.ActiveCertificate)
            .Include(x => x.Certificates)
            .AsNoTracking()
            .Where(x => x.IsMainPlatformDomain && x.Status == "Active")
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var certificate = domain?.ActiveCertificate
            ?? domain?.Certificates
                .Where(x => x.IsActive && x.Status != "Deleted")
                .OrderByDescending(x => x.IsMainPlatformCertificate)
                .ThenByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault();

        var warnings = new List<string>();

        if (domain is null)
        {
            warnings.Add(
                "No active public stack domain is registered yet. The private control plane can still be used, but Matrix and Element public routing will require a domain and certificate.");
        }
        else if (certificate is null)
        {
            warnings.Add(
                "No active certificate is registered for the public stack domain. Issue or select a certificate before publishing Matrix or Element routes.");
        }
        else if (certificate.IsStaging)
        {
            warnings.Add(
                "The selected certificate was issued from the Let's Encrypt staging environment. Browser HTTPS trust will fail until a production certificate is issued.");
        }

        var ready = string.Equals(
            installation.Status,
            "Succeeded",
            StringComparison.OrdinalIgnoreCase);

        var status = ready ? "ready" : "not_ready";
        var message = ready
            ? handoffRequired && !handoffCompleted
                ? "Managed MEM platform installation and verification are complete. Finish setup to acknowledge handoff into normal operator use."
                : "Managed MEM platform setup is complete. Continue in the operator dashboard."
            : "Managed MEM platform setup is not complete. Review setup activity and verification before continuing.";

        _logger.LogInformation(
            "Built private control-plane handoff. InstallationId={InstallationId} Status={Status} HandoffRequired={HandoffRequired} HandoffCompleted={HandoffCompleted}",
            installationId,
            status,
            handoffRequired,
            handoffCompleted);

        return new SetupHandoffResponse(
            InstallationId: installationId,
            Status: status,
            Message: message,
            OperatorDashboardPath: "/dashboard",
            BaseDomain: domain?.BaseDomain,
            CertificateCommonName: certificate?.CommonName,
            CertificateId: certificate?.CertificateId,
            IsStagingCertificate: certificate?.IsStaging ?? false,
            CertificateExpiresAtUtc: certificate?.ExpiresAtUtc is DateTime expiresAtUtc
                ? new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc))
                : null,
            NpmCertificateId: certificate?.NpmCertificateId,
            PostgresContainerName: "mem-postgres",
            NpmContainerName: "mem-npm",
            CoturnContainerName: PlatformCoturnSetupDefaults.ContainerName,
            HandoffRequired: handoffRequired,
            HandoffCompleted: handoffCompleted,
            Warnings: warnings);
    }

    public async Task<SetupHandoffCompletionResponse?> CompleteAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        var installation = await _db.Installations
            .FirstOrDefaultAsync(
                x => x.Id == installationId,
                cancellationToken);

        if (installation is null)
        {
            return null;
        }

        var handoffStep = await _db.InstallationStepExecutions
            .Where(x =>
                x.InstallationId == installationId &&
                x.StepName == InstallStepNames.CompleteSetupHandoff)
            .OrderByDescending(x => x.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

        if (handoffStep is null)
        {
            if (string.Equals(
                installation.Status,
                "Succeeded",
                StringComparison.OrdinalIgnoreCase))
            {
                return new SetupHandoffCompletionResponse(
                    installationId,
                    Completed: true,
                    Status: "not_required",
                    Message: "This completed installation predates durable setup handoff acknowledgement and does not require an additional handoff action.");
            }

            return new SetupHandoffCompletionResponse(
                installationId,
                Completed: false,
                Status: "not_ready",
                Message: "Setup handoff is not available because the installation has not completed.");
        }

        if (string.Equals(
            handoffStep.Status,
            "Succeeded",
            StringComparison.OrdinalIgnoreCase))
        {
            return new SetupHandoffCompletionResponse(
                installationId,
                Completed: true,
                Status: "completed",
                Message: "Setup handoff has already been completed.");
        }

        if (!string.Equals(
                installation.Status,
                "Succeeded",
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                handoffStep.Status,
                "WaitingForUser",
                StringComparison.OrdinalIgnoreCase))
        {
            return new SetupHandoffCompletionResponse(
                installationId,
                Completed: false,
                Status: "not_ready",
                Message: "Setup handoff cannot be completed until installation and final verification have succeeded.");
        }

        var priorIncompleteStepExists = await _db.InstallationStepExecutions
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.InstallationId == installationId &&
                    x.Sequence < handoffStep.Sequence &&
                    x.Status != "Succeeded" &&
                    x.Status != "Skipped",
                cancellationToken);

        if (priorIncompleteStepExists)
        {
            return new SetupHandoffCompletionResponse(
                installationId,
                Completed: false,
                Status: "not_ready",
                Message: "Setup handoff cannot be completed while an earlier installation step remains incomplete.");
        }

        var now = DateTime.UtcNow;

        handoffStep.Status = "Succeeded";
        handoffStep.Message = "Setup handoff acknowledged. Continue in the operator Control Plane.";
        handoffStep.ErrorMessage = null;
        handoffStep.CompletedAtUtc = now;

        installation.LastError = null;
        installation.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(cancellationToken);

        if (_progressReporter is not null)
        {
            var progressContext = new InstallStepContext(
                installation.Id,
                handoffStep.Id,
                handoffStep.StepName,
                handoffStep.Sequence,
                installation.FrozenConfigJson ?? installation.ConfigJson,
                handoffStep.AttemptCount);

            // The handoff is already authoritatively committed in SQLite.
            // Progress history is supplemental and must not turn a completed
            // handoff into a failed HTTP request if the caller disconnects.
            await _progressReporter.CompleteStepAsync(
                progressContext,
                handoffStep.Message ?? "Setup handoff acknowledged.",
                CancellationToken.None);
        }

        _logger.LogInformation(
            "Completed setup handoff acknowledgement for installation {InstallationId}",
            installationId);

        return new SetupHandoffCompletionResponse(
            installationId,
            Completed: true,
            Status: "completed",
            Message: "Setup handoff completed. Continue in the operator dashboard.");
    }
}
