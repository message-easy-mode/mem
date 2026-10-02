using System.Text.Json;

namespace HostAgent.Element.Provisioning;

public sealed class ElementConfigGenerator
{
    public async Task WriteHostConfigAsync(
        string path,
        string homeserverBaseUrl,
        string homeserverServerName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("path required", nameof(path));

        if (string.IsNullOrWhiteSpace(homeserverBaseUrl))
            throw new ArgumentException("homeserverBaseUrl required", nameof(homeserverBaseUrl));

        if (string.IsNullOrWhiteSpace(homeserverServerName))
            throw new ArgumentException("homeserverServerName required", nameof(homeserverServerName));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var payload = new Dictionary<string, object?>
        {
            ["default_server_name"] = homeserverServerName.Trim(),
            ["default_server_config"] = new Dictionary<string, object?>
            {
                ["m.homeserver"] = new Dictionary<string, object?>
                {
                    ["base_url"] = homeserverBaseUrl.Trim(),
                    ["server_name"] = homeserverServerName.Trim()
                }
            },
            ["disable_custom_urls"] = true,
            ["disable_guests"] = true
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(path, json, ct);
    }
}
