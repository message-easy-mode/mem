using Api.IntegrationTests.Runtime;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Setup;

public sealed class NpmCertificateRuntimeAuthorityTests
{
    [Fact]
    public async Task STARTUP_INSTALL_REL_01E_CORR_05_containerized_certificate_import_resolves_NPM_through_Docker_network_without_configured_base_url()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-startup-install-rel-01e-corr-05-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var runtimeContext = TestRuntimeContext.Create(
            root,
            mode: MemRuntimeModes.ContainerizedDevelopment,
            runningInContainer: true,
            containerName: "mem-control-plane-dev",
            uiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa);

        var resolver = new NpmApiBaseUrlResolver(
            db,
            Options.Create(new NpmApiOptions()),
            NullLogger<NpmApiBaseUrlResolver>.Instance,
            new MemManagedServiceAuthorityResolver(runtimeContext));

        try
        {
            var resolution = await resolver.ResolveAsync(CancellationToken.None);

            Assert.Equal("http://npm:81/api", resolution.BaseUrl);
            Assert.Equal(MemManagedServiceRouteKinds.DockerNetwork, resolution.Source);
            Assert.False(resolution.RuntimeRecordFound);
            Assert.Contains("Docker-network", resolution.Warning ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup only.
            }
        }
    }
}
