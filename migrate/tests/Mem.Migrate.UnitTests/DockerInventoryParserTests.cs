using Mem.Migrate.Infrastructure.Docker;

namespace Mem.Migrate.UnitTests;

public sealed class DockerInventoryParserTests
{
    [Fact]
    public void Parser_keeps_safe_metadata_and_drops_secret_values()
    {
        const string json =
            """
            [
              {
                "Id": "abc123",
                "Created": "2026-05-07T10:00:00Z",
                "Image": "sha256:image",
                "Name": "/postgres",
                "Config": {
                  "Image": "postgres:16",
                  "Labels": {
                    "com.docker.compose.project": "mem-dev",
                    "mem.serviceKey": "database",
                    "unrelated.secret.label": "should-not-appear"
                  },
                  "Env": [
                    "POSTGRES_USER=postgres",
                    "POSTGRES_DB=mem",
                    "POSTGRES_PASSWORD=do-not-report"
                  ]
                },
                "State": {
                  "Status": "running",
                  "Health": { "Status": "healthy" }
                },
                "HostConfig": {
                  "RestartPolicy": { "Name": "unless-stopped" }
                },
                "NetworkSettings": {
                  "Ports": {
                    "5432/tcp": [
                      { "HostIp": "0.0.0.0", "HostPort": "5432" }
                    ]
                  },
                  "Networks": {
                    "mem-gateway": {
                      "IPAddress": "172.18.0.2",
                      "Aliases": ["postgres"]
                    }
                  }
                },
                "Mounts": [
                  {
                    "Type": "volume",
                    "Name": "mem_postgres_data",
                    "Source": "/var/lib/docker/volumes/mem/_data",
                    "Destination": "/var/lib/postgresql/data",
                    "RW": true
                  }
                ]
              }
            ]
            """;

        var result = DockerInventoryProbe.ParseContainers(json);
        var container = Assert.Single(result);

        Assert.Equal("postgres", container.Name);
        Assert.Equal("mem", container.SafeEnvironment["POSTGRES_DB"]);
        Assert.Contains(
            "POSTGRES_PASSWORD",
            container.EnvironmentNames);
        Assert.DoesNotContain(
            "POSTGRES_PASSWORD",
            container.SafeEnvironment.Keys);
        Assert.DoesNotContain(
            "unrelated.secret.label",
            container.ManagedLabels.Keys);
        Assert.Equal("healthy", container.Health);
        Assert.Single(container.Mounts);
    }
}
