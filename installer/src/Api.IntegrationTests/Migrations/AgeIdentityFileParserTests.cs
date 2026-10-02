using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class AgeIdentityFileParserTests
{
    [Fact]
    public void Parses_native_age_keygen_identity_file_with_metadata_comments()
    {
        const string identityFile = """
            # created: 2026-07-14T11:34:12Z
            # public key: age1qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq
            AGE-SECRET-KEY-1TESTTESTTESTTESTTESTTESTTESTTESTTESTTESTTESTTESTTESTTEST
            """;

        var identity = AgeIdentityFileParser.Parse(identityFile);

        Assert.Equal(
            "AGE-SECRET-KEY-1TESTTESTTESTTESTTESTTESTTESTTESTTESTTESTTESTTESTTESTTEST",
            identity);
    }

    [Fact]
    public void Rejects_identity_file_without_exactly_one_native_secret_key()
    {
        const string identityFile = """
            # created: 2026-07-14T11:34:12Z
            # public key: age1qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq
            """;

        var exception = Assert.Throws<SecureMigrationIntakeException>(
            () => AgeIdentityFileParser.Parse(identityFile));

        Assert.Equal("age_identity_invalid", exception.Code);
    }

    [Fact]
    public void Rejects_multiple_secret_keys()
    {
        const string identityFile = """
            AGE-SECRET-KEY-1FIRST
            AGE-SECRET-KEY-1SECOND
            """;

        var exception = Assert.Throws<SecureMigrationIntakeException>(
            () => AgeIdentityFileParser.Parse(identityFile));

        Assert.Equal("age_identity_invalid", exception.Code);
    }
}
