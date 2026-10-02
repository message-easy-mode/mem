using System.Security.Claims;
using Carter;
using Microsoft.AspNetCore.Authorization;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Identity;
using Modules.Integrations.Npm.Services;
using Modules.Shared.Domains;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Certificates.Npm;
using Modules.Shared.Domains.Issuance;
using Modules.Shared.Domains.Renewal;

namespace Modules.Operator.Domains;

public sealed class OperatorDomainsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/operator/domains")
            .WithTags("Operator Domains");

        group.MapGet("", async (
            DomainRegistryService registry,
            MemDbContext db,
            CancellationToken cancellationToken) =>
        {
            await registry.SyncStorageAsync(cancellationToken);

            var domains = await db.Domains
                .Include(x => x.ActiveCertificate)
                .Include(x => x.Certificates)
                .OrderByDescending(x => x.IsMainPlatformDomain)
                .ThenBy(x => x.BaseDomain)
                .Select(x => new OperatorDomainSummaryResponse(
                    x.Id,
                    x.BaseDomain,
                    x.DisplayName,
                    x.Purpose,
                    x.IsMainPlatformDomain,
                    x.DnsProvider,
                    x.DnsZone,
                    x.Status,
                    x.ActiveCertificateId,
                    x.ActiveCertificate == null ? null : x.ActiveCertificate.CertificateId,
                    x.ActiveCertificate == null ? null : x.ActiveCertificate.CommonName,
                    x.ActiveCertificate == null ? null : x.ActiveCertificate.IsStaging,
                    x.ActiveCertificate == null ? null : x.ActiveCertificate.ExpiresAtUtc,
                    x.Certificates.Count(c => c.Status != "Deleted"),
                    x.CreatedAtUtc,
                    x.UpdatedAtUtc))
                .ToListAsync(cancellationToken);

            var normalized = domains
                .Select(item => item with
                {
                    ActiveCertificateExpiresAtUtc = NormalizeUtc(item.ActiveCertificateExpiresAtUtc),
                    CreatedAtUtc = NormalizeUtc(item.CreatedAtUtc),
                    UpdatedAtUtc = NormalizeUtc(item.UpdatedAtUtc)
                })
                .ToList();

            return Results.Json(normalized);
        });

        group.MapGet("/{domainId:guid}", async (
            Guid domainId,
            DomainRegistryService registry,
            MemDbContext db,
            CancellationToken cancellationToken) =>
        {
            await registry.SyncStorageAsync(cancellationToken);

            var domain = await db.Domains
                .Include(x => x.Certificates)
                .FirstOrDefaultAsync(x => x.Id == domainId, cancellationToken);

            return domain is null
                ? Results.Json(new { message = $"Domain '{domainId}' was not found." }, statusCode: StatusCodes.Status404NotFound)
                : Results.Json(ToDetail(domain));
        });

        group.MapPost("", async (
            CreateOperatorDomainRequest request,
            MemDbContext db,
            CancellationToken cancellationToken) =>
        {
            var baseDomain = NormalizeBaseDomain(request.BaseDomain);
            if (string.IsNullOrWhiteSpace(baseDomain))
            {
                return Results.Json(new { message = "Base domain is required." }, statusCode: StatusCodes.Status400BadRequest);
            }

            var exists = await db.Domains.AnyAsync(x => x.BaseDomain == baseDomain, cancellationToken);
            if (exists)
            {
                return Results.Json(new { message = $"Domain '{baseDomain}' already exists." }, statusCode: StatusCodes.Status409Conflict);
            }

            var now = DateTime.UtcNow;
            var hasMain = await db.Domains.AnyAsync(x => x.IsMainPlatformDomain, cancellationToken);

            var domain = new DomainEntity
            {
                Id = Guid.NewGuid(),
                BaseDomain = baseDomain,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? baseDomain : request.DisplayName.Trim(),
                Purpose = !hasMain ? "platform-main" : (string.IsNullOrWhiteSpace(request.Purpose) ? "test" : request.Purpose.Trim()),
                IsMainPlatformDomain = !hasMain,
                DnsProvider = string.IsNullOrWhiteSpace(request.DnsProvider) ? "desec" : request.DnsProvider.Trim(),
                DnsZone = string.IsNullOrWhiteSpace(request.DnsZone) ? baseDomain : request.DnsZone.Trim(),
                Status = "Pending",
                Notes = request.Notes,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            db.Domains.Add(domain);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Json(ToDetail(domain), statusCode: StatusCodes.Status201Created);
        });

        group.MapPut("/{domainId:guid}", async (
            Guid domainId,
            UpdateOperatorDomainRequest request,
            MemDbContext db,
            CancellationToken cancellationToken) =>
        {
            var domain = await db.Domains
                .Include(x => x.Certificates)
                .FirstOrDefaultAsync(x => x.Id == domainId, cancellationToken);

            if (domain is null)
            {
                return Results.Json(new { message = $"Domain '{domainId}' was not found." }, statusCode: StatusCodes.Status404NotFound);
            }

            if (!string.IsNullOrWhiteSpace(request.DisplayName)) domain.DisplayName = request.DisplayName.Trim();
            if (!string.IsNullOrWhiteSpace(request.Purpose) && !domain.IsMainPlatformDomain) domain.Purpose = request.Purpose.Trim();
            if (!string.IsNullOrWhiteSpace(request.Status)) domain.Status = request.Status.Trim();
            domain.Notes = request.Notes;
            domain.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            return Results.Json(ToDetail(domain));
        });

        group.MapDelete("/{domainId:guid}", async (
            Guid domainId,
            [FromQuery] bool force,
            OperatorDomainDeletionService deletion,
            CancellationToken cancellationToken) =>
        {
            var result = await deletion.DeleteAsync(
                domainId,
                force,
                cancellationToken);

            return result.Succeeded
                ? Results.Json(result)
                : Results.Json(
                    result,
                    statusCode: result.Status == "NotFound"
                        ? StatusCodes.Status404NotFound
                        : StatusCodes.Status400BadRequest);
        });

        group.MapPost("/{domainId:guid}/set-main", async (
            Guid domainId,
            MemDbContext db,
            DomainRegistryService registry,
            CancellationToken cancellationToken) =>
        {
            var domain = await db.Domains
                .Include(x => x.ActiveCertificate)
                .Include(x => x.Certificates)
                .FirstOrDefaultAsync(x => x.Id == domainId, cancellationToken);

            if (domain is null)
            {
                return Results.Json(new { message = $"Domain '{domainId}' was not found." }, statusCode: StatusCodes.Status404NotFound);
            }

            var activeCertificate = domain.ActiveCertificate;
            if (activeCertificate is null)
            {
                return Results.Json(
                    new { message = "This domain has no active production certificate to set as the main platform certificate." },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (activeCertificate.IsStaging)
            {
                return Results.Json(
                    new { message = "Let's Encrypt staging certificates cannot be used as the main platform certificate." },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var mainCertificate = await registry.SetMainPlatformCertificateAsync(
                activeCertificate.CertificateId,
                cancellationToken);
            if (mainCertificate is null)
            {
                return Results.Json(
                    new { message = "The active certificate is not eligible to become the main platform certificate." },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var updated = await db.Domains
                .Include(x => x.ActiveCertificate)
                .Include(x => x.Certificates)
                .FirstAsync(x => x.Id == domainId, cancellationToken);

            return Results.Json(ToDetail(updated));
        });

        group.MapGet("/{domainId:guid}/certificates", async (
            Guid domainId,
            DomainRegistryService registry,
            MemDbContext db,
            CancellationToken cancellationToken) =>
        {
            await registry.SyncStorageAsync(cancellationToken);

            if (!await db.Domains.AsNoTracking().AnyAsync(x => x.Id == domainId, cancellationToken))
            {
                return Results.Json(
                    new { message = $"Domain '{domainId}' was not found." },
                    statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Json(await ListCertificateReadModelsAsync(db, domainId, cancellationToken));
        });

        group.MapGet("/{domainId:guid}/certificates/{certificateId}", async (
            Guid domainId,
            string certificateId,
            DomainRegistryService registry,
            MemDbContext db,
            CancellationToken cancellationToken) =>
        {
            await registry.SyncStorageAsync(cancellationToken);
            var certificate = await GetCertificateReadModelAsync(
                db,
                domainId,
                certificateId,
                cancellationToken);

            return certificate is null
                ? Results.Json(
                    new { message = $"Certificate '{certificateId}' was not found for domain '{domainId}'." },
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Json(certificate);
        });

        group.MapPost("/{domainId:guid}/certificates/{certificateId}/set-active", async (
            Guid domainId,
            string certificateId,
            DomainRegistryService registry,
            CancellationToken cancellationToken) =>
        {
            var result = await registry.SetDomainActiveCertificateAsync(
                domainId,
                certificateId,
                cancellationToken);

            return result.Succeeded
                ? Results.Json(result)
                : Results.Json(
                    result,
                    statusCode: string.Equals(result.Status, "NotFound", StringComparison.OrdinalIgnoreCase)
                        ? StatusCodes.Status404NotFound
                        : StatusCodes.Status400BadRequest);
        });


        group.MapDelete("/{domainId:guid}/certificates/{certificateId}", async (
            Guid domainId,
            string certificateId,
            OperatorDomainCertificateDeletionService deletion,
            CancellationToken cancellationToken) =>
        {
            var result = await deletion.DeleteAsync(
                domainId,
                certificateId,
                cancellationToken);

            return result.Succeeded
                ? Results.Json(result)
                : Results.Json(
                    result,
                    statusCode: string.Equals(result.Status, "NotFound", StringComparison.OrdinalIgnoreCase)
                        ? StatusCodes.Status404NotFound
                        : StatusCodes.Status400BadRequest);
        })
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform);


        group.MapGet("/certificates/issuance/active", async (
            DomainCertificateIssueOrchestrator issuance,
            CancellationToken cancellationToken) =>
        {
            var active = await issuance.GetActiveAsync(cancellationToken);
            return Results.Json(new DomainCertificateIssueActiveResponse(active));
        })
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus);

        group.MapGet("/{domainId:guid}/certificates/issuance/latest", async (
            Guid domainId,
            HttpContext httpContext,
            DomainCertificateIssueOrchestrator issuance,
            MemDbContext db,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(httpContext);
            if (!await db.Domains.AsNoTracking().AnyAsync(x => x.Id == domainId, cancellationToken))
            {
                return Results.Json(
                    new { message = $"Domain '{domainId}' was not found." },
                    statusCode: StatusCodes.Status404NotFound);
            }

            var latest = await issuance.GetLatestAsync(domainId, cancellationToken);
            return Results.Json(new DomainCertificateIssueLatestResponse(latest));
        })
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus);

        group.MapGet("/{domainId:guid}/certificates/issuance/{operationId:guid}", async (
            Guid domainId,
            Guid operationId,
            HttpContext httpContext,
            DomainCertificateIssueOrchestrator issuance,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(httpContext);
            var operation = await issuance.GetAsync(domainId, operationId, cancellationToken);
            return operation is null
                ? Results.Json(
                    new { message = $"Certificate issuance operation '{operationId}' was not found for domain '{domainId}'." },
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Json(operation);
        })
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus);

        group.MapPost("/{domainId:guid}/certificates/issuance", async (
            Guid domainId,
            DomainCertificateIssueStartRequest request,
            HttpContext httpContext,
            IAuthorizationService authorization,
            DomainCertificateIssueOrchestrator issuance,
            DomainCertificateIssueWakeSignal wakeSignal,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(httpContext);

            if (!request.UseStaging)
            {
                var stepUp = await RequireRecentStepUpAsync(httpContext, authorization);
                if (stepUp is not null)
                {
                    return stepUp;
                }
            }

            var queued = await issuance.QueueAsync(
                domainId,
                request,
                ResolveOperatorId(httpContext.User),
                cancellationToken);

            if (!queued.Accepted)
            {
                var statusCode = string.Equals(queued.Status, "NotFound", StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status404NotFound
                    : string.Equals(queued.Status, "OperationInProgress", StringComparison.OrdinalIgnoreCase)
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status400BadRequest;
                return Results.Json(queued, statusCode: statusCode);
            }

            if (!string.Equals(queued.Status, "AlreadyCompleted", StringComparison.OrdinalIgnoreCase))
            {
                wakeSignal.Signal();
            }

            return Results.Json(
                queued,
                statusCode: string.Equals(queued.Status, "AlreadyCompleted", StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status202Accepted);
        })
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform);

        group.MapGet("/renewal", async (
            DomainCertificateRenewalProjectionService renewal,
            CancellationToken cancellationToken) =>
        {
            var states = await renewal.ListAsync(cancellationToken);
            return Results.Json(states.Select(ToRenewalResponse).ToList());
        })
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus);

        group.MapGet("/{domainId:guid}/renewal", async (
            Guid domainId,
            DomainCertificateRenewalProjectionService renewal,
            CancellationToken cancellationToken) =>
        {
            var state = await renewal.GetAsync(domainId, cancellationToken);
            return state is null
                ? Results.Json(new { message = $"Domain '{domainId}' was not found." }, statusCode: StatusCodes.Status404NotFound)
                : Results.Json(ToRenewalResponse(state));
        })
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus);

        group.MapGet("/{domainId:guid}/renewal/history", async (
            Guid domainId,
            DomainCertificateRenewalProjectionService renewal,
            MemDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (!await db.Domains.AsNoTracking().AnyAsync(item => item.Id == domainId, cancellationToken))
            {
                return Results.Json(
                    new { message = $"Domain '{domainId}' was not found." },
                    statusCode: StatusCodes.Status404NotFound);
            }

            var history = await renewal.HistoryAsync(domainId, cancellationToken);
            return Results.Json(history.Select(ToRenewalHistoryResponse).ToList());
        })
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus);

        group.MapPost("/{domainId:guid}/renewal/run", async (
            Guid domainId,
            HttpContext httpContext,
            IAuthorizationService authorization,
            DomainCertificateRenewalOrchestrator orchestrator,
            DomainCertificateRenewalProjectionService renewal,
            DomainCertificateRenewalWakeSignal wakeSignal,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(httpContext);
            var stepUp = await RequireRecentStepUpAsync(httpContext, authorization);
            if (stepUp is not null)
            {
                return stepUp;
            }

            var queued = await orchestrator.QueueManualRenewalAsync(
                domainId,
                ResolveOperatorId(httpContext.User),
                cancellationToken);

            var projection = await renewal.GetAsync(domainId, cancellationToken);
            var response = new OperatorDomainRenewalRunResponse(
                queued.Accepted,
                queued.Status,
                queued.Message,
                queued.OperationId,
                projection is null ? null : ToRenewalResponse(projection));

            if (!queued.Accepted)
            {
                return Results.Json(
                    response,
                    statusCode: string.Equals(queued.Status, "NotFound", StringComparison.OrdinalIgnoreCase)
                        ? StatusCodes.Status404NotFound
                        : StatusCodes.Status409Conflict);
            }

            wakeSignal.Signal();
            return Results.Json(response, statusCode: StatusCodes.Status202Accepted);
        })
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform);

        group.MapPut("/{domainId:guid}/renewal/policy", async (
            Guid domainId,
            UpdateOperatorDomainRenewalPolicyRequest request,
            HttpContext httpContext,
            IAuthorizationService authorization,
            DomainCertificateRenewalService renewal,
            DomainCertificateRenewalProjectionService projection,
            DomainCertificateRenewalWakeSignal wakeSignal,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(httpContext);
            var stepUp = await RequireRecentStepUpAsync(httpContext, authorization);
            if (stepUp is not null)
            {
                return stepUp;
            }

            try
            {
                var state = await renewal.UpdatePolicyAsync(
                    domainId,
                    request.AutoRenewEnabled,
                    request.AcmeEmail,
                    cancellationToken);

                if (state is null)
                {
                    return Results.Json(
                        new { message = $"Domain '{domainId}' was not found." },
                        statusCode: StatusCodes.Status404NotFound);
                }

                wakeSignal.Signal();
                var projected = await projection.GetAsync(domainId, cancellationToken);
                return Results.Json(projected is null
                    ? ToRenewalResponse(state)
                    : ToRenewalResponse(projected));
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "Renewal policy validation failed",
                    type: "https://mem.invalid/problems/domains.renewal.policy_invalid",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "validation_failed",
                        ["error"] = "validation_failed"
                    });
            }
        })
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform);

        group.MapPut("/{domainId:guid}/renewal/credential", async (
            Guid domainId,
            UpdateOperatorDomainRenewalCredentialRequest request,
            HttpContext httpContext,
            IAuthorizationService authorization,
            DomainCertificateRenewalService renewal,
            DomainCertificateRenewalProjectionService projection,
            DomainCertificateRenewalWakeSignal wakeSignal,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(httpContext);
            var stepUp = await RequireRecentStepUpAsync(httpContext, authorization);
            if (stepUp is not null)
            {
                return stepUp;
            }

            var result = await renewal.EnrollOrRotateCredentialAsync(
                domainId,
                request.ProviderToken,
                request.AcmeEmail,
                cancellationToken);

            var projected = result.Succeeded
                ? await projection.GetAsync(domainId, cancellationToken)
                : null;
            var response = new OperatorDomainRenewalCredentialMutationResponse(
                result.Succeeded,
                result.Status,
                result.Message,
                projected is not null
                    ? ToRenewalResponse(projected)
                    : result.State is null
                        ? null
                        : ToRenewalResponse(result.State));

            if (result.Succeeded)
            {
                wakeSignal.Signal();
                return Results.Json(response);
            }

            var statusCode = string.Equals(result.Status, "NotFound", StringComparison.OrdinalIgnoreCase)
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status422UnprocessableEntity;
            return Results.Problem(
                detail: result.Message,
                statusCode: statusCode,
                title: "Renewal credential update failed",
                type: "https://mem.invalid/problems/domains.renewal.credential_invalid",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = result.Status,
                    ["error"] = result.Status
                });
        })
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform);

        group.MapDelete("/{domainId:guid}/renewal/credential", async (
            Guid domainId,
            HttpContext httpContext,
            IAuthorizationService authorization,
            DomainCertificateRenewalService renewal,
            DomainCertificateRenewalProjectionService projection,
            DomainCertificateRenewalWakeSignal wakeSignal,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(httpContext);
            var stepUp = await RequireRecentStepUpAsync(httpContext, authorization);
            if (stepUp is not null)
            {
                return stepUp;
            }

            var state = await renewal.RemoveCredentialAsync(domainId, cancellationToken);
            if (state is null)
            {
                return Results.Json(
                    new { message = $"Domain '{domainId}' was not found." },
                    statusCode: StatusCodes.Status404NotFound);
            }

            wakeSignal.Signal();
            var projected = await projection.GetAsync(domainId, cancellationToken);
            return Results.Json(new OperatorDomainRenewalCredentialMutationResponse(
                true,
                "Removed",
                "The Domain renewal credential was removed. Automatic renewal is no longer ready until a verified credential is enrolled again.",
                projected is null ? ToRenewalResponse(state) : ToRenewalResponse(projected)));
        })
        .RequireAuthorization(MemOperatorPolicies.ManagePlatform);

        group.MapGet("/ingress/npm/status", async (
            NpmReadinessService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Json(await service.GetReadinessAsync(cancellationToken));
        });

        group.MapGet("/certificates", async (
            DomainRegistryService registry,
            MemDbContext db,
            CancellationToken cancellationToken) =>
        {
            await registry.SyncStorageAsync(cancellationToken);
            return Results.Json(await ListCertificateReadModelsAsync(db, null, cancellationToken));
        });

        group.MapGet("/certificates/{certificateId}", async (
            string certificateId,
            DomainRegistryService registry,
            MemDbContext db,
            CancellationToken cancellationToken) =>
        {
            await registry.SyncStorageAsync(cancellationToken);
            var certificate = await GetCertificateReadModelAsync(
                db,
                domainId: null,
                certificateId: certificateId,
                cancellationToken: cancellationToken);

            return certificate is null
                ? Results.Json(
                    new { message = $"Certificate '{certificateId}' was not found." },
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Json(certificate);
        });

        group.MapPost("/certificates/{certificateId}/validate", async (
            string certificateId,
            NpmCertificateImportProbe probe,
            CertificateValidationService validation,
            CancellationToken cancellationToken) =>
        {
            var result = await validation.ValidateAsync(certificateId, cancellationToken);

            return result.Succeeded
                ? Results.Json(result)
                : Results.Json(result, statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapPost("/certificates/{certificateId}/npm/probe", async (
            string certificateId,
            NpmCertificateImportProbe probe,
            CancellationToken cancellationToken) =>
        {
            var result = await probe.ProbeAsync(certificateId, cancellationToken);

            return result.Succeeded
                ? Results.Json(result)
                : Results.Json(result, statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapPost("/certificates/{certificateId}/npm/import", async (
            string certificateId,
            NpmCertificateImportProbe probe,
            CancellationToken cancellationToken) =>
        {
            var result = await probe.ImportAsync(certificateId, cancellationToken);

            return result.Succeeded
                ? Results.Json(result)
                : Results.Json(result, statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapPost("/certificates/{certificateId}/npm/test-proxy-host", async (
            string certificateId,
            NpmCertificateTestProxyHostRequest request,
            NpmCertificateImportProbe probe,
            CancellationToken cancellationToken) =>
        {
            var result = await probe.TestProxyHostAsync(certificateId, request, cancellationToken);

            return result.Succeeded
                ? Results.Json(result)
                : Results.Json(result, statusCode: StatusCodes.Status400BadRequest);
        });
    }


    internal static async Task<IReadOnlyList<OperatorCertificateReadResponse>> ListCertificateReadModelsAsync(
        MemDbContext db,
        Guid? domainId,
        CancellationToken cancellationToken)
    {
        var query = db.Certificates
            .AsNoTracking()
            .Include(x => x.Domain)
            .Where(x => x.Status != "Deleted");

        if (domainId.HasValue)
        {
            query = query.Where(x => x.DomainId == domainId.Value);
        }

        var certificates = await query.ToListAsync(cancellationToken);

        return certificates
            .Select(ToCertificateRead)
            .OrderByDescending(x => x.DomainIsMainPlatformDomain)
            .ThenBy(x => x.DomainBaseDomain, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => x.IsInUse)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    internal static async Task<OperatorCertificateReadResponse?> GetCertificateReadModelAsync(
        MemDbContext db,
        Guid? domainId,
        string certificateId,
        CancellationToken cancellationToken)
    {
        var query = db.Certificates
            .AsNoTracking()
            .Include(x => x.Domain)
            .Where(x => x.Status != "Deleted" && x.CertificateId == certificateId);

        if (domainId.HasValue)
        {
            query = query.Where(x => x.DomainId == domainId.Value);
        }

        var certificate = await query.SingleOrDefaultAsync(cancellationToken);
        return certificate is null ? null : ToCertificateRead(certificate);
    }

    internal static OperatorCertificateReadResponse ToCertificateRead(CertificateEntity certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(certificate.Domain);

        var domain = certificate.Domain;
        var zone = string.IsNullOrWhiteSpace(domain.DnsZone)
            ? domain.BaseDomain
            : domain.DnsZone!;

        return new OperatorCertificateReadResponse(
            certificate.Id,
            certificate.DomainId,
            domain.BaseDomain,
            domain.DisplayName,
            domain.IsMainPlatformDomain,
            domain.ActiveCertificateId,
            domain.DnsProvider,
            domain.DnsZone,
            certificate.CertificateId,
            certificate.CommonName,
            certificate.CommonName, // Legacy global-read alias retained while the current Web transitions.
            zone,                   // Legacy global-read alias retained while the current Web transitions.
            certificate.Provider,
            certificate.IsWildcard,
            certificate.IsStaging,
            certificate.IsMainPlatformCertificate,
            certificate.IsActive,
            domain.ActiveCertificateId == certificate.Id,
            domain.Purpose,
            certificate.Status,
            NormalizeUtc(certificate.CreatedAtUtc),
            NormalizeUtc(certificate.ExpiresAtUtc),
            certificate.Thumbprint,
            certificate.NpmCertificateId,
            certificate.ImportedToNpm,
            NormalizeUtc(certificate.LastValidatedAtUtc),
            NormalizeUtc(certificate.LastImportedToNpmAtUtc),
            certificate.LastError);
    }

    internal static OperatorDomainRenewalResponse ToRenewalResponse(
        DomainCertificateRenewalProjection projection)
    {
        var state = projection.State;
        return new OperatorDomainRenewalResponse(
            state.DomainId,
            state.BaseDomain,
            state.DnsProvider,
            state.DnsZone,
            state.PolicyConfigured,
            state.AutoRenewEnabled,
            state.AcmeEmail,
            state.RenewalWindowDays,
            state.RetryIntervalHours,
            state.CredentialConfigured,
            NormalizeUtc(state.CredentialUpdatedAtUtc),
            state.HasActiveProductionCertificate,
            state.ActiveCertificateId,
            NormalizeUtc(state.ActiveCertificateExpiresAtUtc),
            state.ReadinessStatus,
            state.ReadinessMessage,
            NormalizeUtc(state.PolicyUpdatedAtUtc),
            projection.OperationalStatus,
            projection.CertificateExpired,
            projection.DaysRemaining,
            NormalizeUtc(projection.NextEligibleRenewalAtUtc),
            NormalizeUtc(projection.NextAutomaticAttemptAtUtc),
            NormalizeUtc(projection.LastAttemptAtUtc),
            NormalizeUtc(projection.LastSuccessfulRenewalAtUtc),
            projection.LatestOperationId,
            projection.LatestOperationStatus,
            projection.LatestOperationStep,
            projection.LatestOperationAttemptCount,
            projection.LatestRequestedBy,
            projection.LatestErrorCode,
            projection.DiagnosticsIncidentId,
            projection.DiagnosticsHref,
            projection.ManualRenewAvailable);
    }

    internal static OperatorDomainRenewalHistoryResponse ToRenewalHistoryResponse(
        DomainCertificateRenewalHistoryItem item) =>
        new(
            item.OperationId,
            item.Status,
            item.Step,
            item.RequestedBy,
            NormalizeUtc(item.RequestedAtUtc),
            NormalizeUtc(item.StartedAtUtc),
            NormalizeUtc(item.CompletedAtUtc),
            item.AttemptCount,
            item.ErrorCode,
            item.DiagnosticsIncidentId,
            item.DiagnosticsHref);

    internal static OperatorDomainRenewalResponse ToRenewalResponse(DomainCertificateRenewalState state) =>
        new(
            state.DomainId,
            state.BaseDomain,
            state.DnsProvider,
            state.DnsZone,
            state.PolicyConfigured,
            state.AutoRenewEnabled,
            state.AcmeEmail,
            state.RenewalWindowDays,
            state.RetryIntervalHours,
            state.CredentialConfigured,
            NormalizeUtc(state.CredentialUpdatedAtUtc),
            state.HasActiveProductionCertificate,
            state.ActiveCertificateId,
            NormalizeUtc(state.ActiveCertificateExpiresAtUtc),
            state.ReadinessStatus,
            state.ReadinessMessage,
            NormalizeUtc(state.PolicyUpdatedAtUtc));

    private static async Task<IResult?> RequireRecentStepUpAsync(
        HttpContext httpContext,
        IAuthorizationService authorization)
    {
        var result = await authorization.AuthorizeAsync(
            httpContext.User,
            resource: null,
            policyName: MemOperatorPolicies.RecentStepUp);

        if (result.Succeeded)
        {
            return null;
        }

        SetNoStore(httpContext);
        return Results.Problem(
            detail: "Recent password and authenticator verification is required for this Domain credential operation.",
            statusCode: StatusCodes.Status403Forbidden,
            title: "Recent identity verification required",
            type: "https://mem.invalid/problems/operator.step_up_required",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "step_up_required",
                ["error"] = "step_up_required"
            });
    }

    private static Guid? ResolveOperatorId(ClaimsPrincipal principal) =>
        Guid.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            out var operatorId)
            ? operatorId
            : null;

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }

    private static OperatorDomainDetailResponse ToDetail(DomainEntity domain)
    {
        return new OperatorDomainDetailResponse(
            domain.Id,
            domain.BaseDomain,
            domain.DisplayName,
            domain.Purpose,
            domain.IsMainPlatformDomain,
            domain.DnsProvider,
            domain.DnsZone,
            domain.Status,
            domain.Notes,
            domain.ActiveCertificateId,
            domain.Certificates
                .OrderByDescending(x => x.IsMainPlatformCertificate)
                .ThenByDescending(x => x.CreatedAtUtc)
                .Select(ToCertificate)
                .ToList(),
            NormalizeUtc(domain.CreatedAtUtc),
            NormalizeUtc(domain.UpdatedAtUtc));
    }

    private static OperatorCertificateSummaryResponse ToCertificate(CertificateEntity certificate)
    {
        return new OperatorCertificateSummaryResponse(
            certificate.Id,
            certificate.DomainId,
            certificate.CertificateId,
            certificate.CommonName,
            certificate.Provider,
            certificate.IsWildcard,
            certificate.IsStaging,
            certificate.IsMainPlatformCertificate,
            certificate.IsActive,
            certificate.Status,
            NormalizeUtc(certificate.CreatedAtUtc),
            NormalizeUtc(certificate.ExpiresAtUtc),
            certificate.Thumbprint,
            certificate.NpmCertificateId,
            certificate.ImportedToNpm,
            NormalizeUtc(certificate.LastValidatedAtUtc),
            NormalizeUtc(certificate.LastImportedToNpmAtUtc),
            certificate.LastError);
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue ? NormalizeUtc(value.Value) : null;

    private static string NormalizeBaseDomain(string value)
    {
        return value
            .Trim()
            .ToLowerInvariant()
            .Replace("https://", string.Empty)
            .Replace("http://", string.Empty)
            .Replace("*.", string.Empty)
            .Trim('/')
            .Trim('.');
    }
}