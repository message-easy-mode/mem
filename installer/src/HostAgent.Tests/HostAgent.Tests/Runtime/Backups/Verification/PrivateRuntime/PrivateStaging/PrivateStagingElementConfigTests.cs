using System.Text.Json.Nodes;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;

namespace HostAgent.Tests.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;

public sealed class PrivateStagingElementConfigTests
{
    [Fact]
    public void Patches_element_config_for_private_synapse_and_removes_legacy_public_overrides()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"mem-element-config-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "default_hs_url": "https://old-matrix.example.test",
                  "default_is_url": "https://identity.example.test",
                  "default_server_config": {
                    "m.homeserver": {
                      "base_url": "https://old-matrix.example.test",
                      "server_name": "old.example.test"
                    }
                  },
                  "disable_custom_urls": false
                }
                """);

            PrivateStagingService.PatchElementConfigForPrivateStaging(
                path,
                "matrix.example.test",
                "http://restore-staging-synapse:8008");

            var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var homeserver = root["default_server_config"]!["m.homeserver"]!.AsObject();

            Assert.Equal(
                "http://restore-staging-synapse:8008",
                homeserver["base_url"]!.GetValue<string>());
            Assert.Equal(
                "matrix.example.test",
                homeserver["server_name"]!.GetValue<string>());
            Assert.Equal(
                "matrix.example.test",
                root["default_server_name"]!.GetValue<string>());
            Assert.True(root["disable_custom_urls"]!.GetValue<bool>());
            Assert.False(root.ContainsKey("default_hs_url"));
            Assert.False(root.ContainsKey("default_is_url"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
