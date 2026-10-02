using System.Text.Json;
using System.Text.Json.Serialization;
using HostAgent.Matrix.Provisioning;
using HostAgent.Matrix.Users;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Operations;
using HostAgent.Runtime.Secrets;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Api.IntegrationTests.Runtime;

public sealed class RuntimeStackUserCreationInventoryPersistenceTests
{
    [Fact]
    public async Task RuntimeStackUserCreation_refreshes_inventory_after_first_admin_and_next_user()
    {
        await using var fixture = await Fixture.CreateAsync();

        var firstAdmin = await fixture.Service.CreateFirstAdminAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "admin",
                Password: "valid-password-123",
                IsAdmin: true,
                DisplayName: "First Admin",
                Email: null),
            CancellationToken.None);

        Assert.Equal("active", firstAdmin.Status);
        Assert.True(firstAdmin.IsAdmin);
        Assert.True(firstAdmin.IsFirstAdmin);

        var afterFirstAdmin = Assert.IsType<RuntimeStackUsersResponse>(
            await fixture.Service.ListAsync(fixture.Manifest.Slug, CancellationToken.None));

        Assert.Equal(RuntimeStackUserInventoryStates.Synchronized, afterFirstAdmin.InventoryStatus);
        Assert.Equal(1, afterFirstAdmin.InventoryUserCount);
        Assert.Equal(1, afterFirstAdmin.ActiveAdminCount);
        Assert.False(afterFirstAdmin.RequiresFirstAdmin);
        Assert.True(afterFirstAdmin.CanCreateUsers);

        var secondUser = await fixture.Service.CreateUserAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "alice",
                Password: "another-valid-password-123",
                IsAdmin: false,
                DisplayName: "Alice",
                Email: null),
            CancellationToken.None);

        Assert.Equal("active", secondUser.Status);
        Assert.False(secondUser.IsAdmin);
        Assert.False(secondUser.IsFirstAdmin);

        var afterSecondUser = Assert.IsType<RuntimeStackUsersResponse>(
            await fixture.Service.ListAsync(fixture.Manifest.Slug, CancellationToken.None));

        Assert.Equal(RuntimeStackUserInventoryStates.Synchronized, afterSecondUser.InventoryStatus);
        Assert.Equal(2, afterSecondUser.InventoryUserCount);
        Assert.Equal(1, afterSecondUser.ActiveAdminCount);
        Assert.False(afterSecondUser.RequiresFirstAdmin);
        Assert.True(afterSecondUser.CanCreateUsers);
        Assert.Equal(2, afterSecondUser.Users.Count);

        var database = await fixture.Db.RuntimeStackDatabases.AsNoTracking().SingleAsync();
        var inventory = RuntimeStackUserInventoryMetadata.Read(database.MetadataJson);

        Assert.Equal(RuntimeStackUserInventoryStates.Synchronized, inventory.Status);
        Assert.Equal(2, inventory.UserCount);
        Assert.Equal(1, inventory.ActiveAdminCount);
    }

    [Fact]
    public async Task RuntimeStackUserCreation_keeps_created_account_active_when_post_creation_inventory_refresh_fails()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Registration.FailInventoryAfterRegistration = true;

        var created = await fixture.Service.CreateFirstAdminAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "admin",
                Password: "valid-password-123",
                IsAdmin: true,
                DisplayName: null,
                Email: null),
            CancellationToken.None);

        Assert.Equal("active", created.Status);

        var entity = await fixture.Db.RuntimeStackUsers.AsNoTracking().SingleAsync();
        Assert.Equal("active", entity.Status);
        Assert.Null(entity.LastError);

        var database = await fixture.Db.RuntimeStackDatabases.AsNoTracking().SingleAsync();
        var inventory = RuntimeStackUserInventoryMetadata.Read(database.MetadataJson);

        Assert.Equal(RuntimeStackUserInventoryStates.Failed, inventory.Status);
        Assert.Equal("matrix_user_inventory_query_failed", inventory.ErrorCode);

        var operation = await fixture.Db.RuntimeOperations.AsNoTracking().SingleAsync();
        Assert.Equal("active", operation.Status);
        Assert.Contains(
            "matrix_user_inventory_query_failed",
            operation.ResultJson ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task First_admin_creation_remains_successful_when_authority_protection_fails()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Protector.ThrowOnProtect = true;

        var admin = await fixture.Service.CreateFirstAdminAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "admin",
                Password: "initial-password-123",
                IsAdmin: true,
                DisplayName: null,
                Email: null),
            CancellationToken.None);

        Assert.Equal("active", admin.Status);
        Assert.Empty(await fixture.Db.RuntimeStackSecrets.AsNoTracking().ToArrayAsync());

        var listed = Assert.IsType<RuntimeStackUsersResponse>(
            await fixture.Service.ListAsync(fixture.Manifest.Slug, CancellationToken.None));
        Assert.Equal(MatrixAdminAuthorityStates.Required, listed.AdminAuthority.Status);
        Assert.False(listed.AdminAuthority.CanResetPasswords);

        var operation = await fixture.Db.RuntimeOperations.AsNoTracking().SingleAsync();
        Assert.Contains(
            "matrix_admin_authority_capture_failed",
            operation.ResultJson ?? string.Empty,
            StringComparison.Ordinal);
        Assert.DoesNotContain("captured-admin-token", operation.ResultJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task First_admin_creation_captures_protected_authority_and_resets_password_without_persisting_secrets()
    {
        await using var fixture = await Fixture.CreateAsync();

        var admin = await fixture.Service.CreateFirstAdminAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "admin",
                Password: "initial-password-123",
                IsAdmin: true,
                DisplayName: null,
                Email: null),
            CancellationToken.None);

        var secret = await fixture.Db.RuntimeStackSecrets.AsNoTracking().SingleAsync(
            x => x.SecretKind == MatrixAdminAuthorityService.SecretKind);
        Assert.Equal("active", secret.Status);
        Assert.DoesNotContain("captured-admin-token", secret.SecretValue, StringComparison.Ordinal);
        Assert.Equal("captured-admin-token", fixture.Protector.Unprotect(secret.SecretValue));
        Assert.DoesNotContain("captured-admin-token", secret.MetadataJson ?? string.Empty, StringComparison.Ordinal);

        var listed = Assert.IsType<RuntimeStackUsersResponse>(
            await fixture.Service.ListAsync(fixture.Manifest.Slug, CancellationToken.None));
        Assert.Equal(MatrixAdminAuthorityStates.Available, listed.AdminAuthority.Status);
        Assert.True(listed.AdminAuthority.CanResetPasswords);
        Assert.Equal(admin.MatrixUserId, listed.AdminAuthority.AdminUserId);

        var user = await fixture.Service.CreateUserAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "alice",
                Password: "alice-initial-password-123",
                IsAdmin: false,
                DisplayName: null,
                Email: null),
            CancellationToken.None);

        var reset = await fixture.Service.ResetPasswordAsync(
            fixture.Manifest.Slug,
            user.Id,
            new ResetRuntimeStackUserPasswordRequest("replacement-password-456"),
            CancellationToken.None);

        Assert.Equal("password_reset", reset.Status);
        Assert.True(reset.LogoutDevices);
        Assert.False(reset.AdminAuthorityInvalidated);
        Assert.Equal(user.MatrixUserId, fixture.AdminClient.LastResetUserId);
        Assert.Equal("replacement-password-456", fixture.AdminClient.LastPassword);
        Assert.Equal("captured-admin-token", fixture.AdminClient.LastAccessToken);
        Assert.True(fixture.AdminClient.LastLogoutDevices);

        var afterReset = Assert.IsType<RuntimeStackUsersResponse>(
            await fixture.Service.ListAsync(fixture.Manifest.Slug, CancellationToken.None));
        Assert.Equal(MatrixAdminAuthorityStates.Available, afterReset.AdminAuthority.Status);

        var operations = await fixture.Db.RuntimeOperations.AsNoTracking().ToArrayAsync();
        var serialized = string.Join("\n", operations.SelectMany(x => new[]
        {
            x.InputJson,
            x.ResultJson,
            x.EvidenceJson,
            x.LastError
        }).Where(x => x is not null));
        Assert.DoesNotContain("initial-password-123", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("alice-initial-password-123", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("replacement-password-456", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("captured-admin-token", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task User_creation_failure_persists_safe_operator_detail_while_rethrowing_technical_exception()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string technicalFailure =
            "Connection refused (matrix-user-creation-stack.restored.test:443)";
        fixture.Registration.Exception = new HttpRequestException(technicalFailure);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            fixture.Service.CreateFirstAdminAsync(
                fixture.Manifest.Slug,
                new CreateRuntimeStackUserRequest(
                    Username: "admin",
                    Password: "valid-password-123",
                    IsAdmin: true,
                    DisplayName: null,
                    Email: null),
                CancellationToken.None));

        Assert.Equal(technicalFailure, ex.Message);

        var entity = await fixture.Db.RuntimeStackUsers.AsNoTracking().SingleAsync();
        Assert.Equal("failed", entity.Status);
        Assert.Equal(
            "Matrix user creation did not complete. Open Diagnostics for the recorded failure before retrying.",
            entity.LastError);
        Assert.DoesNotContain("matrix-user-creation-stack", entity.LastError, StringComparison.Ordinal);

        var operation = await fixture.Db.RuntimeOperations.AsNoTracking().SingleAsync(
            x => x.Operation == "create-first-matrix-admin");
        Assert.Equal(
            "Matrix user creation did not complete. Open Diagnostics for the recorded failure before retrying.",
            operation.LastError);
        Assert.DoesNotContain("matrix-user-creation-stack", operation.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Password_reset_provisions_managed_recovery_authority_when_no_token_is_stored()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Service.CreateFirstAdminAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "admin",
                Password: "initial-password-123",
                IsAdmin: true,
                DisplayName: null,
                Email: null),
            CancellationToken.None);

        var user = await fixture.Service.CreateUserAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "alice",
                Password: "alice-initial-password-123",
                IsAdmin: false,
                DisplayName: null,
                Email: null),
            CancellationToken.None);

        var existingAuthority = await fixture.Db.RuntimeStackSecrets.SingleAsync(
            x => x.SecretKind == MatrixAdminAuthorityService.SecretKind);
        fixture.Db.RuntimeStackSecrets.Remove(existingAuthority);
        await fixture.Db.SaveChangesAsync();

        var reset = await fixture.Service.ResetPasswordAsync(
            fixture.Manifest.Slug,
            user.Id,
            new ResetRuntimeStackUserPasswordRequest("replacement-password-456"),
            CancellationToken.None);

        Assert.False(reset.AdminAuthorityInvalidated);
        Assert.Equal(user.MatrixUserId, fixture.AdminClient.LastResetUserId);
        Assert.Equal("managed-admin-token", fixture.AdminClient.LastAccessToken);
        Assert.True(fixture.Registration.LastAdmin);
        Assert.True(fixture.Registration.LastUsername?.StartsWith(
            MatrixManagedRecoveryAuthorityService.UsernamePrefix,
            StringComparison.Ordinal) == true);
        Assert.False(fixture.Registration.LastUsername?.StartsWith('_') == true);
        Assert.NotEqual("replacement-password-456", fixture.Registration.LastPassword);

        var listed = Assert.IsType<RuntimeStackUsersResponse>(
            await fixture.Service.ListAsync(fixture.Manifest.Slug, CancellationToken.None));
        Assert.Equal(MatrixAdminAuthorityStates.Available, listed.AdminAuthority.Status);
        Assert.Equal(
            MatrixManagedRecoveryAuthorityService.AuthoritySource,
            listed.AdminAuthority.Source);

        var operations = await fixture.Db.RuntimeOperations.AsNoTracking().ToArrayAsync();
        var serialized = string.Join("\n", operations.SelectMany(x => new[]
        {
            x.InputJson,
            x.ResultJson,
            x.EvidenceJson,
            x.LastError
        }).Where(x => x is not null));
        Assert.DoesNotContain("replacement-password-456", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("managed-admin-token", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Registration.LastPassword!, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Password_reset_reports_safe_error_when_Synapse_rejects_managed_recovery_registration()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Service.CreateFirstAdminAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "admin",
                Password: "initial-password-123",
                IsAdmin: true,
                DisplayName: null,
                Email: null),
            CancellationToken.None);

        var user = await fixture.Service.CreateUserAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "alice",
                Password: "alice-initial-password-123",
                IsAdmin: false,
                DisplayName: null,
                Email: null),
            CancellationToken.None);

        var existingAuthority = await fixture.Db.RuntimeStackSecrets.SingleAsync(
            x => x.SecretKind == MatrixAdminAuthorityService.SecretKind);
        fixture.Db.RuntimeStackSecrets.Remove(existingAuthority);
        await fixture.Db.SaveChangesAsync();

        fixture.Registration.Exception = new InvalidOperationException(
            "Synapse shared-secret registration failed with HTTP 400: " +
            "{\"errcode\":\"M_INVALID_USERNAME\",\"error\":\"raw diagnostic\"}");

        var ex = await Assert.ThrowsAsync<MatrixAdminAuthorityException>(() =>
            fixture.Service.ResetPasswordAsync(
                fixture.Manifest.Slug,
                user.Id,
                new ResetRuntimeStackUserPasswordRequest("replacement-password-456"),
                CancellationToken.None));

        Assert.Equal("matrix_managed_recovery_registration_rejected", ex.Code);
        Assert.Equal(
            "Synapse rejected creation of the MEM recovery administrator.",
            ex.SafeDetail);
        Assert.DoesNotContain("M_INVALID_USERNAME", ex.SafeDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("raw diagnostic", ex.SafeDetail, StringComparison.Ordinal);

        var operation = await fixture.Db.RuntimeOperations.AsNoTracking().SingleAsync(
            x => x.Operation == "reset-matrix-user-password");
        var serialized = string.Join("\n", new[]
        {
            operation.InputJson,
            operation.ResultJson,
            operation.EvidenceJson,
            operation.LastError
        }.Where(x => x is not null));

        Assert.Contains(
            "matrix_managed_recovery_registration_rejected",
            serialized,
            StringComparison.Ordinal);
        Assert.DoesNotContain("M_INVALID_USERNAME", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("raw diagnostic", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("replacement-password-456", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resetting_the_authority_admin_rotates_the_protected_authority_with_the_new_password()
    {
        await using var fixture = await Fixture.CreateAsync();

        var admin = await fixture.Service.CreateFirstAdminAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "admin",
                Password: "initial-password-123",
                IsAdmin: true,
                DisplayName: null,
                Email: null),
            CancellationToken.None);

        var reset = await fixture.Service.ResetPasswordAsync(
            fixture.Manifest.Slug,
            admin.Id,
            new ResetRuntimeStackUserPasswordRequest("replacement-password-456"),
            CancellationToken.None);

        Assert.False(reset.AdminAuthorityInvalidated);
        Assert.Equal(admin.MatrixUserId, fixture.AdminClient.LastLoginUserId);
        Assert.Equal("replacement-password-456", fixture.AdminClient.LastLoginPassword);

        var secret = await fixture.Db.RuntimeStackSecrets.AsNoTracking().SingleAsync(
            x => x.SecretKind == MatrixAdminAuthorityService.SecretKind);
        Assert.Equal("rotated-admin-token", fixture.Protector.Unprotect(secret.SecretValue));
        Assert.DoesNotContain("replacement-password-456", secret.SecretValue, StringComparison.Ordinal);
        Assert.DoesNotContain("rotated-admin-token", secret.SecretValue, StringComparison.Ordinal);

        var listed = Assert.IsType<RuntimeStackUsersResponse>(
            await fixture.Service.ListAsync(fixture.Manifest.Slug, CancellationToken.None));
        Assert.Equal(MatrixAdminAuthorityStates.Available, listed.AdminAuthority.Status);
        Assert.True(listed.AdminAuthority.CanResetPasswords);
        Assert.Equal("password-reset-rotation", listed.AdminAuthority.Source);
        Assert.Null(listed.AdminAuthority.ErrorCode);

        var operations = await fixture.Db.RuntimeOperations.AsNoTracking().ToArrayAsync();
        var serialized = string.Join("\n", operations.SelectMany(x => new[]
        {
            x.InputJson,
            x.ResultJson,
            x.EvidenceJson,
            x.LastError
        }).Where(x => x is not null));
        Assert.DoesNotContain("replacement-password-456", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("rotated-admin-token", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authority_admin_password_reset_falls_back_to_managed_recovery_when_token_rotation_fails()
    {
        await using var fixture = await Fixture.CreateAsync();

        var admin = await fixture.Service.CreateFirstAdminAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "admin",
                Password: "initial-password-123",
                IsAdmin: true,
                DisplayName: null,
                Email: null),
            CancellationToken.None);

        fixture.AdminClient.LoginException = new MatrixAdminAuthorityException(
            "matrix_admin_authority_login_rejected",
            "The Matrix administrator credentials were rejected by Synapse.",
            MatrixAdminAuthorityFailureKind.Rejected);

        var reset = await fixture.Service.ResetPasswordAsync(
            fixture.Manifest.Slug,
            admin.Id,
            new ResetRuntimeStackUserPasswordRequest("replacement-password-456"),
            CancellationToken.None);

        Assert.False(reset.AdminAuthorityInvalidated);
        Assert.Equal(admin.MatrixUserId, fixture.AdminClient.LastResetUserId);
        Assert.True(fixture.Registration.LastUsername?.StartsWith(
            MatrixManagedRecoveryAuthorityService.UsernamePrefix,
            StringComparison.Ordinal) == true);
        Assert.True(fixture.Registration.LastAdmin);

        var listed = Assert.IsType<RuntimeStackUsersResponse>(
            await fixture.Service.ListAsync(fixture.Manifest.Slug, CancellationToken.None));
        Assert.Equal(MatrixAdminAuthorityStates.Available, listed.AdminAuthority.Status);
        Assert.True(listed.AdminAuthority.CanResetPasswords);
        Assert.Equal(
            MatrixManagedRecoveryAuthorityService.AuthoritySource,
            listed.AdminAuthority.Source);
        Assert.Null(listed.AdminAuthority.ErrorCode);
    }

    [Fact]
    public async Task Rejected_stored_authority_is_replaced_automatically_and_password_reset_is_retried()
    {
        await using var fixture = await Fixture.CreateAsync();

        var admin = await fixture.Service.CreateFirstAdminAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "admin",
                Password: "initial-password-123",
                IsAdmin: true,
                DisplayName: null,
                Email: null),
            CancellationToken.None);

        fixture.AdminClient.ResetException = new MatrixAdminAuthorityException(
            "matrix_admin_authority_rejected",
            "The stored Matrix administrator authority was rejected by Synapse.",
            MatrixAdminAuthorityFailureKind.Rejected);
        fixture.AdminClient.ResetFailuresRemaining = 1;

        var reset = await fixture.Service.ResetPasswordAsync(
            fixture.Manifest.Slug,
            admin.Id,
            new ResetRuntimeStackUserPasswordRequest("replacement-password-456"),
            CancellationToken.None);

        Assert.False(reset.AdminAuthorityInvalidated);
        Assert.Equal("managed-admin-token", fixture.AdminClient.LastAccessToken);
        Assert.True(fixture.Registration.LastUsername?.StartsWith(
            MatrixManagedRecoveryAuthorityService.UsernamePrefix,
            StringComparison.Ordinal) == true);

        var listed = Assert.IsType<RuntimeStackUsersResponse>(
            await fixture.Service.ListAsync(fixture.Manifest.Slug, CancellationToken.None));
        Assert.Equal(MatrixAdminAuthorityStates.Available, listed.AdminAuthority.Status);
        Assert.True(listed.AdminAuthority.CanResetPasswords);
        Assert.Equal(
            MatrixManagedRecoveryAuthorityService.AuthoritySource,
            listed.AdminAuthority.Source);
    }

    [Fact]
    public async Task Restored_stack_can_import_validated_admin_authority_without_recording_the_token()
    {
        await using var fixture = await Fixture.CreateAsync();

        var status = await fixture.Service.ImportAdminAuthorityAsync(
            fixture.Manifest.Slug,
            new ImportMatrixAdminAuthorityRequest("operator-supplied-token"),
            CancellationToken.None);

        Assert.Equal(MatrixAdminAuthorityStates.Available, status.Status);
        Assert.Equal("@existing-admin:restored.test", status.AdminUserId);
        Assert.Equal("operator-supplied", status.Source);
        Assert.Equal("operator-supplied-token", fixture.AdminClient.LastValidatedAccessToken);

        var secret = await fixture.Db.RuntimeStackSecrets.AsNoTracking().SingleAsync(
            x => x.SecretKind == MatrixAdminAuthorityService.SecretKind);
        Assert.DoesNotContain("operator-supplied-token", secret.SecretValue, StringComparison.Ordinal);
        Assert.DoesNotContain("operator-supplied-token", secret.MetadataJson ?? string.Empty, StringComparison.Ordinal);

        var operation = await fixture.Db.RuntimeOperations.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("operator-supplied-token", operation.InputJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("operator-supplied-token", operation.ResultJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("operator-supplied-token", operation.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restored_stack_can_import_authority_by_admin_login_without_recording_credentials()
    {
        await using var fixture = await Fixture.CreateAsync();

        var status = await fixture.Service.ImportAdminAuthorityAsync(
            fixture.Manifest.Slug,
            new ImportMatrixAdminAuthorityRequest(
                MatrixUserId: "@existing-admin:restored.test",
                Password: "known-admin-password-123"),
            CancellationToken.None);

        Assert.Equal(MatrixAdminAuthorityStates.Available, status.Status);
        Assert.Equal("@existing-admin:restored.test", status.AdminUserId);
        Assert.Equal("operator-admin-login", status.Source);
        Assert.Equal("@existing-admin:restored.test", fixture.AdminClient.LastLoginUserId);
        Assert.Equal("known-admin-password-123", fixture.AdminClient.LastLoginPassword);

        var secret = await fixture.Db.RuntimeStackSecrets.AsNoTracking().SingleAsync(
            x => x.SecretKind == MatrixAdminAuthorityService.SecretKind);
        Assert.Equal("login-admin-token", fixture.Protector.Unprotect(secret.SecretValue));
        Assert.DoesNotContain("known-admin-password-123", secret.SecretValue, StringComparison.Ordinal);
        Assert.DoesNotContain("login-admin-token", secret.SecretValue, StringComparison.Ordinal);
        Assert.DoesNotContain("known-admin-password-123", secret.MetadataJson ?? string.Empty, StringComparison.Ordinal);

        var operation = await fixture.Db.RuntimeOperations.AsNoTracking().SingleAsync();
        var serialized = string.Join("\n", new[]
        {
            operation.InputJson,
            operation.ResultJson,
            operation.EvidenceJson,
            operation.LastError
        }.Where(x => x is not null));
        Assert.DoesNotContain("known-admin-password-123", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("login-admin-token", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task User_lifecycle_deactivates_and_reactivates_member_without_recording_password()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.CreateFirstAdminAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "admin",
                Password: "initial-admin-password-123",
                IsAdmin: true,
                DisplayName: null,
                Email: null),
            CancellationToken.None);
        var member = await fixture.Service.CreateUserAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "member",
                Password: "initial-member-password-123",
                IsAdmin: false,
                DisplayName: null,
                Email: null),
            CancellationToken.None);

        var deactivated = await fixture.Service.DeactivateUserAsync(
            fixture.Manifest.Slug,
            member.Id,
            new DeactivateRuntimeStackUserRequest(),
            CancellationToken.None);

        Assert.Equal("deactivated", deactivated.Status);
        Assert.True(deactivated.IsDeactivated);
        Assert.Equal(member.MatrixUserId, fixture.AdminClient.LastDeactivatedUserId);
        Assert.False(fixture.AdminClient.LastDeactivateErase);

        var afterDeactivate = Assert.IsType<RuntimeStackUsersResponse>(
            await fixture.Service.ListAsync(fixture.Manifest.Slug, CancellationToken.None));
        Assert.Equal("deactivated", Assert.Single(
            afterDeactivate.Users,
            x => x.Id == member.Id).Status);

        const string replacementPassword = "reactivated-member-password-456";
        var reactivated = await fixture.Service.ReactivateUserAsync(
            fixture.Manifest.Slug,
            member.Id,
            new ReactivateRuntimeStackUserRequest(replacementPassword),
            CancellationToken.None);

        Assert.Equal("reactivated", reactivated.Status);
        Assert.False(reactivated.IsDeactivated);
        Assert.Equal(member.MatrixUserId, fixture.AdminClient.LastReactivatedUserId);
        Assert.Equal(replacementPassword, fixture.AdminClient.LastReactivationPassword);

        var afterReactivate = Assert.IsType<RuntimeStackUsersResponse>(
            await fixture.Service.ListAsync(fixture.Manifest.Slug, CancellationToken.None));
        Assert.Equal("active", Assert.Single(
            afterReactivate.Users,
            x => x.Id == member.Id).Status);

        var operation = await fixture.Db.RuntimeOperations.AsNoTracking().SingleAsync(
            x => x.Operation == "reactivate-matrix-user");
        var serialized = string.Join("\n", new[]
        {
            operation.InputJson,
            operation.ResultJson,
            operation.EvidenceJson,
            operation.LastError
        }.Where(x => x is not null));
        Assert.DoesNotContain(replacementPassword, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task User_lifecycle_refuses_to_deactivate_last_active_admin()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = await fixture.Service.CreateFirstAdminAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "admin",
                Password: "initial-admin-password-123",
                IsAdmin: true,
                DisplayName: null,
                Email: null),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<MatrixAdminAuthorityException>(() =>
            fixture.Service.DeactivateUserAsync(
                fixture.Manifest.Slug,
                admin.Id,
                new DeactivateRuntimeStackUserRequest(),
                CancellationToken.None));

        Assert.Equal("matrix_user_deactivate_last_admin", ex.Code);
        Assert.Null(fixture.AdminClient.LastDeactivatedUserId);
    }

    [Fact]
    public async Task User_lifecycle_provisions_managed_authority_before_deactivating_its_human_admin()
    {
        await using var fixture = await Fixture.CreateAsync();
        var firstAdmin = await fixture.Service.CreateFirstAdminAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "admin-one",
                Password: "initial-admin-one-password-123",
                IsAdmin: true,
                DisplayName: null,
                Email: null),
            CancellationToken.None);
        await fixture.Service.CreateUserAsync(
            fixture.Manifest.Slug,
            new CreateRuntimeStackUserRequest(
                Username: "admin-two",
                Password: "initial-admin-two-password-123",
                IsAdmin: true,
                DisplayName: null,
                Email: null),
            CancellationToken.None);

        var firstAdminMatrixUserId = Assert.IsType<string>(firstAdmin.MatrixUserId);
        await fixture.Authority.StoreAsync(
            fixture.Manifest.StackId,
            "captured-admin-token",
            firstAdminMatrixUserId,
            "test-first-admin-authority",
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var result = await fixture.Service.DeactivateUserAsync(
            fixture.Manifest.Slug,
            firstAdmin.Id,
            new DeactivateRuntimeStackUserRequest(),
            CancellationToken.None);

        Assert.Equal("deactivated", result.Status);
        Assert.Equal("managed-admin-token", fixture.AdminClient.LastAccessToken);
        Assert.StartsWith(
            MatrixManagedRecoveryAuthorityService.UsernamePrefix,
            fixture.Registration.LastUsername,
            StringComparison.Ordinal);

        var listed = Assert.IsType<RuntimeStackUsersResponse>(
            await fixture.Service.ListAsync(fixture.Manifest.Slug, CancellationToken.None));
        Assert.Equal(1, listed.ActiveAdminCount);
        Assert.DoesNotContain(
            listed.Users,
            x => MatrixManagedRecoveryAuthorityService.IsManagedRecoveryUser(
                x.MatrixUserId ?? string.Empty));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly string _dataRoot;

        private Fixture(
            string databasePath,
            string dataRoot,
            MemDbContext db,
            RuntimeStackManifest manifest,
            FakeInventoryReader reader,
            FakeRegistrationClient registration,
            FakeMatrixAdminAuthorityProtector protector,
            FakeSynapseAdminUserClient adminClient,
            MatrixAdminAuthorityService authority,
            RuntimeStackUserService service)
        {
            _databasePath = databasePath;
            _dataRoot = dataRoot;
            Db = db;
            Manifest = manifest;
            Reader = reader;
            Registration = registration;
            Protector = protector;
            AdminClient = adminClient;
            Authority = authority;
            Service = service;
        }

        public MemDbContext Db { get; }
        public RuntimeStackManifest Manifest { get; }
        public FakeInventoryReader Reader { get; }
        public FakeRegistrationClient Registration { get; }
        public FakeMatrixAdminAuthorityProtector Protector { get; }
        public FakeSynapseAdminUserClient AdminClient { get; }
        public MatrixAdminAuthorityService Authority { get; }
        public RuntimeStackUserService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"mem-runtime-user-creation-{Guid.NewGuid():N}");
            var databasePath = Path.Combine(root, "mem.db");
            var dataRoot = Path.Combine(root, "data");
            Directory.CreateDirectory(root);

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var stackId = Guid.NewGuid();
            var matrixInstanceId = Guid.NewGuid();
            var manifest = new RuntimeStackManifest(
                Source: "control-plane",
                StackId: stackId,
                Slug: "user-creation-stack",
                LastVerifiedStatus: "passed",
                LastVerifiedAtUtc: DateTimeOffset.UtcNow,
                Matrix: new RuntimeStackServiceManifest(
                    InstanceId: matrixInstanceId,
                    ServiceKey: "matrix",
                    ContainerId: "matrix-container",
                    ContainerName: "mem-matrix-user-creation-stack",
                    HostPort: 0,
                    DataPath: Path.Combine(root, "matrix"),
                    ServerName: "restored.test",
                    PublicHost: "matrix.restored.test",
                    PublicBaseUrl: "https://matrix.restored.test",
                    InternalHost: "mem-matrix-user-creation-stack",
                    InternalBaseUrl: "http://mem-matrix-user-creation-stack:8008",
                    PublicRouteId: null,
                    InternalRouteId: null,
                    NpmCertificateId: null,
                    RuntimeMetadata: new Dictionary<string, string?>()),
                Element: null,
                Warnings: [],
                Metadata: new Dictionary<string, string?>());

            db.RuntimeStacks.Add(new RuntimeStackEntity
            {
                Id = stackId,
                Slug = manifest.Slug,
                DisplayName = manifest.Slug,
                Status = "passed",
                LastVerifiedStatus = "passed",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                MatrixInstanceId = matrixInstanceId,
                MatrixPublicBaseUrl = manifest.Matrix.PublicBaseUrl
            });
            db.RuntimeStackDatabases.Add(new RuntimeStackDatabaseEntity
            {
                Id = Guid.NewGuid(),
                RuntimeStackId = stackId,
                DatabaseEngine = "postgres",
                DatabaseHost = "mem-postgres",
                DatabasePort = 5432,
                DatabaseName = "matrix_user_creation_stack_12345678",
                DatabaseUsername = "mxu_user_creation_stack_12345678",
                PasswordSecretKind = "matrix_postgres_password",
                Status = "active",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                MetadataJson = "{\"purpose\":\"synapse-database\",\"ownership\":\"one-postgres-database-per-runtime-stack\"}"
            });
            await db.SaveChangesAsync();

            var manifestDirectory = Path.Combine(dataRoot, "control-plane", "runtime-stacks");
            Directory.CreateDirectory(manifestDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(manifestDirectory, $"{stackId:N}.json"),
                JsonSerializer.Serialize(manifest, JsonOptions()));

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HostAgent:DataRoot"] = dataRoot
                })
                .Build();
            var manifestStore = new RuntimeStackManifestStore(configuration, db);
            var reader = new FakeInventoryReader();
            var reconciliation = new RuntimeStackUserInventoryReconciliationService(
                db,
                reader,
                NullLogger<RuntimeStackUserInventoryReconciliationService>.Instance);
            var registration = new FakeRegistrationClient(reader, manifest.Matrix.ServerName!);
            var protector = new FakeMatrixAdminAuthorityProtector();
            var adminClient = new FakeSynapseAdminUserClient(reader);
            var authority = new MatrixAdminAuthorityService(db, protector);
            var bootstrapOptions = Options.Create(new MatrixBootstrapOptions
            {
                SharedSecret = "integration-test-shared-secret"
            });
            var secretService = new RuntimeStackSecretService(db);
            var managedAuthority = new MatrixManagedRecoveryAuthorityService(
                secretService,
                registration,
                adminClient,
                authority,
                bootstrapOptions);
            var service = new RuntimeStackUserService(
                db,
                manifestStore,
                reconciliation,
                registration,
                new RuntimeOperationStore(db),
                secretService,
                authority,
                managedAuthority,
                adminClient,
                bootstrapOptions);

            return new Fixture(
                databasePath,
                dataRoot,
                db,
                manifest,
                reader,
                registration,
                protector,
                adminClient,
                authority,
                service);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();

            var root = Directory.GetParent(_dataRoot)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            else
            {
                foreach (var path in new[]
                         {
                             _databasePath,
                             _databasePath + "-shm",
                             _databasePath + "-wal"
                         })
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }
        }
    }

    private sealed class FakeInventoryReader : ISynapseUserInventoryReader
    {
        private readonly List<SynapseUserInventoryAccount> _accounts = [];

        public RuntimeStackUserInventoryException? Exception { get; set; }

        public void Upsert(SynapseUserInventoryAccount account)
        {
            _accounts.RemoveAll(x =>
                string.Equals(x.Username, account.Username, StringComparison.OrdinalIgnoreCase));
            _accounts.Add(account);
        }

        public void SetDeactivated(string matrixUserId, bool isDeactivated)
        {
            var index = _accounts.FindIndex(x => string.Equals(
                x.MatrixUserId,
                matrixUserId,
                StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new InvalidOperationException($"Fake Matrix user '{matrixUserId}' was not found.");
            }

            var current = _accounts[index];
            _accounts[index] = current with { IsDeactivated = isDeactivated };
        }

        public Task<SynapseUserInventorySnapshot> ReadAsync(
            RuntimeStackUserInventoryTarget target,
            CancellationToken ct)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(new SynapseUserInventorySnapshot(
                Source: "synapse-postgres",
                Accounts: _accounts
                    .Where(x => !MatrixManagedRecoveryAuthorityService.IsManagedRecoveryUser(
                        x.MatrixUserId))
                    .ToArray(),
                ReadAtUtc: DateTimeOffset.UtcNow));
        }
    }

    private sealed class FakeRegistrationClient(
        FakeInventoryReader reader,
        string serverName)
        : ISynapseSharedSecretRegistrationClient
    {
        public bool FailInventoryAfterRegistration { get; set; }
        public Exception? Exception { get; set; }
        public string? LastUsername { get; private set; }
        public string? LastPassword { get; private set; }
        public bool LastAdmin { get; private set; }
        public int CallCount { get; private set; }

        public Task<SynapseSharedSecretRegistrationResult> RegisterAsync(
            string homeserverBaseUrl,
            string username,
            string password,
            bool admin,
            string sharedSecret,
            CancellationToken ct)
        {
            CallCount += 1;
            LastUsername = username;

            if (Exception is not null)
            {
                throw Exception;
            }
            LastPassword = password;
            LastAdmin = admin;
            var managedRecovery = username.StartsWith(
                MatrixManagedRecoveryAuthorityService.UsernamePrefix,
                StringComparison.Ordinal);
            var registeredUsername = managedRecovery
                ? "mem_recovery_test"
                : username;
            var matrixUserId = $"@{registeredUsername}:{serverName}";
            reader.Upsert(new SynapseUserInventoryAccount(
                MatrixUserId: matrixUserId,
                Username: registeredUsername,
                IsAdmin: admin,
                IsDeactivated: false,
                CreatedAtUtc: DateTime.UtcNow));

            if (FailInventoryAfterRegistration)
            {
                reader.Exception = new RuntimeStackUserInventoryException(
                    "matrix_user_inventory_query_failed",
                    "The Matrix user inventory could not be read after account creation.");
            }

            var accessToken = admin
                ? managedRecovery
                    ? "managed-admin-token"
                    : "captured-admin-token"
                : null;

            return Task.FromResult(new SynapseSharedSecretRegistrationResult(
                UserId: matrixUserId,
                AccessToken: accessToken,
                HomeServer: serverName));
        }
    }

    public sealed class FakeMatrixAdminAuthorityProtector : IMatrixAdminAuthorityProtector
    {
        private readonly Dictionary<string, string> _protectedValues =
            new(StringComparer.Ordinal);

        public bool ThrowOnProtect { get; set; }

        public string Protect(string accessToken)
        {
            if (ThrowOnProtect)
            {
                throw new System.Security.Cryptography.CryptographicException("Test protection failure.");
            }

            var protectedValue = $"protected::{Guid.NewGuid():N}";
            _protectedValues[protectedValue] = accessToken;
            return protectedValue;
        }

        public string Unprotect(string protectedAccessToken) =>
            _protectedValues.TryGetValue(protectedAccessToken, out var accessToken)
                ? accessToken
                : throw new System.Security.Cryptography.CryptographicException("Invalid protected test value.");
    }

    private sealed class FakeSynapseAdminUserClient(FakeInventoryReader reader)
        : ISynapseAdminUserClient
    {
        public string? LastValidatedAccessToken { get; private set; }
        public string? LastResetUserId { get; private set; }
        public string? LastPassword { get; private set; }
        public string? LastAccessToken { get; private set; }
        public bool LastLogoutDevices { get; private set; }
        public string? LastLoginUserId { get; private set; }
        public string? LastLoginPassword { get; private set; }
        public string? LastDeactivatedUserId { get; private set; }
        public bool LastDeactivateErase { get; private set; }
        public string? LastReactivatedUserId { get; private set; }
        public string? LastReactivationPassword { get; private set; }
        public MatrixAdminAuthorityException? ResetException { get; set; }
        public int ResetFailuresRemaining { get; set; }
        public MatrixAdminAuthorityException? LoginException { get; set; }

        public Task<MatrixAdminAuthorityIdentity> ValidateAuthorityAsync(
            string homeserverBaseUrl,
            string accessToken,
            CancellationToken ct)
        {
            LastValidatedAccessToken = accessToken;
            var matrixUserId = string.Equals(
                accessToken,
                "managed-admin-token",
                StringComparison.Ordinal)
                ? "@mem_recovery_test:restored.test"
                : "@existing-admin:restored.test";

            return Task.FromResult(new MatrixAdminAuthorityIdentity(
                matrixUserId,
                IsAdmin: true,
                IsDeactivated: false));
        }

        public Task<MatrixAdminAuthorityLoginResult> LoginAndValidateAuthorityAsync(
            string homeserverBaseUrl,
            string matrixUserId,
            string password,
            CancellationToken ct)
        {
            LastLoginUserId = matrixUserId;
            LastLoginPassword = password;

            if (LoginException is not null)
            {
                throw LoginException;
            }

            var token = string.Equals(
                password,
                "replacement-password-456",
                StringComparison.Ordinal)
                ? "rotated-admin-token"
                : "login-admin-token";

            return Task.FromResult(new MatrixAdminAuthorityLoginResult(
                token,
                new MatrixAdminAuthorityIdentity(
                    matrixUserId,
                    IsAdmin: true,
                    IsDeactivated: false)));
        }

        public Task ResetPasswordAsync(
            string homeserverBaseUrl,
            string matrixUserId,
            string newPassword,
            string accessToken,
            bool logoutDevices,
            CancellationToken ct)
        {
            if (ResetException is not null && ResetFailuresRemaining != 0)
            {
                if (ResetFailuresRemaining > 0)
                {
                    ResetFailuresRemaining -= 1;
                }

                throw ResetException;
            }

            LastResetUserId = matrixUserId;
            LastPassword = newPassword;
            LastAccessToken = accessToken;
            LastLogoutDevices = logoutDevices;
            return Task.CompletedTask;
        }

        public Task DeactivateUserAsync(
            string homeserverBaseUrl,
            string matrixUserId,
            string accessToken,
            bool erase,
            CancellationToken ct)
        {
            LastDeactivatedUserId = matrixUserId;
            LastDeactivateErase = erase;
            LastAccessToken = accessToken;
            reader.SetDeactivated(matrixUserId, true);
            return Task.CompletedTask;
        }

        public Task ReactivateUserAsync(
            string homeserverBaseUrl,
            string matrixUserId,
            string newPassword,
            string accessToken,
            CancellationToken ct)
        {
            LastReactivatedUserId = matrixUserId;
            LastReactivationPassword = newPassword;
            LastAccessToken = accessToken;
            reader.SetDeactivated(matrixUserId, false);
            return Task.CompletedTask;
        }
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
