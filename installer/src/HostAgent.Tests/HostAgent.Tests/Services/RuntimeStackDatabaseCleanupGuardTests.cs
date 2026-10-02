using HostAgent.Runtime.Databases;

namespace HostAgent.Tests.Services;

public sealed class RuntimeStackDatabaseCleanupGuardTests
{
    private static readonly RuntimeStackDatabaseIdentity Expected = new(
        "matrix_demo_12345678",
        "mxu_demo_12345678");

    [Fact]
    public void STACK_CREATE_REL_01A_identity_mismatch_refuses_cleanup()
    {
        Assert.Throws<RuntimeStackDatabaseCleanupRefusedException>(() =>
            RuntimeStackDatabaseService.EnsureUnregisteredMatrixDatabaseCleanupAllowed(
                Expected,
                "matrix_other_12345678",
                Expected.DatabaseUsername,
                runtimeStackExists: false,
                ownershipExists: false));
    }

    [Fact]
    public void STACK_CREATE_REL_01A_registered_runtime_stack_refuses_cleanup()
    {
        Assert.Throws<RuntimeStackDatabaseCleanupRefusedException>(() =>
            RuntimeStackDatabaseService.EnsureUnregisteredMatrixDatabaseCleanupAllowed(
                Expected,
                Expected.DatabaseName,
                Expected.DatabaseUsername,
                runtimeStackExists: true,
                ownershipExists: false));
    }

    [Fact]
    public void STACK_CREATE_REL_01A_recorded_database_ownership_refuses_cleanup()
    {
        Assert.Throws<RuntimeStackDatabaseCleanupRefusedException>(() =>
            RuntimeStackDatabaseService.EnsureUnregisteredMatrixDatabaseCleanupAllowed(
                Expected,
                Expected.DatabaseName,
                Expected.DatabaseUsername,
                runtimeStackExists: false,
                ownershipExists: true));
    }

    [Fact]
    public void STACK_CREATE_REL_01A_exact_unregistered_identity_allows_cleanup()
    {
        RuntimeStackDatabaseService.EnsureUnregisteredMatrixDatabaseCleanupAllowed(
            Expected,
            Expected.DatabaseName,
            Expected.DatabaseUsername,
            runtimeStackExists: false,
            ownershipExists: false);
    }
}
