using System.Text;
using HostAgent.Runtime.Databases;
using HostAgent.Runtime.Secrets;

namespace HostAgent.Tests.Services;

public sealed class RuntimeStackDatabaseDockerExecSecurityTests
{
    [Fact]
    public void POSTGRES_CREDENTIAL_01A_psql_exec_argv_contains_only_non_secret_options()
    {
        var password = RuntimeStackSecretService.CreateSecretValue();
        var sql = RuntimeStackDatabaseService.BuildEnsureRoleSql(
            "mxu_demo_12345678",
            password);

        var exec = RuntimeStackDatabaseService.CreatePsqlExecParameters();
        var argv = string.Join("\u001f", exec.Cmd ?? []);

        Assert.True(exec.AttachStdin);
        Assert.DoesNotContain("-c", exec.Cmd ?? []);
        Assert.DoesNotContain(password, argv, StringComparison.Ordinal);
        Assert.DoesNotContain(sql, argv, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE ROLE", argv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER ROLE", argv, StringComparison.OrdinalIgnoreCase);

        // Guard against a meaningless fixture: the secret-bearing role SQL really
        // does contain the generated password, it is simply no longer in argv.
        Assert.Contains(password, sql, StringComparison.Ordinal);
    }

    [Fact]
    public void POSTGRES_CREDENTIAL_01A_psql_exec_reads_sql_from_attached_stdin()
    {
        const string sql = "SELECT 1;";

        var encoded = RuntimeStackDatabaseService.EncodeSqlStandardInput(sql);

        Assert.Equal(sql + "\n", Encoding.UTF8.GetString(encoded));
    }
}
