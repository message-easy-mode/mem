using System.Text;
using System.Text.Json;
using Carter;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Modules.Auth;
using Modules.Auth.Identity;
using Modules.Operator.Diagnostics.Services;
using Shared.Exceptions;

namespace Modules.Setup.SupportReports;

public sealed class SetupInstallationSupportReportEndpoints : ICarterModule
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/setup")
            .WithTags("Setup Support Report")
            .RequireAuthorization(new AuthorizeAttribute
            {
                // Support reports are available during transitional Setup and,
                // after the installer-unlock cookie is revoked, to a named Platform Owner.
                // The default authentication scheme remains InstallerAuth, so both
                // browser cookie schemes must be named explicitly here.
                AuthenticationSchemes = string.Join(
                    ",",
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    IdentityConstants.ApplicationScheme)
            });

        group.MapPost("/installations/{installationId:guid}/support-report", async (
            Guid installationId,
            HttpContext context,
            DiagnosticsApiOptions options,
            SetupInstallationSupportReportService service,
            SetupInstallationSupportReportFormatter formatter,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            EnsureSetupReportAuthority(context);
            var request = await ReadRequestAsync(
                context.Request,
                options.SupportReportMaximumRequestBytes,
                cancellationToken);
            var report = await service.GenerateAsync(
                installationId,
                request,
                cancellationToken);
            return Download(context, report, request.Format, formatter);
        })
        .WithName("GenerateSetupInstallationSupportReport")
        .Accepts<SetupInstallationSupportReportRequest>("application/json")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status413PayloadTooLarge);

        group.MapPost("/support-report", async (
            HttpContext context,
            DiagnosticsApiOptions options,
            SetupInstallationSupportReportService service,
            SetupInstallationSupportReportFormatter formatter,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(context);
            EnsureSetupReportAuthority(context);
            var request = await ReadRequestAsync(
                context.Request,
                options.SupportReportMaximumRequestBytes,
                cancellationToken);
            var report = await service.GenerateAsync(
                installationId: null,
                request,
                cancellationToken);
            return Download(context, report, request.Format, formatter);
        })
        .WithName("GenerateCurrentSetupSupportReport")
        .Accepts<SetupInstallationSupportReportRequest>("application/json")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status413PayloadTooLarge);
    }

    private static IResult Download(
        HttpContext context,
        SetupInstallationSupportReport report,
        string? format,
        SetupInstallationSupportReportFormatter formatter)
    {
        var document = formatter.Format(report, format);
        var timestamp = report.GeneratedAtUtc.ToUniversalTime()
            .ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var identity = report.Setup?.InstallationId.ToString() ??
                       report.Selection.TraceId ??
                       "setup";
        var fileSafeIdentity = new string(identity
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Take(80)
            .ToArray());
        if (string.IsNullOrWhiteSpace(fileSafeIdentity))
        {
            fileSafeIdentity = "setup";
        }

        context.Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"mem-install-report-{fileSafeIdentity}-{timestamp}.{document.FileExtension}\"";
        return Results.Text(
            document.Content,
            document.ContentType,
            Encoding.UTF8,
            StatusCodes.Status200OK);
    }

    private static void EnsureSetupReportAuthority(HttpContext context)
    {
        var user = context.User;
        var transitionalSetup = user.HasClaim(
            InstallerAuthClaims.TransitionalInstallerUnlocked,
            InstallerAuthClaims.True);
        var platformOwner = user.IsInRole(MemOperatorRoles.PlatformOwner);
        if (transitionalSetup || platformOwner)
        {
            return;
        }

        throw new MemProblemException(
            StatusCodes.Status403Forbidden,
            "setup_support_report_forbidden",
            "The installation support report is restricted",
            "A current Setup authority or Platform Owner session is required to generate this report.",
            createIncident: false,
            retryable: false,
            feature: "setup",
            stage: "support-report");
    }

    private static async Task<SetupInstallationSupportReportRequest> ReadRequestAsync(
        HttpRequest request,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > maximumBytes)
        {
            throw RequestTooLarge();
        }

        var buffer = new byte[maximumBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await request.Body.ReadAsync(
                buffer.AsMemory(total, buffer.Length - total),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        if (total > maximumBytes)
        {
            throw RequestTooLarge();
        }

        if (total == 0)
        {
            return new SetupInstallationSupportReportRequest();
        }

        try
        {
            return JsonSerializer.Deserialize<SetupInstallationSupportReportRequest>(
                       buffer.AsSpan(0, total),
                       JsonOptions)
                   ?? new SetupInstallationSupportReportRequest();
        }
        catch (JsonException exception)
        {
            throw new MemProblemException(
                StatusCodes.Status400BadRequest,
                "setup_support_report_request_invalid",
                "The installation support report request is invalid",
                "Provide a valid bounded JSON request.",
                createIncident: false,
                retryable: false,
                feature: "setup",
                stage: "support-report",
                innerException: exception);
        }
    }

    private static MemProblemException RequestTooLarge() =>
        new(
            StatusCodes.Status413PayloadTooLarge,
            "setup_support_report_request_too_large",
            "The installation support report request is too large",
            "The request exceeded the configured support-report request-size limit.",
            createIncident: false,
            retryable: false,
            feature: "setup",
            stage: "support-report");

    private static void SetNoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }
}
