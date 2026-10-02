using System.Net;
using Microsoft.Extensions.Logging;
using Modules.Integrations.Npm.Contracts;

namespace Modules.Integrations.Npm.Services;

public sealed class NpmReadinessService
{
    private readonly NpmRuntimeService _runtimeService;
    private readonly NpmApiBaseUrlResolver _baseUrlResolver;
    private readonly INpmTokenProvider _tokenProvider;
    private readonly NpmApiClient _npmApiClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NpmReadinessService> _logger;

    public NpmReadinessService(
        NpmRuntimeService runtimeService,
        NpmApiBaseUrlResolver baseUrlResolver,
        INpmTokenProvider tokenProvider,
        NpmApiClient npmApiClient,
        IHttpClientFactory httpClientFactory,
        ILogger<NpmReadinessService> logger)
    {
        _runtimeService = runtimeService;
        _baseUrlResolver = baseUrlResolver;
        _tokenProvider = tokenProvider;
        _npmApiClient = npmApiClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<NpmReadinessResponse> GetReadinessAsync(
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        var runtimeStatus = await _runtimeService.GetStatusAsync(cancellationToken);

        warnings.AddRange(runtimeStatus.Warnings);

        NpmApiBaseUrlResolution? resolution = null;

        try
        {
            resolution = await _baseUrlResolver.ResolveAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(resolution.Warning))
            {
                warnings.Add(resolution.Warning);
            }
        }
        catch (Exception ex)
        {
            warnings.Add(ex.Message);
        }

        var baseUrl = resolution?.BaseUrl;
        var adminUiReachable = false;
        var initialized = false;
        var apiAuthenticated = false;
        var certificateApiReachable = false;
        var certificateCount = 0;

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            adminUiReachable = await CheckAdminUiReachableAsync(
                baseUrl,
                cancellationToken);

            try
            {
                var token = await _tokenProvider.GetTokenAsync(
                    baseUrl,
                    cancellationToken);

                initialized = true;
                apiAuthenticated = true;

                var certificates = await _npmApiClient.GetCertificatesAsync(
                    baseUrl,
                    token,
                    cancellationToken);

                certificateApiReachable = true;
                certificateCount = certificates.Count;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                initialized = true;
                apiAuthenticated = false;

                _tokenProvider.Invalidate();

                warnings.Add("NPM rejected the configured API credentials.");
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
            {
                initialized = false;
                apiAuthenticated = false;

                warnings.Add("NPM API did not accept login. NPM may still be in first-run setup state.");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "NPM readiness API probe failed.");

                warnings.Add($"NPM API readiness probe failed: {ex.Message}");
            }
        }

        var recommendedAction = DetermineRecommendedAction(
            runtimeStatus.Exists,
            runtimeStatus.Running,
            adminUiReachable,
            initialized,
            apiAuthenticated,
            certificateApiReachable);

        return new NpmReadinessResponse(
            ContainerExists: runtimeStatus.Exists,
            ContainerRunning: runtimeStatus.Running,
            AdminUiReachable: adminUiReachable,
            Initialized: initialized,
            ApiAuthenticated: apiAuthenticated,
            CertificateApiReachable: certificateApiReachable,
            RuntimeState: runtimeStatus.State,
            BaseUrl: baseUrl,
            BaseUrlSource: resolution?.Source,
            CertificateCount: certificateCount,
            RecommendedAction: recommendedAction,
            Warnings: warnings.Distinct().ToList());
    }

    private async Task<bool> CheckAdminUiReachableAsync(
        string apiBaseUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var adminUrl = ToAdminUiUrl(apiBaseUrl);

            var client = _httpClientFactory.CreateClient("npm-probe");

            using var response = await client.GetAsync(
                adminUrl,
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "NPM admin UI probe failed.");
            return false;
        }
    }

    private static string ToAdminUiUrl(string apiBaseUrl)
    {
        var value = apiBaseUrl.Trim().TrimEnd('/');

        if (value.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            return value[..^4];
        }

        return value;
    }

    private static string DetermineRecommendedAction(
        bool containerExists,
        bool containerRunning,
        bool adminUiReachable,
        bool initialized,
        bool apiAuthenticated,
        bool certificateApiReachable)
    {
        if (!containerExists)
        {
            return "install-npm";
        }

        if (!containerRunning)
        {
            return "start-npm";
        }

        if (!adminUiReachable)
        {
            return "check-npm-admin-ui";
        }

        if (!initialized)
        {
            return "open-npm-and-create-admin-account";
        }

        if (!apiAuthenticated)
        {
            return "check-npm-api-credentials";
        }

        if (!certificateApiReachable)
        {
            return "check-npm-certificate-api";
        }

        return "ready";
    }
}