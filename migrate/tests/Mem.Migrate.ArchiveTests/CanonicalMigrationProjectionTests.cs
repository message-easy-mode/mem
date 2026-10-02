using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Legacy.V010.Capture;

namespace Mem.Migrate.ArchiveTests;

public sealed class CanonicalMigrationProjectionTests
{
    [Fact]
    public void PublicUrl_removes_the_development_only_18443_transport_override()
    {
        var result = CanonicalMigrationProjection.PublicUrl(
            "https://matrix-example.test:18443/path?ignored=true");

        Assert.Equal("https://matrix-example.test", result);
    }


    [Fact]
    public void PublicUrl_removes_any_source_transport_port()
    {
        var result = CanonicalMigrationProjection.PublicUrl(
            "https://matrix-example.test:8448/path");

        Assert.Equal("https://matrix-example.test", result);
    }

    [Fact]
    public void Stack_projection_excludes_the_legacy_owner_and_canonicalises_its_url()
    {
        var source = new LegacyStackRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "example",
            "Example",
            1,
            null,
            "https://chat-example.test:18443",
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-07-12T22:00:00Z"),
            null);

        var result = CanonicalMigrationProjection.Stack(source);

        Assert.Equal("https://chat-example.test", result.PrimaryUrl);
        Assert.DoesNotContain(
            source.OwnerUserId.ToString("D"),
            System.Text.Json.JsonSerializer.Serialize(result),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Service_projection_does_not_expose_source_host_or_forward_ports()
    {
        var source = new LegacyServiceRecord(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "matrix",
            1,
            "matrixdotorg/synapse:latest",
            "1.0",
            "container-id",
            18008,
            "https://matrix-example.test:18443",
            "example.test",
            "/private/source",
            true,
            "matrix-example.test",
            null,
            Guid.NewGuid(),
            null,
            null,
            "route-public",
            "matrix-example.test",
            null,
            null,
            "127.0.0.1",
            18443,
            DateTimeOffset.Parse("2026-07-12T22:00:00Z"),
            null);

        var result = CanonicalMigrationProjection.Service(source);
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.Equal("matrix-example.test", result.PublicHost);
        Assert.Equal("https://matrix-example.test", result.PublicUrl);
        Assert.DoesNotContain("18443", json, StringComparison.Ordinal);
        Assert.DoesNotContain("18008", json, StringComparison.Ordinal);
        Assert.DoesNotContain("/private/source", json, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicUrl_removes_user_information_paths_queries_and_fragments()
    {
        var result = CanonicalMigrationProjection.PublicUrl(
            "https://user:secret@matrix-example.test/path?q=1#fragment");

        Assert.Equal("https://matrix-example.test", result);
    }
}
