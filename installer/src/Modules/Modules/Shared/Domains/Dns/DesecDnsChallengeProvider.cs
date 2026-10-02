using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modules.Shared.Domains.Certificates;

namespace Modules.Shared.Domains.Dns;

public sealed class DesecDnsChallengeProvider : IDnsChallengeProvider, IDnsZoneAccessProbe
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DesecDnsChallengeProvider> _logger;
    private readonly IOptions<AcmeCertificateOptions> _options;

    public string ProviderName => "desec";

    public DesecDnsChallengeProvider(
        HttpClient httpClient,
        ILogger<DesecDnsChallengeProvider> logger,
        IOptions<AcmeCertificateOptions>? options = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options ?? Options.Create(new AcmeCertificateOptions());
    }

    public async Task<DnsChallengeResult> UpsertTxtChallengeAsync(
        DnsChallengeRequest request,
        CancellationToken cancellationToken)
    {
        var evidence = new List<CertificateOperationEvidence>
        {
            new("provider", ProviderName),
            new("zone", request.Zone),
            new("recordName", request.RecordName),
            new("txtValue", "created, masked", Sensitive: true)
        };

        try
        {
            var token = request.ProviderToken?.Trim();

            if (string.IsNullOrWhiteSpace(token))
            {
                return new DnsChallengeResult(
                    Succeeded: false,
                    Message: "deSEC provider token is required.",
                    ErrorCode: "DesecTokenMissing",
                    Evidence: evidence);
            }

            var subname = request.RecordName;

            using var getRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"domains/{request.Zone}/rrsets/{subname}/TXT/");

            getRequest.Headers.TryAddWithoutValidation("Authorization", $"Token {token}");

            var getResponse = await SendChallengeRequestAsync(getRequest, cancellationToken);

            // deSEC enforces a per-zone minimum TTL. Keep the established 3600s
            // value here rather than guessing a lower TTL that the provider may reject;
            // CORR-03 hardens readiness by replacing the RRset directly and observing
            // authoritative/public visibility before ACME validation.
            var payload = new DesecRrsetRequest(
                Subname: subname,
                Type: "TXT",
                Ttl: 3600,
                Records: [$"\"{request.TxtValue}\""]);

            if (getResponse.StatusCode == HttpStatusCode.OK)
            {
                using var putRequest = new HttpRequestMessage(
                    HttpMethod.Put,
                    $"domains/{request.Zone}/rrsets/{subname}/TXT/")
                {
                    Content = JsonContent.Create(payload)
                };

                putRequest.Headers.TryAddWithoutValidation("Authorization", $"Token {token}");

                var putResponse = await SendChallengeRequestAsync(putRequest, cancellationToken);

                if (!putResponse.IsSuccessStatusCode)
                {
                    var body = await putResponse.Content.ReadAsStringAsync(cancellationToken);

                    var classified = ClassifyDesecFailure(
                        putResponse.StatusCode,
                        body,
                        operation: "TXT record update");

                    return new DnsChallengeResult(
                        Succeeded: false,
                        Message: classified.Message,
                        ErrorCode: classified.ErrorCode,
                        Evidence: evidence.Append(new CertificateOperationEvidence(
                            Key: "desecResponse",
                            Value: MaskResponse(body),
                            Sensitive: false,
                            Status: "Failed")).ToList());
                }

                return new DnsChallengeResult(
                    Succeeded: true,
                    Message: "Existing deSEC TXT challenge record updated.",
                    ErrorCode: null,
                    Evidence: evidence.Append(new CertificateOperationEvidence(
                        Key: "desecTxtRecord",
                        Value: "updated",
                        Status: "Succeeded")).ToList());
            }

            if (getResponse.StatusCode == HttpStatusCode.NotFound)
            {
                using var postRequest = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"domains/{request.Zone}/rrsets/")
                {
                    Content = JsonContent.Create(payload)
                };

                postRequest.Headers.TryAddWithoutValidation("Authorization", $"Token {token}");

                var postResponse = await SendChallengeRequestAsync(postRequest, cancellationToken);

                if (!postResponse.IsSuccessStatusCode)
                {
                    var body = await postResponse.Content.ReadAsStringAsync(cancellationToken);

                    var classified = ClassifyDesecFailure(
                        postResponse.StatusCode,
                        body,
                        operation: "TXT record creation");

                    return new DnsChallengeResult(
                        Succeeded: false,
                        Message: classified.Message,
                        ErrorCode: classified.ErrorCode,
                        Evidence: evidence.Append(new CertificateOperationEvidence(
                            Key: "desecResponse",
                            Value: MaskResponse(body),
                            Sensitive: false,
                            Status: "Failed")).ToList());
                }

                return new DnsChallengeResult(
                    Succeeded: true,
                    Message: "deSEC TXT challenge record created.",
                    ErrorCode: null,
                    Evidence: evidence.Append(new CertificateOperationEvidence(
                        Key: "desecTxtRecord",
                        Value: "created",
                        Status: "Succeeded")).ToList());
            }

            var unexpectedBody = await getResponse.Content.ReadAsStringAsync(cancellationToken);

            var unexpected = ClassifyDesecFailure(
                getResponse.StatusCode,
                unexpectedBody,
                operation: "TXT record lookup");

            return new DnsChallengeResult(
                Succeeded: false,
                Message: unexpected.Message,
                ErrorCode: unexpected.ErrorCode,
                Evidence: evidence.Append(new CertificateOperationEvidence(
                    Key: "desecResponse",
                    Value: MaskResponse(unexpectedBody),
                    Status: "Failed")).ToList());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DnsProviderRequestTimeoutException)
        {
            return new DnsChallengeResult(
                Succeeded: false,
                Message: "The deSEC DNS challenge request timed out.",
                ErrorCode: "DesecRequestTimedOut",
                Evidence: evidence.Append(new CertificateOperationEvidence(
                    Key: "desecRequest",
                    Value: "timed out",
                    Status: "Failed")).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create or update deSEC TXT challenge record {RecordName} in zone {Zone}",
                request.RecordName,
                request.Zone);

            return new DnsChallengeResult(
                Succeeded: false,
                Message: "Failed to create or update deSEC TXT challenge record.",
                ErrorCode: "DesecTxtUpsertException",
                Evidence: evidence.Append(new CertificateOperationEvidence(
                    Key: "exception",
                    Value: ex.Message,
                    Status: "Failed")).ToList());
        }
    }

    public async Task<DnsZoneAccessProbeResult> ProbeAsync(
        DnsZoneAccessProbeRequest request,
        CancellationToken cancellationToken)
    {
        const string operation = "read-only-zone-access";
        var evidence = new List<CertificateOperationEvidence>
        {
            new("provider", ProviderName),
            new("zone", request.Zone),
            new("operation", "read-only RRset inventory probe")
        };

        var token = request.ProviderToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return new DnsZoneAccessProbeResult(
                false,
                "deSEC provider token is required.",
                "DesecTokenMissing",
                evidence);
        }

        var startedAt = Stopwatch.GetTimestamp();
        _logger.LogInformation(
            "deSEC domain provider validation started. Provider={Provider} Zone={Zone} Operation={Operation}",
            ProviderName,
            request.Zone,
            operation);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            using var probeRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"domains/{request.Zone}/rrsets/");

            probeRequest.Headers.TryAddWithoutValidation("Authorization", $"Token {token}");

            using var response = await _httpClient.SendAsync(
                probeRequest,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            var durationMs = Math.Round(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, 1);
            var providerStatusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "deSEC domain provider validation completed. Provider={Provider} Zone={Zone} Operation={Operation} Result={Result} HttpStatus={HttpStatus} DurationMs={DurationMs}",
                    ProviderName,
                    request.Zone,
                    operation,
                    "Succeeded",
                    providerStatusCode,
                    durationMs);

                return new DnsZoneAccessProbeResult(
                    true,
                    "deSEC accepted the token and the requested DNS zone is readable.",
                    null,
                    evidence.Append(new CertificateOperationEvidence(
                        "desecZoneAccess",
                        "read-only access confirmed",
                        Status: "Succeeded")).ToArray(),
                    providerStatusCode);
            }

            var classified = ClassifyDesecZoneProbeFailure(
                response.StatusCode,
                request.Zone);

            _logger.LogWarning(
                "deSEC domain provider validation completed. Provider={Provider} Zone={Zone} Operation={Operation} Result={Result} HttpStatus={HttpStatus} FailureClass={FailureClass} DurationMs={DurationMs}",
                ProviderName,
                request.Zone,
                operation,
                "Failed",
                providerStatusCode,
                classified.ErrorCode,
                durationMs);

            return new DnsZoneAccessProbeResult(
                false,
                classified.Message,
                classified.ErrorCode,
                evidence.Append(new CertificateOperationEvidence(
                    "desecResponse",
                    $"{providerStatusCode} {response.StatusCode}",
                    Status: "Failed")).ToArray(),
                providerStatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var durationMs = Math.Round(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, 1);
            _logger.LogWarning(
                "deSEC domain provider validation completed. Provider={Provider} Zone={Zone} Operation={Operation} Result={Result} FailureClass={FailureClass} DurationMs={DurationMs}",
                ProviderName,
                request.Zone,
                operation,
                "Failed",
                "DesecProbeTimedOut",
                durationMs);

            return new DnsZoneAccessProbeResult(
                false,
                "The deSEC read-only validation request timed out.",
                "DesecProbeTimedOut",
                evidence);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var durationMs = Math.Round(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, 1);
            _logger.LogWarning(
                ex,
                "deSEC domain provider validation completed. Provider={Provider} Zone={Zone} Operation={Operation} Result={Result} FailureClass={FailureClass} DurationMs={DurationMs}",
                ProviderName,
                request.Zone,
                operation,
                "Failed",
                "DesecProbeFailed",
                durationMs);

            return new DnsZoneAccessProbeResult(
                false,
                "MEM could not validate read-only access to the deSEC DNS zone.",
                "DesecProbeFailed",
                evidence);
        }
    }

    public async Task<DnsChallengeResult> DeleteTxtChallengeAsync(
        DnsChallengeRequest request,
        CancellationToken cancellationToken)
    {
        var evidence = new List<CertificateOperationEvidence>
        {
            new("provider", ProviderName),
            new("zone", request.Zone),
            new("recordName", request.RecordName)
        };

        try
        {
            var token = request.ProviderToken?.Trim();

            if (string.IsNullOrWhiteSpace(token))
            {
                return new DnsChallengeResult(
                    Succeeded: false,
                    Message: "deSEC provider token is required.",
                    ErrorCode: "DesecTokenMissing",
                    Evidence: evidence);
            }

            using var getRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"domains/{request.Zone}/rrsets/{request.RecordName}/TXT/");

            getRequest.Headers.TryAddWithoutValidation("Authorization", $"Token {token}");

            var getResponse = await SendChallengeRequestAsync(getRequest, cancellationToken);

            if (getResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return new DnsChallengeResult(
                    Succeeded: true,
                    Message: "No existing deSEC TXT challenge record was present.",
                    ErrorCode: null,
                    Evidence: evidence.Append(new CertificateOperationEvidence(
                        Key: "desecTxtRecord",
                        Value: "not found",
                        Status: "Skipped")).ToList());
            }

            if (!getResponse.IsSuccessStatusCode)
            {
                var body = await getResponse.Content.ReadAsStringAsync(cancellationToken);

                var classified = ClassifyDesecFailure(
                    getResponse.StatusCode,
                    body,
                    operation: "TXT record lookup before delete");

                return new DnsChallengeResult(
                    Succeeded: false,
                    Message: classified.Message,
                    ErrorCode: classified.ErrorCode,
                    Evidence: evidence.Append(new CertificateOperationEvidence(
                        Key: "desecResponse",
                        Value: MaskResponse(body),
                        Status: "Failed")).ToList());
            }

            var payload = new DesecRrsetRequest(
                Subname: request.RecordName,
                Type: "TXT",
                Ttl: 3600,
                Records: []);

            using var putRequest = new HttpRequestMessage(
                HttpMethod.Put,
                $"domains/{request.Zone}/rrsets/{request.RecordName}/TXT/")
            {
                Content = JsonContent.Create(payload)
            };

            putRequest.Headers.TryAddWithoutValidation("Authorization", $"Token {token}");

            var putResponse = await SendChallengeRequestAsync(putRequest, cancellationToken);

            if (!putResponse.IsSuccessStatusCode)
            {
                var body = await putResponse.Content.ReadAsStringAsync(cancellationToken);

                var classified = ClassifyDesecFailure(
                    putResponse.StatusCode,
                    body,
                    operation: "TXT record clear");

                return new DnsChallengeResult(
                    Succeeded: false,
                    Message: classified.Message,
                    ErrorCode: classified.ErrorCode,
                    Evidence: evidence.Append(new CertificateOperationEvidence(
                        Key: "desecResponse",
                        Value: MaskResponse(body),
                        Status: "Failed")).ToList());
            }

            return new DnsChallengeResult(
                Succeeded: true,
                Message: "Existing deSEC TXT challenge record cleared.",
                ErrorCode: null,
                Evidence: evidence.Append(new CertificateOperationEvidence(
                    Key: "desecTxtRecord",
                    Value: "cleared",
                    Status: "Succeeded")).ToList());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DnsProviderRequestTimeoutException)
        {
            return new DnsChallengeResult(
                Succeeded: false,
                Message: "The deSEC DNS challenge cleanup request timed out.",
                ErrorCode: "DesecRequestTimedOut",
                Evidence: evidence.Append(new CertificateOperationEvidence(
                    Key: "desecRequest",
                    Value: "timed out",
                    Status: "Failed")).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to clear deSEC TXT challenge record {RecordName} in zone {Zone}",
                request.RecordName,
                request.Zone);

            return new DnsChallengeResult(
                Succeeded: false,
                Message: "Failed to clear deSEC TXT challenge record.",
                ErrorCode: "DesecTxtDeleteException",
                Evidence: evidence.Append(new CertificateOperationEvidence(
                    Key: "exception",
                    Value: ex.Message,
                    Status: "Failed")).ToList());
        }
    }

    private async Task<HttpResponseMessage> SendChallengeRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            Math.Max(5, _options.Value.DnsProviderRequestTimeoutSeconds)));

        try
        {
            // Buffer the small deSEC API response under the same bounded token so
            // a stalled response body cannot outlive the provider request timeout.
            return await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DnsProviderRequestTimeoutException();
        }
    }

    private sealed class DnsProviderRequestTimeoutException : Exception
    {
    }

    private static (string ErrorCode, string Message) ClassifyDesecFailure(
        HttpStatusCode statusCode,
        string responseBody,
        string operation)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => (
                "DesecAuthenticationFailed",
                "deSEC rejected the provider token. Check that the token is correct."),

            HttpStatusCode.Forbidden => (
                "DesecAccessDenied",
                "deSEC denied access to this DNS zone. Check token permissions."),

            HttpStatusCode.NotFound => (
                "DesecZoneOrRecordNotFound",
                "deSEC could not find or access the supplied DNS zone or record. Check the zone name and token access."),

            HttpStatusCode.BadRequest => (
                "DesecBadRequest",
                $"deSEC rejected the TXT record request during {operation}. Check the zone, record name, and payload."),

            _ => (
                "DesecUnexpectedStatus",
                $"deSEC returned an unexpected response during {operation}: {(int)statusCode} {statusCode}.")
        };
    }


    private static (string ErrorCode, string Message) ClassifyDesecZoneProbeFailure(
        HttpStatusCode statusCode,
        string zone)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => (
                "DesecAuthenticationFailed",
                "deSEC rejected the provider token. Check that the token is correct."),

            HttpStatusCode.Forbidden => (
                "DesecAccessDenied",
                $"deSEC authenticated the token but denied read access to DNS zone '{zone}'. Check that this token belongs to the account that manages the zone."),

            HttpStatusCode.NotFound => (
                "DesecZoneNotAccessible",
                $"deSEC could not find DNS zone '{zone}' in the account accessible with this token. Check the base domain and confirm the token belongs to the account that manages this zone."),

            HttpStatusCode.BadRequest => (
                "DesecZoneProbeBadRequest",
                $"deSEC rejected the read-only validation request for DNS zone '{zone}'. Check the base domain and try again."),

            _ => (
                "DesecUnexpectedStatus",
                $"deSEC returned an unexpected response while validating DNS zone '{zone}': {(int)statusCode} {statusCode}.")
        };
    }

    private static string MaskResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return "(empty response)";
        }

        return response.Length <= 1000
            ? response
            : response[..1000] + "...";
    }

    private sealed record DesecRrsetRequest(
        string Subname,
        string Type,
        int Ttl,
        string[] Records);
}