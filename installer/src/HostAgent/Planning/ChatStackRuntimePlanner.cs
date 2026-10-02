using Core.RuntimeDefinition;
using HostAgent.Options;
using HostAgent.Runtime.ServiceRuntime;
using Microsoft.Extensions.Options;

namespace HostAgent.Planning;

public sealed record ChatStackRuntimePlan(
    string RuntimeNetworkName,
    string MatrixContainerName,
    string MatrixInternalHost,
    string MatrixInternalBaseUrl,
    string MatrixDataPath,
    string? ElementContainerName,
    string? ElementInternalHost,
    string? ElementInternalBaseUrl,
    string? ElementDataPath);

public sealed class ChatStackRuntimePlanner
{
    private readonly InstanceStorageOptions _storageOptions;
    private readonly RuntimeNetworkOptions _networkOptions;

    public ChatStackRuntimePlanner(
        IOptions<InstanceStorageOptions> storageOptions,
        IOptions<RuntimeNetworkOptions> networkOptions)
    {
        _storageOptions = storageOptions.Value;
        _networkOptions = networkOptions.Value;
    }

    public ChatStackRuntimePlan Plan(
    Guid stackId,
    Guid matrixInstanceId,
    Guid? elementInstanceId,
    string? stackSlug)
    {
        var token = BuildSafeToken(stackId, stackSlug);

        var rootPath = _storageOptions
            .ResolveRequiredRoot()
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var networkName = string.IsNullOrWhiteSpace(_networkOptions.GatewayNetworkName)
            ? ManagedNetworkNames.MemGateway
            : _networkOptions.GatewayNetworkName.Trim();

        var matrixContainerName = $"mem-matrix-{token}";
        var matrixInternalHost = matrixContainerName;
        var matrixDataPath = $"{rootPath}/{stackId:N}/matrix-{matrixInstanceId:N}";

        string? elementContainerName = null;
        string? elementInternalHost = null;
        string? elementDataPath = null;

        if (elementInstanceId.HasValue)
        {
            elementContainerName = $"mem-element-{token}";
            elementInternalHost = elementContainerName;
            elementDataPath = $"{rootPath}/{stackId:N}/element-{elementInstanceId.Value:N}";
        }

        return new ChatStackRuntimePlan(
            RuntimeNetworkName: networkName,
            MatrixContainerName: matrixContainerName,
            MatrixInternalHost: matrixInternalHost,
            MatrixInternalBaseUrl: $"http://{matrixInternalHost}:8008",
            MatrixDataPath: matrixDataPath,
            ElementContainerName: elementContainerName,
            ElementInternalHost: elementInternalHost,
            ElementInternalBaseUrl: elementInternalHost is null ? null : $"http://{elementInternalHost}:80",
            ElementDataPath: elementDataPath);
    }

    private static string BuildSafeToken(Guid stackId, string? stackSlug)
    {
        if (!string.IsNullOrWhiteSpace(stackSlug))
        {
            var normalized = NormalizeSlug(stackSlug);

            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return stackId.ToString("N")[..8];
    }

    private static string NormalizeSlug(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
            .ToArray();

        var normalized = new string(chars);

        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized.Trim('-');
    }
}