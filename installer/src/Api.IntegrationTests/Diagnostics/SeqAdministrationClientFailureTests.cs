using System.Net;
using Modules.Integrations.Seq.Services;

namespace Api.IntegrationTests.Diagnostics;

public sealed class SeqAdministrationClientFailureTests
{
    [Fact]
    public void Unauthorized_response_is_reported_as_a_credential_rejection()
    {
        var translated = SeqApiAdministrationClientFactory.TranslateAuthenticationFailure(
            new HttpRequestException(
                "super-secret-response",
                inner: null,
                statusCode: HttpStatusCode.Unauthorized));

        Assert.Equal("seq_administrator_credentials_rejected", translated.Code);
        Assert.Equal(401, translated.StatusCode);
        Assert.Contains("username or supplied password", translated.Message);
        Assert.DoesNotContain("super-secret-response", translated.Message);
    }

    [Fact]
    public void Forbidden_response_is_reported_as_missing_administrator_access()
    {
        var translated = SeqApiAdministrationClientFactory.TranslateAuthenticationFailure(
            new HttpRequestException(
                "forbidden-detail",
                inner: null,
                statusCode: HttpStatusCode.Forbidden));

        Assert.Equal("seq_administrator_access_denied", translated.Code);
        Assert.Equal(403, translated.StatusCode);
        Assert.DoesNotContain("forbidden-detail", translated.Message);
    }

    [Fact]
    public void Server_failure_is_not_misreported_as_a_bad_password()
    {
        var translated = SeqApiAdministrationClientFactory.TranslateAuthenticationFailure(
            new HttpRequestException(
                "upstream-secret-detail",
                inner: null,
                statusCode: HttpStatusCode.ServiceUnavailable));

        Assert.Equal("seq_administration_unavailable", translated.Code);
        Assert.Equal(503, translated.StatusCode);
        Assert.DoesNotContain("password", translated.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("upstream-secret-detail", translated.Message);
    }

    [Fact]
    public void Timeout_is_reported_separately()
    {
        var translated = SeqApiAdministrationClientFactory.TranslateAuthenticationFailure(
            new TimeoutException("timed-out-secret-detail"));

        Assert.Equal("seq_administration_timeout", translated.Code);
        Assert.Equal(504, translated.StatusCode);
        Assert.DoesNotContain("timed-out-secret-detail", translated.Message);
    }

    [Fact]
    public void Reflected_not_found_status_is_reported_as_an_api_contract_mismatch()
    {
        var translated = SeqApiAdministrationClientFactory.TranslateAuthenticationFailure(
            new StatusBearingException(404, "compatibility-secret-detail"));

        Assert.Equal("seq_administration_api_incompatible", translated.Code);
        Assert.Equal(503, translated.StatusCode);
        Assert.DoesNotContain("compatibility-secret-detail", translated.Message);
    }

    [Fact]
    public void Unknown_client_failure_fails_honestly_without_blame_on_credentials()
    {
        var translated = SeqApiAdministrationClientFactory.TranslateAuthenticationFailure(
            new InvalidOperationException("unknown-secret-detail"));

        Assert.Equal("seq_administration_api_incompatible", translated.Code);
        Assert.Equal(503, translated.StatusCode);
        Assert.DoesNotContain("password", translated.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unknown-secret-detail", translated.Message);
    }

    private sealed class StatusBearingException(
        int statusCode,
        string message) : Exception(message)
    {
        public int StatusCode { get; } = statusCode;
    }
}
