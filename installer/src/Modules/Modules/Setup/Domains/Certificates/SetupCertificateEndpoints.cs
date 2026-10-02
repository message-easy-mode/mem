using Carter;
using Modules.Shared.Domains.Certificates;

namespace Modules.Setup.Domains.Certificates;

public sealed class SetupCertificateEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/setup/domains/certificates")
            .WithTags("Setup Domains");

        // First-time Setup no longer exposes certificate/DNS/NPM mutation endpoints.
        // Certificate issuance is executed only by the reviewed installation pipeline.
        group.MapGet("/", async (
            CertificateStorageService storage,
            CancellationToken cancellationToken) =>
        {
            var certificates = await storage.ListAsync(cancellationToken);
            return Results.Json(certificates);
        });

        group.MapGet("/{certificateId}", async (
            string certificateId,
            CertificateStorageService storage,
            CancellationToken cancellationToken) =>
        {
            var certificate = await storage.GetMetadataAsync(certificateId, cancellationToken);

            return certificate is null
                ? Results.Json(
                    new { message = $"Certificate '{certificateId}' was not found." },
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Json(certificate);
        });
    }
}
