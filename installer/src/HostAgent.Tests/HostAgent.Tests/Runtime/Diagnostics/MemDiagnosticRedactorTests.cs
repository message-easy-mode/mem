using HostAgent.Runtime.Diagnostics;

namespace HostAgent.Tests.Runtime.Diagnostics;

public sealed class MemDiagnosticRedactorTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("passwd")]
    [InlineData("pwd")]
    [InlineData("db_password")]
    [InlineData("client_secret")]
    [InlineData("api-token")]
    [InlineData("Authorization")]
    [InlineData("credential")]
    [InlineData("connectionString")]
    [InlineData("private_key")]
    [InlineData("signing-key")]
    [InlineData("shared_key")]
    [InlineData("api_key")]
    [InlineData("recovery-code")]
    [InlineData("access-code")]
    [InlineData("age_identity")]
    [InlineData("setup-code")]
    [InlineData("cookie")]
    public void Sensitive_detail_keys_are_redacted(string key)
    {
        var redactor = new MemDiagnosticRedactor(new MemDiagnosticsOptions());

        var result = redactor.RedactDictionary(new Dictionary<string, string?>
        {
            [key] = "known-secret-value"
        });

        Assert.NotNull(result.Value);
        Assert.Equal("[redacted]", Assert.Single(result.Value!).Value);
        Assert.True(result.Changed);
    }

    [Theory]
    [InlineData("password=hunter2")]
    [InlineData("token:abc123")]
    [InlineData("Authorization=BearerValue")]
    [InlineData("Authorization: Bearer abc123")]
    [InlineData("POSTGRES_PASSWORD=hunter2")]
    [InlineData("SYNAPSE_REGISTRATION_SHARED_SECRET=abc123")]
    [InlineData("--static-auth-secret=BearerValue")]
    [InlineData("access_token=abcdefghijklmnop")]
    [InlineData("MEM_SETUP_CODE=hunter2")]
    [InlineData("AGE_IDENTITY=abc123")]
    [InlineData("{\"password\":\"hunter2\"}")]
    [InlineData("--static-auth-secret abc123")]
    [InlineData("Bearer abcdefghijklmnop")]
    [InlineData("Basic YWRtaW46cGFzc3dvcmQ=")]
    public void Sensitive_assignments_and_authorization_values_are_removed(
        string value)
    {
        var redactor = new MemDiagnosticRedactor(new MemDiagnosticsOptions());

        var result = redactor.RedactText(value);

        Assert.DoesNotContain("hunter2", result.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", result.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("BearerValue", result.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghijklmnop", result.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("YWRtaW46cGFzc3dvcmQ=", result.Value, StringComparison.Ordinal);
        Assert.True(result.Changed);
    }

    [Fact]
    public void Exact_known_secrets_are_removed_from_otherwise_safe_text()
    {
        var redactor = new MemDiagnosticRedactor(new MemDiagnosticsOptions());
        const string secret = "a-very-specific-runtime-secret";

        var result = redactor.RedactText(
            $"Docker returned {secret} while starting the container.",
            exactSecrets: [secret]);

        Assert.DoesNotContain(secret, result.Value, StringComparison.Ordinal);
        Assert.Contains("[redacted]", result.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Uri_credentials_private_keys_age_identities_and_jwts_are_removed()
    {
        var redactor = new MemDiagnosticRedactor(new MemDiagnosticsOptions());
        var value = """
            postgres://mem:supersecret@postgres:5432/mem
            -----BEGIN PRIVATE KEY-----
            abcdefghijklmnop
            -----END PRIVATE KEY-----
            AGE-SECRET-KEY-1ABCDEF234567
            eyJabcdefghijk.abcdefghijkl.abcdefghijkl
            """;

        var result = redactor.RedactText(value);

        Assert.DoesNotContain("supersecret", result.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghijklmnop", result.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("AGE-SECRET-KEY", result.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eyJabcdefghijk", result.Value, StringComparison.Ordinal);
        Assert.True(result.Changed);
    }

    [Fact]
    public void Detail_values_and_key_count_are_bounded()
    {
        var options = new MemDiagnosticsOptions
        {
            MaximumDetailCharacters = 100
        };
        var redactor = new MemDiagnosticRedactor(options);
        var details = Enumerable.Range(0, 80)
            .ToDictionary(
                index => $"key-{index:000}",
                _ => (string?)new string('x', 500),
                StringComparer.Ordinal);

        var result = redactor.RedactDictionary(details);

        Assert.NotNull(result.Value);
        Assert.Equal(64, result.Value!.Count);
        Assert.All(result.Value.Values, value => Assert.True(value.Length <= 100));
        Assert.True(result.Truncated);
    }
}
