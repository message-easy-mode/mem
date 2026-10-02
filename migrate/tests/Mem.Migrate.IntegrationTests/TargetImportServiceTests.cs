using System.Net;
using System.Text;
using System.Text.Json;
using Mem.Migrate.Core.Security;
using Mem.Migrate.Core.Target;
using Mem.Migrate.Infrastructure.Persistence;
using Mem.Migrate.Infrastructure.Target;

namespace Mem.Migrate.IntegrationTests;

public sealed class TargetImportServiceTests
{
    [Fact]
    public async Task Preview_commit_upload_and_bind_are_orchestrated_and_journaled()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-migrate-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var zipPath = Path.Combine(root, "stack.zip");
            await File.WriteAllBytesAsync(
                zipPath,
                Encoding.UTF8.GetBytes("test-zip"));
            var zipSha = await Sha256File.ComputeAsync(
                zipPath,
                CancellationToken.None);
            var zipBytes = new FileInfo(zipPath).Length;
            var manifestPath = Path.Combine(root, "manifest.json");
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(new
                {
                    contractVersion = "mem-migration-import/v1",
                    sources = new[]
                    {
                        new
                        {
                            sourceId = "source",
                            kind = "capture",
                            product = "MEM",
                            productVersion = "0.1.0",
                            sourceFingerprint = new string('a', 64)
                        }
                    },
                    artifacts = new[]
                    {
                        new
                        {
                            artifactId = "artifact",
                            kind = "mem-stack-export",
                            logicalName = "Stack",
                            targetKind = "backup-catalog-import",
                            targetKey = "stack",
                            sha256 = zipSha,
                            sizeBytes = zipBytes,
                            required = true
                        }
                    }
                }));

            var deviceCredential = CreateDeviceCredential();
            var handler = new RecordingHandler(
                zipSha,
                zipBytes,
                deviceCredential);
            using var client = new HttpClient(handler);
            var journal = new SqliteTargetImportJournal(
                Path.Combine(root, "journal"));
            var profileResolver = new StaticProfileCredentialResolver(
                new TargetProfileCredential(
                    "target-server",
                    "http://target.test",
                    deviceCredential));
            var service = new TargetImportService(
                client,
                journal,
                profileResolver);

            var report = await service.ImportAsync(
                new TargetImportOptions
                {
                    ManifestPath = manifestPath,
                    StackExportPath = zipPath,
                    ProfileName = "target-server",
                    WorkspacePath = Path.Combine(root, "journal"),
                    AttemptId = "mm05c-test"
                },
                CancellationToken.None);

            Assert.Equal("Completed", report.Status);
            Assert.Equal("target-server", report.TargetProfileName);
            Assert.Equal("http://target.test", report.TargetBaseUrl);
            Assert.Equal("mig_test", report.IntakeId);
            Assert.Equal("val_test", report.ValidationId);
            Assert.Equal("bkp_test", report.CatalogEntryId);
            Assert.Equal("bound", report.BindingStatus);
            Assert.Contains("rehearsal-only", report.Warnings.Single());
            Assert.Equal(
                new[]
                {
                    "preview",
                    "get",
                    "commit",
                    "upload",
                    "bind",
                    "validation"
                },
                handler.Calls);

            var stored = await journal.GetAsync(
                "mm05c-test",
                CancellationToken.None);
            Assert.Equal("Completed", stored!.Status);
            Assert.Equal("target-server", stored.TargetProfileName);
            Assert.Equal("http://target.test", stored.TargetBaseUrl);
            Assert.Equal("mig_test", stored.IntakeId);
            Assert.DoesNotContain(
                deviceCredential,
                stored.ReportJson ?? string.Empty,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Resume_rejects_a_different_profile_target_identity()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-migrate-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var zipPath = Path.Combine(root, "stack.zip");
            await File.WriteAllBytesAsync(
                zipPath,
                Encoding.UTF8.GetBytes("test-zip"));
            var zipSha = await Sha256File.ComputeAsync(
                zipPath,
                CancellationToken.None);
            var zipBytes = new FileInfo(zipPath).Length;
            var manifestPath = Path.Combine(root, "manifest.json");
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(new
                {
                    contractVersion = "mem-migration-import/v1",
                    artifacts = new[]
                    {
                        new
                        {
                            artifactId = "artifact",
                            kind = "mem-stack-export",
                            targetKind = "backup-catalog-import",
                            sha256 = zipSha,
                            sizeBytes = zipBytes
                        }
                    }
                }));

            var journal = new SqliteTargetImportJournal(
                Path.Combine(root, "journal"));
            await journal.InitializeAsync(CancellationToken.None);
            await journal.StartAsync(
                "mm05c-test",
                DateTimeOffset.UtcNow,
                await Sha256File.ComputeAsync(
                    manifestPath,
                    CancellationToken.None),
                zipSha,
                "target-one",
                "http://target-one.test",
                CancellationToken.None);

            using var client = new HttpClient(
                new RecordingHandler(
                    zipSha,
                    zipBytes,
                    CreateDeviceCredential()));
            var service = new TargetImportService(
                client,
                journal,
                new StaticProfileCredentialResolver(
                    new TargetProfileCredential(
                        "target-two",
                        "http://target-two.test",
                        CreateDeviceCredential())));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ImportAsync(
                    new TargetImportOptions
                    {
                        ManifestPath = manifestPath,
                        StackExportPath = zipPath,
                        ProfileName = "target-two",
                        WorkspacePath = Path.Combine(root, "journal"),
                        AttemptId = "mm05c-test",
                        Resume = true
                    },
                    CancellationToken.None));

            Assert.Contains(
                "does not match the journaled target identity",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string CreateDeviceCredential() =>
        Convert.ToBase64String(new byte[32])
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class StaticProfileCredentialResolver(
        TargetProfileCredential credential) :
        ITargetProfileCredentialResolver
    {
        public Task<TargetProfileCredential> ResolveAsync(
            string profileName,
            CancellationToken cancellationToken) =>
            Task.FromResult(credential);
    }

    private sealed class RecordingHandler(
        string sha,
        long bytes,
        string expectedCredential) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(
                expectedCredential,
                request.Headers.Authorization?.Parameter);
            var path = request.RequestUri!.AbsolutePath;
            object payload;

            if (path.EndsWith("/preview", StringComparison.Ordinal))
            {
                Calls.Add("preview");
                payload = Intake("previewed");
            }
            else if (path == "/api/operator/migrations/intakes/mig_test" &&
                     request.Method == HttpMethod.Get)
            {
                Calls.Add("get");
                payload = Intake("previewed");
            }
            else if (path.EndsWith("/commit", StringComparison.Ordinal))
            {
                Calls.Add("commit");
                payload = Intake("committed");
            }
            else if (path.EndsWith(
                         "/validated-imports",
                         StringComparison.Ordinal) &&
                     request.Method == HttpMethod.Post)
            {
                Calls.Add("upload");
                payload = new
                {
                    status = "valid",
                    validationId = "val_test",
                    zipBytes = bytes,
                    catalogEntryId = "bkp_test",
                    catalogPayloadState = "available"
                };
            }
            else if (path.EndsWith("/bind-catalog", StringComparison.Ordinal))
            {
                Calls.Add("bind");
                payload = new
                {
                    status = "bound",
                    intakeId = "mig_test",
                    artifactId = "artifact",
                    validationId = "val_test",
                    catalogEntryId = "bkp_test",
                    expectedSha256 = sha,
                    actualSha256 = sha,
                    expectedBytes = bytes,
                    actualBytes = bytes
                };
            }
            else if (path.EndsWith(
                         "/validated-imports/val_test",
                         StringComparison.Ordinal))
            {
                Calls.Add("validation");
                payload = new
                {
                    status = "valid",
                    validationId = "val_test",
                    warnings = new[] { "rehearsal-only" }
                };
            }
            else
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(payload),
                        Encoding.UTF8,
                        "application/json")
                });
        }

        private static object Intake(string status) =>
            new
            {
                intake = new
                {
                    intakeId = "mig_test",
                    status,
                    errorCount = 0
                }
            };
    }
}
