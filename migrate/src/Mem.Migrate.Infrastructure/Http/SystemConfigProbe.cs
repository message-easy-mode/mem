using System.Net;
using System.Text.Json;
using Mem.Migrate.Core.Assessment;

namespace Mem.Migrate.Infrastructure.Http;

public sealed class SystemConfigProbe : ISystemConfigProbe
{
    public async Task<SystemConfigObservation> ProbeAsync(
        AssessmentOptions options,
        DockerInventoryObservation docker,
        CancellationToken cancellationToken)
    {
        var candidates = BuildCandidates(options, docker);

        if (candidates.Length == 0)
        {
            return new SystemConfigObservation(
                Reachable: false,
                Endpoint: null,
                ProductName: null,
                ProductVersion: null,
                ErrorCode: "system_config_endpoint_not_found",
                ErrorMessage: "No safe local MEM API endpoint could be derived.");
        }

        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate
        };

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds)
        };

        SystemConfigObservation? lastFailure = null;

        foreach (var endpoint in candidates)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    endpoint);
                request.Headers.UserAgent.ParseAdd(
                    "mem-migrate/0.1.0-alpha.1");

                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    lastFailure = new SystemConfigObservation(
                        Reachable: false,
                        Endpoint: endpoint,
                        ProductName: null,
                        ProductVersion: null,
                        ErrorCode: "system_config_http_error",
                        ErrorMessage:
                            $"The endpoint returned HTTP {(int)response.StatusCode}.");
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(
                    cancellationToken);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);

                var product = document.RootElement.TryGetProperty(
                    "product",
                    out var productElement)
                    ? productElement
                    : default;

                var name = product.ValueKind is JsonValueKind.Object
                    && product.TryGetProperty("name", out var nameElement)
                    && nameElement.ValueKind is JsonValueKind.String
                        ? nameElement.GetString()
                        : null;

                var version = product.ValueKind is JsonValueKind.Object
                    && product.TryGetProperty("version", out var versionElement)
                    && versionElement.ValueKind is JsonValueKind.String
                        ? versionElement.GetString()
                        : null;

                return new SystemConfigObservation(
                    Reachable: true,
                    Endpoint: endpoint,
                    ProductName: name,
                    ProductVersion: version,
                    ErrorCode: null,
                    ErrorMessage: null);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = new SystemConfigObservation(
                    Reachable: false,
                    Endpoint: endpoint,
                    ProductName: null,
                    ProductVersion: null,
                    ErrorCode: "system_config_timeout",
                    ErrorMessage: "The system configuration request timed out.");
            }
            catch (HttpRequestException)
            {
                lastFailure = new SystemConfigObservation(
                    Reachable: false,
                    Endpoint: endpoint,
                    ProductName: null,
                    ProductVersion: null,
                    ErrorCode: "system_config_unreachable",
                    ErrorMessage: "The system configuration endpoint was unreachable.");
            }
            catch (JsonException)
            {
                lastFailure = new SystemConfigObservation(
                    Reachable: false,
                    Endpoint: endpoint,
                    ProductName: null,
                    ProductVersion: null,
                    ErrorCode: "system_config_invalid_json",
                    ErrorMessage: "The system configuration response was not valid JSON.");
            }
        }

        return lastFailure
            ?? new SystemConfigObservation(
                Reachable: false,
                Endpoint: null,
                ProductName: null,
                ProductVersion: null,
                ErrorCode: "system_config_unreachable",
                ErrorMessage: "No candidate system configuration endpoint was reachable.");
    }

    private static Uri[] BuildCandidates(
        AssessmentOptions options,
        DockerInventoryObservation docker)
    {
        var candidates = new List<Uri>();

        var requestedApiUrl = options.ApiUrl;

        if (!string.IsNullOrWhiteSpace(requestedApiUrl) &&
            Uri.TryCreate(requestedApiUrl, UriKind.Absolute, out var explicitUri) &&
            explicitUri.Scheme is "http" or "https" &&
            string.IsNullOrEmpty(explicitUri.UserInfo))
        {
            candidates.Add(AppendConfigPath(explicitUri));
        }

        foreach (var container in docker.Containers.Where(IsApiCandidate))
        {
            foreach (var port in container.Ports.Where(port =>
                         string.Equals(
                             port.ContainerPort,
                             "7000/tcp",
                             StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrWhiteSpace(port.HostPort)))
            {
                if (Uri.TryCreate(
                    $"http://127.0.0.1:{port.HostPort}/api/system/config",
                    UriKind.Absolute,
                    out var endpoint))
                {
                    candidates.Add(endpoint);
                }
            }
        }

        if (docker.Containers.Any(IsApiCandidate) &&
            Uri.TryCreate(
                "http://127.0.0.1:7000/api/system/config",
                UriKind.Absolute,
                out var defaultEndpoint))
        {
            candidates.Add(defaultEndpoint);
        }

        return candidates
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Uri AppendConfigPath(Uri uri)
    {
        if (uri.AbsolutePath.EndsWith(
                "/api/system/config",
                StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/api/system/config",
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri;
    }

    private static bool IsApiCandidate(DockerContainerObservation container)
    {
        return string.Equals(
                   container.Name,
                   "mem-api",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   container.ComposeService,
                   "api",
                   StringComparison.OrdinalIgnoreCase)
               || container.Image.Contains(
                   "mem-api",
                   StringComparison.OrdinalIgnoreCase)
               || container.Image.Contains(
                   "matrix-easy-mode-api",
                   StringComparison.OrdinalIgnoreCase)
               || container.Ports.Any(port =>
                   string.Equals(
                       port.ContainerPort,
                       "7000/tcp",
                       StringComparison.OrdinalIgnoreCase));
    }
}
