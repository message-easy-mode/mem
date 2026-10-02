using Docker.DotNet;
using Docker.DotNet.Models;
using Modules.Integrations.Npm.Services;

namespace HostAgent.Runtime.Readiness;

public sealed record RuntimeReadinessVerificationRequest(
    string MatrixInternalBaseUrl,
    string ElementInternalBaseUrl,
    string MatrixPublicBaseUrl,
    string ElementPublicBaseUrl,
    string MatrixPublicHost,
    string ElementPublicHost,
    string MatrixForwardHost,
    int MatrixForwardPort,
    string ElementForwardHost,
    int ElementForwardPort,
    int? ExpectedNpmCertificateId);

public sealed record RuntimeReadinessVerificationResult(
    IReadOnlyList<RuntimeReadinessCheckResult> Checks)
{
    public bool AllPassed => Checks.Count > 0 && Checks.All(x => x.Success);
}

public sealed record RuntimeReadinessCheckResult(
    string Code,
    string Name,
    string Url,
    bool Success,
    int? StatusCode,
    string? Detail,
    string? BodyPreview);

public sealed class RuntimeReadinessVerifier
{
    internal const string NpmContainerName = "mem-npm";
    internal const string PublicProbeIp = "127.0.0.1";

    private static readonly TimeSpan MatrixRetryTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ElementRetryTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly DockerClient _docker;
    private readonly NpmReadinessService _npmReadinessService;
    private readonly NpmProxyHostService _npmProxyHostService;

    public RuntimeReadinessVerifier(
        DockerClient docker,
        NpmReadinessService npmReadinessService,
        NpmProxyHostService npmProxyHostService)
    {
        _docker = docker;
        _npmReadinessService = npmReadinessService;
        _npmProxyHostService = npmProxyHostService;
    }

    public async Task<RuntimeReadinessVerificationResult> VerifyAsync(
        RuntimeReadinessVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var checks = new List<RuntimeReadinessCheckResult>();

        checks.Add(await CheckNpmReadyAsync(cancellationToken));

        checks.Add(await CheckProxyHostAsync(
            code: "host-agent.matrix.public-route.configured",
            name: "Matrix NPM proxy host configuration check",
            publicHost: request.MatrixPublicHost,
            expectedForwardHost: request.MatrixForwardHost,
            expectedForwardPort: request.MatrixForwardPort,
            expectedNpmCertificateId: request.ExpectedNpmCertificateId,
            cancellationToken));

        checks.Add(await CheckProxyHostAsync(
            code: "host-agent.element.public-route.configured",
            name: "Element NPM proxy host configuration check",
            publicHost: request.ElementPublicHost,
            expectedForwardHost: request.ElementForwardHost,
            expectedForwardPort: request.ElementForwardPort,
            expectedNpmCertificateId: request.ExpectedNpmCertificateId,
            cancellationToken));

        // The four HTTP checks are independent. Run them concurrently so one failing
        // public route cannot serially consume the Matrix and Element retry budgets.
        // The longest individual budget remains 90 seconds.
        var httpChecks = await Task.WhenAll(
            RetryUntilSuccessAsync(
                description: "Matrix internal HTTP readiness",
                timeout: MatrixRetryTimeout,
                delay: RetryDelay,
                check: attempt => CheckInternalFromNpmAsync(
                    code: "host-agent.matrix.internal-http.reachable",
                    name: "Internal Matrix HTTP check from NPM",
                    url: CombineUrl(request.MatrixInternalBaseUrl, "/_matrix/client/versions"),
                    attempt,
                    cancellationToken),
                cancellationToken),
            RetryUntilSuccessAsync(
                description: "Element internal HTTP readiness",
                timeout: ElementRetryTimeout,
                delay: RetryDelay,
                check: attempt => CheckInternalFromNpmAsync(
                    code: "host-agent.element.internal-http.reachable",
                    name: "Internal Element HTTP check from NPM",
                    url: request.ElementInternalBaseUrl,
                    attempt,
                    cancellationToken),
                cancellationToken),
            RetryUntilSuccessAsync(
                description: "Matrix public HTTPS readiness",
                timeout: MatrixRetryTimeout,
                delay: RetryDelay,
                check: attempt => CheckPublicHttpsFromNpmAsync(
                    code: "host-agent.matrix.public-https.reachable",
                    name: "Public Matrix HTTPS check from NPM",
                    url: CombineUrl(request.MatrixPublicBaseUrl, "/_matrix/client/versions"),
                    attempt,
                    cancellationToken),
                cancellationToken),
            RetryUntilSuccessAsync(
                description: "Element public HTTPS readiness",
                timeout: ElementRetryTimeout,
                delay: RetryDelay,
                check: attempt => CheckPublicHttpsFromNpmAsync(
                    code: "host-agent.element.public-https.reachable",
                    name: "Public Element HTTPS check from NPM",
                    url: request.ElementPublicBaseUrl,
                    attempt,
                    cancellationToken),
                cancellationToken));

        checks.AddRange(httpChecks);

        return new RuntimeReadinessVerificationResult(checks);
    }

