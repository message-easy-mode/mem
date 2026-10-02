using HostAgent.Matrix.Users;
using HostAgent.Runtime.Databases;

namespace HostAgent.Tests.Matrix.Users;

public sealed class SynapseUserInventoryReaderTests
{
    [Fact]
    public async Task UserInventory_parses_active_admin_standard_and_deactivated_accounts()
    {
        var executor = new FakeQueryExecutor(
            "name\nadmin\ndeactivated\ncreation_ts\npassword_hash\n",
            """
            {"matrixUserId":"@admin:example.test","isAdmin":true,"isDeactivated":false,"createdAtUnixMs":1710000000000}
            {"matrixUserId":"@alice:example.test","isAdmin":false,"isDeactivated":false,"createdAtUnixMs":1710000001000}
            {"matrixUserId":"@retired:example.test","isAdmin":false,"isDeactivated":true,"createdAtUnixMs":null}
            """);

        var reader = new SynapsePostgresUserInventoryReader(executor);

        var result = await reader.ReadAsync(
            new RuntimeStackUserInventoryTarget(Guid.NewGuid(), "matrix_test_12345678"),
            CancellationToken.None);

        Assert.Equal(3, result.Accounts.Count);
        Assert.Equal(1, result.ActiveAdminCount);

        var admin = Assert.Single(result.Accounts, x => x.Username == "admin");
        Assert.True(admin.IsAdmin);
        Assert.False(admin.IsDeactivated);
        Assert.NotNull(admin.CreatedAtUtc);

        var retired = Assert.Single(result.Accounts, x => x.Username == "retired");
        Assert.False(retired.IsAdmin);
        Assert.True(retired.IsDeactivated);
        Assert.Null(retired.CreatedAtUtc);
    }

    [Fact]
    public async Task UserInventory_excludes_MEM_managed_recovery_administrators()
    {
        var executor = new FakeQueryExecutor(
            "name\nadmin\ndeactivated\ncreation_ts\n",
            """
            {"matrixUserId":"@admin:example.test","isAdmin":true,"isDeactivated":false,"createdAtUnixMs":1710000000000}
            {"matrixUserId":"@mem_recovery_abcdef1234567890:example.test","isAdmin":true,"isDeactivated":false,"createdAtUnixMs":1710000001000}
            {"matrixUserId":"@_mem_recovery_legacy12345678:example.test","isAdmin":true,"isDeactivated":false,"createdAtUnixMs":1710000001500}
            {"matrixUserId":"@alice:example.test","isAdmin":false,"isDeactivated":false,"createdAtUnixMs":1710000002000}
            """);

        var reader = new SynapsePostgresUserInventoryReader(executor);
        var result = await reader.ReadAsync(
            new RuntimeStackUserInventoryTarget(Guid.NewGuid(), "matrix_test_12345678"),
            CancellationToken.None);

        Assert.Equal(2, result.Accounts.Count);
        Assert.Equal(1, result.ActiveAdminCount);
        Assert.DoesNotContain(
            result.Accounts,
            x => MatrixManagedRecoveryAuthorityService.IsManagedRecoveryUser(x.MatrixUserId));
    }

    [Fact]
    public async Task UserInventory_handles_absent_optional_columns_conservatively()
    {
        var executor = new FakeQueryExecutor(
            "name\nadmin\npassword_hash\n",
            "{\"matrixUserId\":\"@alice:example.test\",\"isAdmin\":true,\"isDeactivated\":false,\"createdAtUnixMs\":null}\n");

        var reader = new SynapsePostgresUserInventoryReader(executor);
        var result = await reader.ReadAsync(
            new RuntimeStackUserInventoryTarget(Guid.NewGuid(), "matrix_test_12345678"),
            CancellationToken.None);

        var account = Assert.Single(result.Accounts);
        Assert.True(account.IsAdmin);
        Assert.False(account.IsDeactivated);
        Assert.Null(account.CreatedAtUtc);

        var inventoryQuery = executor.Queries[1];
        Assert.Contains("admin::text", inventoryQuery, StringComparison.Ordinal);
        Assert.Contains("'isDeactivated', false", inventoryQuery, StringComparison.Ordinal);
        Assert.Contains("'createdAtUnixMs', NULL", inventoryQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserInventory_rejects_schema_without_required_identity_column()
    {
        var executor = new FakeQueryExecutor("admin\ndeactivated\n", string.Empty);
        var reader = new SynapsePostgresUserInventoryReader(executor);

        var ex = await Assert.ThrowsAsync<RuntimeStackUserInventoryException>(() =>
            reader.ReadAsync(
                new RuntimeStackUserInventoryTarget(Guid.NewGuid(), "matrix_test_12345678"),
                CancellationToken.None));

        Assert.Equal("synapse_users_schema_unsupported", ex.Code);
        Assert.Single(executor.Queries);
    }

    [Fact]
    public async Task UserInventory_query_never_selects_password_hashes_or_token_material()
    {
        var executor = new FakeQueryExecutor(
            "name\nadmin\ndeactivated\ncreation_ts\npassword_hash\n",
            string.Empty);
        var reader = new SynapsePostgresUserInventoryReader(executor);

        await reader.ReadAsync(
            new RuntimeStackUserInventoryTarget(Guid.NewGuid(), "matrix_test_12345678"),
            CancellationToken.None);

        var inventoryQuery = executor.Queries[1];
        Assert.DoesNotContain("password_hash", inventoryQuery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", inventoryQuery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("device", inventoryQuery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT *", inventoryQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UserInventory_rejects_invalid_structured_output_with_safe_error()
    {
        var executor = new FakeQueryExecutor("name\nadmin\n", "not-json\n");
        var reader = new SynapsePostgresUserInventoryReader(executor);

        var ex = await Assert.ThrowsAsync<RuntimeStackUserInventoryException>(() =>
            reader.ReadAsync(
                new RuntimeStackUserInventoryTarget(Guid.NewGuid(), "matrix_test_12345678"),
                CancellationToken.None));

        Assert.Equal("synapse_users_inventory_invalid", ex.Code);
        Assert.DoesNotContain("not-json", ex.SafeDetail, StringComparison.Ordinal);
    }

    private sealed class FakeQueryExecutor : IRuntimeStackPostgresQueryExecutor
    {
        private readonly Queue<string> _outputs;

        public FakeQueryExecutor(params string[] outputs)
        {
            _outputs = new Queue<string>(outputs);
        }

        public List<string> Queries { get; } = [];

        public Task<RuntimeStackPostgresQueryResult> QueryAsync(
            string databaseName,
            string sql,
            CancellationToken ct)
        {
            Queries.Add(sql);

            if (_outputs.Count == 0)
            {
                throw new InvalidOperationException("No fake PostgreSQL output remains.");
            }

            return Task.FromResult(new RuntimeStackPostgresQueryResult(_outputs.Dequeue()));
        }
    }
}
