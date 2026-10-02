using Mem.Migrate.Web.Security;

namespace Mem.Migrate.Web.Tests;

public sealed class AccessCodeCredentialTests
{
    [Fact]
    public void Generated_code_is_human_readable_and_verifies()
    {
        var generated = AccessCodeCredential.Generate();

        Assert.Equal(34, generated.DisplayCode.Length);
        Assert.Equal(6, generated.DisplayCode.Count(character => character == '-'));
        Assert.True(generated.Credential.Verify(generated.DisplayCode));
        Assert.True(generated.Credential.Verify(generated.DisplayCode.ToLowerInvariant().Replace("-", " ")));
    }

    [Fact]
    public void Incorrect_code_is_rejected()
    {
        var credential = AccessCodeCredential.Create("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345");

        Assert.False(credential.Verify("ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2346"));
        Assert.False(credential.Verify(null));
        Assert.False(credential.Verify("short"));
    }
}