    private async Task<RuntimeReadinessCheckResult> CheckNpmReadyAsync(
        CancellationToken cancellationToken)
    {
        const string code = "host-agent.npm.ready";
        const string name = "NPM readiness check";

        try
        {
            var readiness = await _npmReadinessService.GetReadinessAsync(
                cancellationToken);

            var success =
                readiness.ContainerExists &&
                readiness.ContainerRunning &&
                readiness.AdminUiReachable &&
                readiness.Initialized &&
                readiness.ApiAuthenticated &&
                readiness.CertificateApiReachable &&
                string.Equals(
                    readiness.RecommendedAction,
                    "ready",
                    StringComparison.OrdinalIgnoreCase);

            var detail = success
                ? "NPM is ready."
                : string.Join(
                    " ",
                    new[]
                    {
                        $"ContainerExists={readiness.ContainerExists}.",
                        $"ContainerRunning={readiness.ContainerRunning}.",
                        $"AdminUiReachable={readiness.AdminUiReachable}.",
                        $"Initialized={readiness.Initialized}.",
                        $"ApiAuthenticated={readiness.ApiAuthenticated}.",
                        $"CertificateApiReachable={readiness.CertificateApiReachable}.",
                        $"RuntimeState={readiness.RuntimeState ?? "unknown"}.",
                        $"BaseUrl={readiness.BaseUrl ?? "unknown"}.",
                        $"BaseUrlSource={readiness.BaseUrlSource ?? "unknown"}.",
                        $"CertificateCount={readiness.CertificateCount}.",
                        $"RecommendedAction={readiness.RecommendedAction}.",
                        readiness.Warnings.Count > 0
                            ? $"Warnings={string.Join(" | ", readiness.Warnings)}."
                            : "Warnings=none."
                    });

            return new RuntimeReadinessCheckResult(
                Code: code,
                Name: name,
                Url: readiness.BaseUrl ?? "npm://mem-npm",
                Success: success,
                StatusCode: null,
                Detail: detail,
                BodyPreview: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RuntimeReadinessCheckResult(
                Code: code,
                Name: name,
                Url: "npm://mem-npm",
                Success: false,
                StatusCode: null,
                Detail: ex.Message,
                BodyPreview: null);
        }
    }

    private async Task<RuntimeReadinessCheckResult> CheckProxyHostAsync(
        string code,
        string name,
        string publicHost,
        string expectedForwardHost,
        int expectedForwardPort,
        int? expectedNpmCertificateId,
        CancellationToken cancellationToken)
    {
        try
        {
            var proxyHost = await _npmProxyHostService.GetByDomainAsync(
                publicHost,
                cancellationToken);

            if (proxyHost is null)
            {
                return new RuntimeReadinessCheckResult(
                    Code: code,
                    Name: name,
                    Url: $"npm://proxy-hosts/{publicHost}",
                    Success: false,
                    StatusCode: null,
                    Detail: $"No NPM proxy host exists for {publicHost}.",
                    BodyPreview: null);
            }

            var actualForwardHost = proxyHost.forward_host;
            var actualForwardPort = proxyHost.forward_port;
            var actualCertificateId = proxyHost.certificate_id;
            var actualSslForced = proxyHost.ssl_forced == true;
            var actualEnabled = proxyHost.enabled == true;
            var actualNginxOnline = proxyHost.meta?.nginx_online;

            var problems = new List<string>();

            if (!string.Equals(
                    actualForwardHost,
                    expectedForwardHost,
                    StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(
                    $"forward_host expected '{expectedForwardHost}' but was '{actualForwardHost}'.");
            }

            if (actualForwardPort != expectedForwardPort)
            {
                problems.Add(
                    $"forward_port expected '{expectedForwardPort}' but was '{actualForwardPort?.ToString() ?? "null"}'.");
            }

            if (!actualEnabled)
            {
                problems.Add("proxy host is not enabled.");
            }

            if (!actualSslForced)
            {
                problems.Add("ssl_forced is not enabled.");
            }

            if (expectedNpmCertificateId is not null &&
                actualCertificateId != expectedNpmCertificateId.Value)
            {
                problems.Add(
                    $"certificate_id expected '{expectedNpmCertificateId.Value}' but was '{actualCertificateId?.ToString() ?? "null"}'.");
            }

            if (actualNginxOnline == false)
            {
                problems.Add("NPM reports nginx_online=false.");
            }

            var success = problems.Count == 0;

            return new RuntimeReadinessCheckResult(
                Code: code,
                Name: name,
                Url: $"npm://proxy-hosts/{publicHost}",
                Success: success,
                StatusCode: null,
                Detail: success
                    ? "NPM proxy host matches expected runtime route configuration."
                    : string.Join(" ", problems),
                BodyPreview: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RuntimeReadinessCheckResult(
                Code: code,
                Name: name,
                Url: $"npm://proxy-hosts/{publicHost}",
                Success: false,
                StatusCode: null,
                Detail: ex.Message,
                BodyPreview: null);
        }
    }

    private async Task<RuntimeReadinessCheckResult> CheckInternalFromNpmAsync(
        string code,
        string name,
        string url,
        int attempt,
        CancellationToken cancellationToken)
    {
        try
        {
            var exec = await _docker.Exec.ExecCreateContainerAsync(
                NpmContainerName,
                new ContainerExecCreateParameters
                {
                    AttachStdout = true,
                    AttachStderr = true,
                    Cmd =
                    [
                        "curl",
                        "-sS",
                        "--max-time",
                        "10",
                        "-o",
                        "-",
                        "-w",
                        "\nMEM_HTTP_STATUS:%{http_code}",
                        url
                    ]
                },
                cancellationToken);

            using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
                exec.ID,
                tty: false,
                cancellationToken);

            var output = await stream.ReadOutputToEndAsync(cancellationToken);

            var inspect = await _docker.Exec.InspectContainerExecAsync(
                exec.ID,
                cancellationToken);

            var stdout = output.stdout ?? string.Empty;
            var stderr = output.stderr ?? string.Empty;
            var statusCode = TryParseCurlStatus(stdout);
            var body = StripCurlStatus(stdout);

            var success = inspect.ExitCode == 0 &&
                          statusCode is >= 200 and < 400;

            return new RuntimeReadinessCheckResult(
                Code: code,
                Name: name,
                Url: url,
                Success: success,
                StatusCode: statusCode,
                Detail: success
                    ? $"Reachable from mem-npm. attempt={attempt}."
                    : $"curl failed from mem-npm. attempt={attempt}; exitCode={inspect.ExitCode}; stderr={Trim(stderr, 500)}",
                BodyPreview: Trim(body, 500));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RuntimeReadinessCheckResult(
                Code: code,
                Name: name,
                Url: url,
                Success: false,
                StatusCode: null,
                Detail: $"attempt={attempt}; {ex.Message}",
                BodyPreview: null);
        }
    }

    private async Task<RuntimeReadinessCheckResult> CheckPublicHttpsFromNpmAsync(
        string code,
        string name,
        string url,
        int attempt,
        CancellationToken cancellationToken)
    {
        try
        {
            var exec = await _docker.Exec.ExecCreateContainerAsync(
                NpmContainerName,
                new ContainerExecCreateParameters
                {
                    AttachStdout = true,
                    AttachStderr = true,
                    Cmd = BuildPublicHttpsProbeCommand(url)
                },
                cancellationToken);

            using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
                exec.ID,
                tty: false,
                cancellationToken);

            var output = await stream.ReadOutputToEndAsync(cancellationToken);

            var inspect = await _docker.Exec.InspectContainerExecAsync(
                exec.ID,
                cancellationToken);

            var stdout = output.stdout ?? string.Empty;
            var stderr = output.stderr ?? string.Empty;
            var statusCode = TryParseCurlStatus(stdout);
            var body = StripCurlStatus(stdout);

            var success = inspect.ExitCode == 0 &&
                          statusCode is >= 200 and < 400;

            return new RuntimeReadinessCheckResult(
                Code: code,
                Name: name,
                Url: url,
                Success: success,
                StatusCode: statusCode,
                Detail: success
                    ? $"Reachable through the mem-npm HTTPS listener with the public hostname preserved. attempt={attempt}."
                    : $"curl failed through the mem-npm HTTPS listener. attempt={attempt}; exitCode={inspect.ExitCode}; stderr={Trim(stderr, 500)}",
                BodyPreview: Trim(body, 500));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RuntimeReadinessCheckResult(
                Code: code,
                Name: name,
                Url: url,
                Success: false,
                StatusCode: null,
                Detail: $"attempt={attempt}; {ex.Message}",
                BodyPreview: null);
        }
    }

    internal static string[] BuildPublicHttpsProbeCommand(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.IdnHost))
        {
            throw new ArgumentException(
                "Public readiness probes require an absolute HTTPS URL.",
                nameof(url));
        }

