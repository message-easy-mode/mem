using Mem.Migrate.Infrastructure.FileSystem;

namespace Mem.Migrate.UnitTests;

public sealed class HomeserverConfigurationParserTests
{
    [Fact]
    public void Parses_legacy_sqlite_configuration()
    {
        var directory = Directory.CreateTempSubdirectory(
            "mem-migrate-homeserver-");

        try
        {
            var path = Path.Combine(directory.FullName, "homeserver.yaml");
            File.WriteAllText(
                path,
                """
                server_name: "matrix.example.test"
                media_store_path: /data/media_store
                signing_key_path: "/data/matrix.example.test.signing.key"
                database:
                  name: sqlite3
                  args:
                    database: /data/homeserver.db
                """);

            var result = HomeserverConfigurationParser.Parse(
                path,
                1024 * 1024);

            Assert.True(result.Parsed);
            Assert.Equal("matrix.example.test", result.ServerName);
            Assert.Equal("sqlite3", result.DatabaseEngine);
            Assert.Equal("/data/homeserver.db", result.DatabasePath);
            Assert.Equal("/data/media_store", result.MediaStorePath);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Rejects_oversized_configuration()
    {
        var directory = Directory.CreateTempSubdirectory(
            "mem-migrate-homeserver-");

        try
        {
            var path = Path.Combine(directory.FullName, "homeserver.yaml");
            File.WriteAllText(path, new string('x', 2048));

            var result = HomeserverConfigurationParser.Parse(path, 1024);

            Assert.False(result.Parsed);
            Assert.Equal(
                "homeserver_configuration_too_large",
                result.ErrorCode);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