        var port = uri.IsDefaultPort ? 443 : uri.Port;
        var resolve = $"{uri.IdnHost}:{port}:{PublicProbeIp}";

        return
        [
            "curl",
            "-k",
            "-sS",
            "--max-time",
            "10",
            "--resolve",
            resolve,
            "-o",
            "-",
            "-w",
            "\nMEM_HTTP_STATUS:%{http_code}",
            url
        ];
    }

    private static async Task<RuntimeReadinessCheckResult> RetryUntilSuccessAsync(
        string description,
        TimeSpan timeout,
        TimeSpan delay,
        Func<int, Task<RuntimeReadinessCheckResult>> check,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        RuntimeReadinessCheckResult? lastResult = null;
        var attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            attempt++;

            lastResult = await check(attempt);

            if (lastResult.Success)
            {
                return lastResult with
                {
                    Detail = AppendRetryDetail(
                        lastResult.Detail,
                        description,
                        attempt,
                        startedAt)
                };
            }

            var elapsed = DateTimeOffset.UtcNow - startedAt;

            if (elapsed >= timeout)
            {
                return lastResult with
                {
                    Detail = AppendRetryDetail(
                        lastResult.Detail,
                        $"{description} timed out",
                        attempt,
                        startedAt)
                };
            }

            var remaining = timeout - elapsed;
            var effectiveDelay = remaining < delay
                ? remaining
                : delay;

            if (effectiveDelay <= TimeSpan.Zero)
            {
                return lastResult with
                {
                    Detail = AppendRetryDetail(
                        lastResult.Detail,
                        $"{description} timed out",
                        attempt,
                        startedAt)
                };
            }

            await Task.Delay(
                effectiveDelay,
                cancellationToken);
        }
    }

    private static string AppendRetryDetail(
        string? detail,
        string description,
        int attempts,
        DateTimeOffset startedAt)
    {
        var elapsed = DateTimeOffset.UtcNow - startedAt;

        var retryDetail =
            $"{description}. attempts={attempts}; elapsedSeconds={Math.Round(elapsed.TotalSeconds, 1)}.";

        if (string.IsNullOrWhiteSpace(detail))
        {
            return retryDetail;
        }

        return $"{detail} {retryDetail}";
    }

    private static string CombineUrl(
        string baseUrl,
        string path)
    {
        return $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    private static int? TryParseCurlStatus(string stdout)
    {
        const string marker = "MEM_HTTP_STATUS:";

        var index = stdout.LastIndexOf(
            marker,
            StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            return null;
        }

        var raw = stdout[(index + marker.Length)..].Trim();

        return int.TryParse(raw, out var statusCode)
            ? statusCode
            : null;
    }

    private static string StripCurlStatus(string stdout)
    {
        const string marker = "MEM_HTTP_STATUS:";

        var index = stdout.LastIndexOf(
            marker,
            StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            return stdout;
        }

        return stdout[..index].Trim();
    }

    private static string? Trim(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }
}