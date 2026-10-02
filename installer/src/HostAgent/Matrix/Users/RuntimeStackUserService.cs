using System.Text.Json;
using System.Text.Json.Serialization;
using HostAgent.Matrix.Provisioning;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Operations;
using HostAgent.Runtime.Secrets;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HostAgent.Matrix.Users;

public sealed class RuntimeStackUserService
{
    private const string UserCreationFailureDetail =
        "Matrix user creation did not complete. Open Diagnostics for the recorded failure before retrying.";

    private readonly MemDbContext _db;
    private readonly RuntimeStackManifestStore _manifestStore;
    private readonly RuntimeStackUserInventoryReconciliationService _inventoryReconciliation;
    private readonly ISynapseSharedSecretRegistrationClient _registrationClient;
    private readonly RuntimeOperationStore _operationStore;
    private readonly RuntimeStackSecretService _secretService;
    private readonly MatrixAdminAuthorityService _adminAuthority;
    private readonly MatrixManagedRecoveryAuthorityService _managedRecoveryAuthority;
    private readonly ISynapseAdminUserClient _adminUserClient;
    private readonly MatrixBootstrapOptions _bootstrapOptions;

    public RuntimeStackUserService(
        MemDbContext db,
        RuntimeStackManifestStore manifestStore,
        RuntimeStackUserInventoryReconciliationService inventoryReconciliation,
        ISynapseSharedSecretRegistrationClient registrationClient,
        RuntimeOperationStore operationStore,
        RuntimeStackSecretService secretService,
        MatrixAdminAuthorityService adminAuthority,
        MatrixManagedRecoveryAuthorityService managedRecoveryAuthority,
        ISynapseAdminUserClient adminUserClient,
        IOptions<MatrixBootstrapOptions> bootstrapOptions)
    {
        _db = db;
        _manifestStore = manifestStore;
        _inventoryReconciliation = inventoryReconciliation;
        _registrationClient = registrationClient;
        _operationStore = operationStore;
        _secretService = secretService;
        _adminAuthority = adminAuthority;
        _managedRecoveryAuthority = managedRecoveryAuthority;
        _adminUserClient = adminUserClient;
        _bootstrapOptions = bootstrapOptions.Value;
    }

    public async Task<RuntimeStackUsersResponse?> ListAsync(
        string slugOrId,
        CancellationToken ct)
    {
        var manifest = await _manifestStore.FindAsync(slugOrId, ct);

        if (manifest is null)
        {
            return null;
        }

        var users = await _db.Set<RuntimeStackUserEntity>()
            .AsNoTracking()
            .Where(x => x.RuntimeStackId == manifest.StackId)
            .OrderByDescending(x => x.IsFirstAdmin)
            .ThenBy(x => x.Username)
            .ToArrayAsync(ct);

        var database = await _db.RuntimeStackDatabases
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RuntimeStackId == manifest.StackId, ct);

        var inventory = RuntimeStackUserInventoryMetadata.Read(database?.MetadataJson);
        var adminAuthority = await _adminAuthority.GetStatusAsync(manifest.StackId, ct);
        var projectedUsers = users
            .Select(RuntimeStackUserResponse.FromEntity)
            .ToArray();
        var activeAdminCount = projectedUsers.Count(x =>
            x.IsAdmin && string.Equals(x.Status, "active", StringComparison.Ordinal));
        var inventoryUserCount = projectedUsers.Count(x =>
            x.Status is "active" or "deactivated");
        var metadataSynchronized = string.Equals(
            inventory.Status,
            RuntimeStackUserInventoryStates.Synchronized,
            StringComparison.Ordinal);
        var projectionMatchesInventory =
            inventory.UserCount == inventoryUserCount &&
            inventory.ActiveAdminCount == activeAdminCount;
        var synchronized = metadataSynchronized && projectionMatchesInventory;
        var effectiveInventoryStatus = metadataSynchronized && !projectionMatchesInventory
            ? RuntimeStackUserInventoryStates.NotSynchronized
            : inventory.Status;
        var effectiveErrorCode = metadataSynchronized && !projectionMatchesInventory
            ? "matrix_user_projection_stale"
            : inventory.ErrorCode;

        return new RuntimeStackUsersResponse(
            Source: "control-plane",
            Status: "ok",
            StackId: manifest.StackId,
            Slug: manifest.Slug,
            InventorySource: inventory.Source,
            InventoryStatus: effectiveInventoryStatus,
            InventoryLastAttemptedAtUtc: inventory.LastAttemptedAtUtc,
            InventoryLastSynchronizedAtUtc: inventory.LastSynchronizedAtUtc,
            InventoryUserCount: metadataSynchronized ? inventoryUserCount : inventory.UserCount,
            ActiveAdminCount: activeAdminCount,
            InventoryErrorCode: effectiveErrorCode,
            SynchronizationRequired: !synchronized,
            Users: projectedUsers,
            RequiresFirstAdmin: synchronized && activeAdminCount == 0,
            CanCreateUsers: synchronized,
            AdminAuthority: adminAuthority,
            Detail: inventory.Status == RuntimeStackUserInventoryStates.Failed
                ? "Matrix user inventory synchronization failed. Retry synchronization before creating users."
                : metadataSynchronized && !projectionMatchesInventory
                    ? "The MEM Matrix user projection is stale. Synchronize users before creating accounts."
                    : null);
    }

    public async Task<RuntimeStackUsersResponse?> SynchronizeAsync(
        string slugOrId,
        CancellationToken ct)
    {
        var manifest = await _manifestStore.FindAsync(slugOrId, ct);

        if (manifest is null)
        {
            return null;
        }

        await _inventoryReconciliation.SynchronizeAsync(manifest, ct);

        return await ListAsync(slugOrId, ct);
    }

    public Task<RuntimeStackUserResponse> CreateFirstAdminAsync(
        string slugOrId,
        CreateRuntimeStackUserRequest request,
        CancellationToken ct)
    {
        return CreateAsync(
            slugOrId,
            request with { IsAdmin = true },
            isFirstAdmin: true,
            ct);
    }

    public Task<RuntimeStackUserResponse> CreateUserAsync(
        string slugOrId,
        CreateRuntimeStackUserRequest request,
        CancellationToken ct)
    {
        return CreateAsync(
            slugOrId,
            request,
            isFirstAdmin: false,
            ct);
    }

    private async Task<RuntimeStackUserResponse> CreateAsync(
        string slugOrId,
        CreateRuntimeStackUserRequest request,
        bool isFirstAdmin,
        CancellationToken ct)
    {
        var manifest = await _manifestStore.FindAsync(slugOrId, ct);

        if (manifest is null)
        {
            throw new InvalidOperationException(
                $"Runtime stack '{slugOrId}' was not found in the local control-plane manifest store.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Matrix.PublicBaseUrl))
        {
            throw new InvalidOperationException("Runtime stack Matrix public base URL is missing.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Matrix.ServerName))
        {
            throw new InvalidOperationException("Runtime stack Matrix server name is missing.");
        }

        var username = NormalizeUsername(request.Username);

        if (username.Length == 0)
        {
            throw new InvalidOperationException("Matrix username is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            throw new InvalidOperationException("Matrix password must be at least 8 characters.");
        }

        var stackExists = await _db.RuntimeStacks
            .AnyAsync(x => x.Id == manifest.StackId, ct);

        if (!stackExists)
        {
            throw new InvalidOperationException(
                $"RuntimeStack '{manifest.StackId}' was not found. Run stack creation/verification first.");
        }

        var inventory = await _inventoryReconciliation.SynchronizeAsync(manifest, ct);
        RuntimeStackUserCreationPolicy.EnsureAllowed(inventory, username, isFirstAdmin);

        var existingUsername = await _db.Set<RuntimeStackUserEntity>()
            .AnyAsync(x => x.RuntimeStackId == manifest.StackId && x.Username == username, ct);

        if (existingUsername)
        {
            throw new RuntimeStackUserConflictException(
                "matrix_user_projection_exists",
                $"Matrix user '{username}' already exists in MEM records for stack '{manifest.Slug}'.");
        }

        var perStackSharedSecret = await _secretService.GetMatrixRegistrationSharedSecretAsync(
            manifest.StackId,
            ct);

        var sharedSecret = (perStackSharedSecret ?? _bootstrapOptions.SharedSecret ?? string.Empty)
            .Trim();

        var sharedSecretSource = string.IsNullOrWhiteSpace(perStackSharedSecret)
            ? "global-fallback"
            : "per-stack";

        if (sharedSecret.Length == 0)
        {
            throw new InvalidOperationException(
                "Matrix registration shared secret is missing. It is required for Synapse shared-secret registration.");
        }

        var operationName = isFirstAdmin
            ? "create-first-matrix-admin"
            : "create-matrix-user";

        var operationId = await _operationStore.StartAsync(
            runtimeStackId: manifest.StackId,
            operation: operationName,
            idempotencyKey: null,
            requestedBy: "control-plane-ui",
            hostMutationLevel: "matrix-shared-secret-registration",
            input: new
            {
                manifest.StackId,
                manifest.Slug,
                MatrixInstanceId = manifest.Matrix.InstanceId,
                MatrixServerName = manifest.Matrix.ServerName,
                MatrixPublicBaseUrl = manifest.Matrix.PublicBaseUrl,
                Username = username,
                DisplayName = NullIfWhiteSpace(request.DisplayName),
                Email = NullIfWhiteSpace(request.Email),
                IsAdmin = request.IsAdmin || isFirstAdmin,
                IsFirstAdmin = isFirstAdmin,
                SecretSource = sharedSecretSource
            },
            ct);

        RuntimeStackUserEntity? entity = null;
        RuntimeStackUserResponse response;
        string? registrationAccessToken = null;

        try
        {
            var now = DateTimeOffset.UtcNow;

            entity = new RuntimeStackUserEntity
            {
                Id = Guid.NewGuid(),
                RuntimeStackId = manifest.StackId,
                MatrixInstanceId = manifest.Matrix.InstanceId,
                Username = username,
                MatrixUserId = null,
                IsAdmin = request.IsAdmin || isFirstAdmin,
                IsFirstAdmin = isFirstAdmin,
                Status = "pending",
                DisplayName = NullIfWhiteSpace(request.DisplayName),
                Email = NullIfWhiteSpace(request.Email),
                LastError = null,
                CreatedAtUtc = now.UtcDateTime,
                UpdatedAtUtc = now.UtcDateTime,
                MatrixSyncedAtUtc = null,
                MetadataJson = JsonSerializer.Serialize(new Dictionary<string, string?>
                {
                    ["origin"] = RuntimeStackUserProjectionMetadata.MemCreated,
                    ["matrixServerName"] = manifest.Matrix.ServerName,
                    ["matrixPublicBaseUrl"] = manifest.Matrix.PublicBaseUrl,
                    ["operationId"] = operationId.ToString(),
                    ["registrationSecretSource"] = sharedSecretSource
                }, JsonOptions())
            };

            _db.Set<RuntimeStackUserEntity>().Add(entity);

            await _db.SaveChangesAsync(ct);

            var registerResult = await _registrationClient.RegisterAsync(
                homeserverBaseUrl: manifest.Matrix.PublicBaseUrl,
                username: username,
                password: request.Password,
                admin: entity.IsAdmin,
                sharedSecret: sharedSecret,
                ct);

            entity.MatrixUserId = registerResult.UserId;
            registrationAccessToken = registerResult.AccessToken;
            entity.Status = "active";
            entity.MatrixSyncedAtUtc = DateTimeOffset.UtcNow.UtcDateTime;
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow.UtcDateTime;
            entity.LastError = null;

            await _db.SaveChangesAsync(ct);

            response = RuntimeStackUserResponse.FromEntity(entity);
        }
        catch (Exception ex)
        {
            if (entity is not null)
            {
                try
                {
                    entity.Status = "failed";
                    entity.LastError = UserCreationFailureDetail;
                    entity.UpdatedAtUtc = DateTimeOffset.UtcNow.UtcDateTime;

                    await _db.SaveChangesAsync(ct);
                }
                catch
                {
                    // Keep the original exception as the important failure.
                }
            }

            await _operationStore.FailAsync(
                operationId,
                currentStep: "failed",
                error: UserCreationFailureDetail,
                evidence: new
                {
                    manifest.StackId,
                    manifest.Slug,
                    MatrixInstanceId = manifest.Matrix.InstanceId,
                    Username = username,
                    IsFirstAdmin = isFirstAdmin,
                    SecretSource = sharedSecretSource
                },
                ct);

            throw;
        }

        var authorityCapture = await CaptureAuthorityAfterCreationAsync(
            manifest,
            response,
            registrationAccessToken,
            ct);
        var inventoryRefresh = await RefreshInventoryAfterCreationAsync(manifest, ct);

        await _operationStore.CompleteAsync(
            operationId,
            status: "active",
            currentStep: "completed",
            result: new
            {
                response.Id,
                response.RuntimeStackId,
                response.MatrixInstanceId,
                response.Username,
                response.MatrixUserId,
                response.IsAdmin,
                response.IsFirstAdmin,
                response.Status,
                response.Origin,
                SecretSource = sharedSecretSource,
                InventoryStatus = inventoryRefresh.Status,
                InventoryErrorCode = inventoryRefresh.ErrorCode,
                AdminAuthorityStatus = authorityCapture.Status,
                AdminAuthorityErrorCode = authorityCapture.ErrorCode
            },
            evidence: new
            {
                response.MatrixUserId,
                response.MatrixSyncedAtUtc,
                RegistrationMethod = "shared-secret",
                SecretSource = sharedSecretSource,
                InventoryStatus = inventoryRefresh.Status,
                inventoryRefresh.UserCount,
                inventoryRefresh.ActiveAdminCount,
                InventoryErrorCode = inventoryRefresh.ErrorCode,
                AdminAuthorityStatus = authorityCapture.Status,
                AdminAuthorityErrorCode = authorityCapture.ErrorCode
            },
            ct);

        return response;
    }


    public async Task<MatrixAdminAuthorityStatus> ImportAdminAuthorityAsync(
        string slugOrId,
        ImportMatrixAdminAuthorityRequest request,
        CancellationToken ct)
    {
        var manifest = await RequireManifestAsync(slugOrId, ct);
        var hasToken = !string.IsNullOrWhiteSpace(request.AccessToken);
        var hasUserId = !string.IsNullOrWhiteSpace(request.MatrixUserId);
        var hasPassword = !string.IsNullOrWhiteSpace(request.Password);

        if (hasToken && (hasUserId || hasPassword))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_admin_authority_request_ambiguous",
                "Provide either a Matrix administrator access token or administrator credentials, not both.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        if (!hasToken && (!hasUserId || !hasPassword))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_admin_authority_credentials_required",
                "Provide a Matrix administrator access token or both the administrator Matrix user ID and password.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        var source = hasToken
            ? "operator-supplied"
            : "operator-admin-login";
        var operationId = await _operationStore.StartAsync(
            manifest.StackId,
            "set-matrix-admin-authority",
            null,
            "control-plane-ui",
            "matrix-admin-authority",
            new { manifest.StackId, manifest.Slug, Source = source },
            ct);

        try
        {
            MatrixAdminAuthorityIdentity identity;
            string accessToken;

            if (hasToken)
            {
                accessToken = request.AccessToken!.Trim();
                identity = await _adminUserClient.ValidateAuthorityAsync(
                    manifest.Matrix.PublicBaseUrl!,
                    accessToken,
                    ct);
            }
            else
            {
                var login = await _adminUserClient.LoginAndValidateAuthorityAsync(
                    manifest.Matrix.PublicBaseUrl!,
                    request.MatrixUserId!,
                    request.Password!,
                    ct);
                accessToken = login.AccessToken;
                identity = login.Identity;
            }

            EnsureLocalMatrixUser(identity.MatrixUserId, manifest.Matrix.ServerName!);
            var status = await _adminAuthority.StoreAsync(
                manifest.StackId,
                accessToken,
                identity.MatrixUserId,
                source,
                DateTimeOffset.UtcNow,
                ct);

            await _operationStore.CompleteAsync(
                operationId,
                "active",
                "completed",
                new { status.Status, status.AdminUserId, status.Source },
                new { status.AdminUserId, status.LastValidatedAtUtc },
                ct);
            return status;
        }
        catch (Exception ex)
        {
            var safeDetail = ex is MatrixAdminAuthorityException authorityError
                ? authorityError.SafeDetail
                : "Matrix administrator authority could not be validated or stored.";
            var errorCode = ex is MatrixAdminAuthorityException codedError
                ? codedError.Code
                : "matrix_admin_authority_store_failed";

            await _operationStore.FailAsync(
                operationId,
                "failed",
                safeDetail,
                new { ErrorCode = errorCode },
                ct);
            throw;
        }
    }

    public async Task<RuntimeStackUserPasswordResetResponse> ResetPasswordAsync(
        string slugOrId,
        Guid userId,
        ResetRuntimeStackUserPasswordRequest request,
        CancellationToken ct)
    {
        var manifest = await RequireManifestAsync(slugOrId, ct);
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            throw new MatrixAdminAuthorityException(
                "matrix_password_reset_password_invalid",
                "The new Matrix password must be at least 8 characters.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        var user = await _db.RuntimeStackUsers.FirstOrDefaultAsync(
            x => x.Id == userId && x.RuntimeStackId == manifest.StackId, ct);
        if (user is null || string.IsNullOrWhiteSpace(user.MatrixUserId))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_password_reset_user_not_found",
                "The Matrix user was not found in this runtime stack.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }
        if (!string.Equals(user.Status, "active", StringComparison.Ordinal))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_password_reset_user_inactive",
                "Password reset is available only for active Matrix users.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        var operationId = await _operationStore.StartAsync(
            manifest.StackId,
            "reset-matrix-user-password",
            null,
            "control-plane-ui",
            "matrix-admin-api",
            new
            {
                manifest.StackId,
                manifest.Slug,
                user.Id,
                user.MatrixUserId,
                LogoutDevices = true,
                AuthorityMode = "automatic"
            },
            ct);

        try
        {
            var credential = await _managedRecoveryAuthority.GetOrProvisionAsync(
                manifest,
                forceManagedReplacement: false,
                ct);
            var authorityRecovered = false;

            try
            {
                await _adminUserClient.ResetPasswordAsync(
                    manifest.Matrix.PublicBaseUrl!,
                    user.MatrixUserId,
                    request.NewPassword,
                    credential.AccessToken,
                    logoutDevices: true,
                    ct);
            }
            catch (MatrixAdminAuthorityException ex)
                when (ex.FailureKind == MatrixAdminAuthorityFailureKind.Rejected)
            {
                try
                {
                    await _adminAuthority.MarkInvalidAsync(manifest.StackId, ex.Code, ct);
                }
                catch
                {
                    // Continue to the managed recovery path; the rejected token is not reused.
                }

                credential = await _managedRecoveryAuthority.GetOrProvisionAsync(
                    manifest,
                    forceManagedReplacement: true,
                    ct);
                authorityRecovered = true;

                await _adminUserClient.ResetPasswordAsync(
                    manifest.Matrix.PublicBaseUrl!,
                    user.MatrixUserId,
                    request.NewPassword,
                    credential.AccessToken,
                    logoutDevices: true,
                    ct);
            }

            var isAuthorityAdmin = string.Equals(
                credential.AdminUserId,
                user.MatrixUserId,
                StringComparison.OrdinalIgnoreCase);
            var authorityInvalidated = false;
            var authorityRotated = false;
            var managedAuthorityProvisioned = authorityRecovered ||
                string.Equals(
                    credential.Source,
                    MatrixManagedRecoveryAuthorityService.AuthoritySource,
                    StringComparison.Ordinal);
            string? authorityRotationErrorCode = null;

            if (isAuthorityAdmin)
            {
                try
                {
                    var replacement = await _adminUserClient.LoginAndValidateAuthorityAsync(
                        manifest.Matrix.PublicBaseUrl!,
                        user.MatrixUserId,
                        request.NewPassword,
                        ct);
                    EnsureLocalMatrixUser(
                        replacement.Identity.MatrixUserId,
                        manifest.Matrix.ServerName!);
                    await _adminAuthority.StoreAsync(
                        manifest.StackId,
                        replacement.AccessToken,
                        replacement.Identity.MatrixUserId,
                        "password-reset-rotation",
                        DateTimeOffset.UtcNow,
                        ct);
                    authorityRotated = true;
                }
                catch (Exception rotationError)
                {
                    authorityRotationErrorCode = rotationError is MatrixAdminAuthorityException coded
                        ? coded.Code
                        : "matrix_admin_authority_rotation_failed";

                    try
                    {
                        await _adminAuthority.MarkInvalidAsync(
                            manifest.StackId,
                            authorityRotationErrorCode,
                            ct);

                        credential = await _managedRecoveryAuthority.GetOrProvisionAsync(
                            manifest,
                            forceManagedReplacement: true,
                            ct);
                        managedAuthorityProvisioned = true;
                        authorityInvalidated = false;
                    }
                    catch
                    {
                        authorityInvalidated = true;
                    }
                }
            }

            var completedAt = DateTimeOffset.UtcNow.UtcDateTime;
            await _operationStore.CompleteAsync(
                operationId,
                "active",
                "completed",
                new
                {
                    user.Id,
                    user.MatrixUserId,
                    LogoutDevices = true,
                    AdminAuthorityRotated = authorityRotated,
                    ManagedAuthorityProvisioned = managedAuthorityProvisioned,
                    AdminAuthorityInvalidated = authorityInvalidated,
                    AdminAuthorityRotationErrorCode = authorityRotationErrorCode,
                    CompletedAtUtc = completedAt
                },
                new
                {
                    user.MatrixUserId,
                    LogoutDevices = true,
                    AdminAuthorityRotated = authorityRotated,
                    ManagedAuthorityProvisioned = managedAuthorityProvisioned,
                    AdminAuthorityInvalidated = authorityInvalidated,
                    AdminAuthorityRotationErrorCode = authorityRotationErrorCode,
                    CompletedAtUtc = completedAt
                },
                ct);
            return new RuntimeStackUserPasswordResetResponse(
                "control-plane",
                "password_reset",
                manifest.StackId,
                user.Id,
                user.MatrixUserId,
                true,
                authorityInvalidated,
                completedAt);
        }
        catch (MatrixAdminAuthorityException ex)
        {
            if (ex.FailureKind == MatrixAdminAuthorityFailureKind.Rejected)
            {
                try
                {
                    await _adminAuthority.MarkInvalidAsync(manifest.StackId, ex.Code, ct);
                }
                catch
                {
                    // Preserve the authoritative Synapse rejection as the primary failure.
                }
            }
            await _operationStore.FailAsync(
                operationId,
                "failed",
                ex.SafeDetail,
                new { user.MatrixUserId, ErrorCode = ex.Code },
                ct);
            throw;
        }
    }

    public async Task<RuntimeStackUserLifecycleResponse> DeactivateUserAsync(
        string slugOrId,
        Guid userId,
        DeactivateRuntimeStackUserRequest request,
        CancellationToken ct)
    {
        var manifest = await RequireManifestAsync(slugOrId, ct);
        var inventory = await _inventoryReconciliation.SynchronizeAsync(manifest, ct);
        var user = await RequireProjectedUserAsync(manifest.StackId, userId, ct);
        var matrixUserId = RequireLifecycleMatrixUser(user);
        var account = RequireInventoryAccount(inventory, matrixUserId);

        if (account.IsDeactivated || !string.Equals(user.Status, "active", StringComparison.Ordinal))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_user_already_deactivated",
                "The Matrix account is already deactivated.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        if (account.IsAdmin && inventory.ActiveAdminCount <= 1)
        {
            throw new MatrixAdminAuthorityException(
                "matrix_user_deactivate_last_admin",
                "The last active Matrix administrator cannot be deactivated.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        var operationId = await _operationStore.StartAsync(
            manifest.StackId,
            "deactivate-matrix-user",
            null,
            "control-plane-ui",
            "matrix-admin-api",
            new
            {
                manifest.StackId,
                manifest.Slug,
                user.Id,
                MatrixUserId = matrixUserId,
                EraseProfile = request.Erase
            },
            ct);

        try
        {
            var credential = await _managedRecoveryAuthority.GetOrProvisionAsync(
                manifest,
                forceManagedReplacement: false,
                ct);
            var authorityRecovered = false;

            if (string.Equals(
                    credential.AdminUserId,
                    matrixUserId,
                    StringComparison.OrdinalIgnoreCase))
            {
                credential = await _managedRecoveryAuthority.GetOrProvisionAsync(
                    manifest,
                    forceManagedReplacement: true,
                    ct);
                authorityRecovered = true;
            }

            try
            {
                await _adminUserClient.DeactivateUserAsync(
                    manifest.Matrix.PublicBaseUrl!,
                    matrixUserId,
                    credential.AccessToken,
                    request.Erase,
                    ct);
            }
            catch (MatrixAdminAuthorityException ex)
                when (ex.FailureKind == MatrixAdminAuthorityFailureKind.Rejected)
            {
                try
                {
                    await _adminAuthority.MarkInvalidAsync(manifest.StackId, ex.Code, ct);
                }
                catch
                {
                    // The rejected token is not reused.
                }

                credential = await _managedRecoveryAuthority.GetOrProvisionAsync(
                    manifest,
                    forceManagedReplacement: true,
                    ct);
                authorityRecovered = true;

                await _adminUserClient.DeactivateUserAsync(
                    manifest.Matrix.PublicBaseUrl!,
                    matrixUserId,
                    credential.AccessToken,
                    request.Erase,
                    ct);
            }

            var completedAt = DateTimeOffset.UtcNow.UtcDateTime;
            user.Status = "deactivated";
            user.LastError = null;
            user.UpdatedAtUtc = completedAt;
            user.MatrixSyncedAtUtc = completedAt;
            await _db.SaveChangesAsync(ct);

            var refresh = await RefreshInventoryAfterLifecycleMutationAsync(manifest, ct);

            await _operationStore.CompleteAsync(
                operationId,
                "active",
                "completed",
                new
                {
                    user.Id,
                    MatrixUserId = matrixUserId,
                    Deactivated = true,
                    EraseProfile = request.Erase,
                    AuthorityRecovered = authorityRecovered,
                    InventoryStatus = refresh.Status,
                    InventoryErrorCode = refresh.ErrorCode,
                    CompletedAtUtc = completedAt
                },
                new
                {
                    MatrixUserId = matrixUserId,
                    Deactivated = true,
                    EraseProfile = request.Erase,
                    AuthorityRecovered = authorityRecovered,
                    InventoryStatus = refresh.Status,
                    InventoryErrorCode = refresh.ErrorCode,
                    CompletedAtUtc = completedAt
                },
                ct);

            return new RuntimeStackUserLifecycleResponse(
                Source: "control-plane",
                Status: "deactivated",
                RuntimeStackId: manifest.StackId,
                UserId: user.Id,
                MatrixUserId: matrixUserId,
                IsDeactivated: true,
                LogoutDevices: true,
                CompletedAtUtc: completedAt);
        }
        catch (MatrixAdminAuthorityException ex)
        {
            await _operationStore.FailAsync(
                operationId,
                "failed",
                ex.SafeDetail,
                new { MatrixUserId = matrixUserId, ErrorCode = ex.Code },
                ct);
            throw;
        }
    }

    public async Task<RuntimeStackUserLifecycleResponse> ReactivateUserAsync(
        string slugOrId,
        Guid userId,
        ReactivateRuntimeStackUserRequest request,
        CancellationToken ct)
    {
        var manifest = await RequireManifestAsync(slugOrId, ct);
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            throw new MatrixAdminAuthorityException(
                "matrix_user_reactivation_password_invalid",
                "The new Matrix password must be at least 8 characters.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        var inventory = await _inventoryReconciliation.SynchronizeAsync(manifest, ct);
        var user = await RequireProjectedUserAsync(manifest.StackId, userId, ct);
        var matrixUserId = RequireLifecycleMatrixUser(user);
        var account = RequireInventoryAccount(inventory, matrixUserId);

        if (!account.IsDeactivated || !string.Equals(user.Status, "deactivated", StringComparison.Ordinal))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_user_not_deactivated",
                "The Matrix account is already active.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        var operationId = await _operationStore.StartAsync(
            manifest.StackId,
            "reactivate-matrix-user",
            null,
            "control-plane-ui",
            "matrix-admin-api",
            new
            {
                manifest.StackId,
                manifest.Slug,
                user.Id,
                MatrixUserId = matrixUserId,
                LogoutDevices = true
            },
            ct);

        try
        {
            var credential = await _managedRecoveryAuthority.GetOrProvisionAsync(
                manifest,
                forceManagedReplacement: false,
                ct);
            var authorityRecovered = false;

            try
            {
                await _adminUserClient.ReactivateUserAsync(
                    manifest.Matrix.PublicBaseUrl!,
                    matrixUserId,
                    request.NewPassword,
                    credential.AccessToken,
                    ct);
            }
            catch (MatrixAdminAuthorityException ex)
                when (ex.FailureKind == MatrixAdminAuthorityFailureKind.Rejected)
            {
                try
                {
                    await _adminAuthority.MarkInvalidAsync(manifest.StackId, ex.Code, ct);
                }
                catch
                {
                    // The rejected token is not reused.
                }

                credential = await _managedRecoveryAuthority.GetOrProvisionAsync(
                    manifest,
                    forceManagedReplacement: true,
                    ct);
                authorityRecovered = true;

                await _adminUserClient.ReactivateUserAsync(
                    manifest.Matrix.PublicBaseUrl!,
                    matrixUserId,
                    request.NewPassword,
                    credential.AccessToken,
                    ct);
            }

            var completedAt = DateTimeOffset.UtcNow.UtcDateTime;
            user.Status = "active";
            user.LastError = null;
            user.UpdatedAtUtc = completedAt;
            user.MatrixSyncedAtUtc = completedAt;
            await _db.SaveChangesAsync(ct);

            var refresh = await RefreshInventoryAfterLifecycleMutationAsync(manifest, ct);

            await _operationStore.CompleteAsync(
                operationId,
                "active",
                "completed",
                new
                {
                    user.Id,
                    MatrixUserId = matrixUserId,
                    Deactivated = false,
                    LogoutDevices = true,
                    AuthorityRecovered = authorityRecovered,
                    InventoryStatus = refresh.Status,
                    InventoryErrorCode = refresh.ErrorCode,
                    CompletedAtUtc = completedAt
                },
                new
                {
                    MatrixUserId = matrixUserId,
                    Deactivated = false,
                    LogoutDevices = true,
                    AuthorityRecovered = authorityRecovered,
                    InventoryStatus = refresh.Status,
                    InventoryErrorCode = refresh.ErrorCode,
                    CompletedAtUtc = completedAt
                },
                ct);

            return new RuntimeStackUserLifecycleResponse(
                Source: "control-plane",
                Status: "reactivated",
                RuntimeStackId: manifest.StackId,
                UserId: user.Id,
                MatrixUserId: matrixUserId,
                IsDeactivated: false,
                LogoutDevices: true,
                CompletedAtUtc: completedAt);
        }
        catch (MatrixAdminAuthorityException ex)
        {
            await _operationStore.FailAsync(
                operationId,
                "failed",
                ex.SafeDetail,
                new { MatrixUserId = matrixUserId, ErrorCode = ex.Code },
                ct);
            throw;
        }
    }

    private async Task<RuntimeStackUserEntity> RequireProjectedUserAsync(
        Guid runtimeStackId,
        Guid userId,
        CancellationToken ct)
    {
        return await _db.RuntimeStackUsers.FirstOrDefaultAsync(
                x => x.Id == userId && x.RuntimeStackId == runtimeStackId,
                ct)
            ?? throw new MatrixAdminAuthorityException(
                "matrix_user_not_found",
                "The Matrix user was not found in this runtime stack.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
    }

    private static string RequireLifecycleMatrixUser(RuntimeStackUserEntity user)
    {
        if (string.IsNullOrWhiteSpace(user.MatrixUserId))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_user_identity_unavailable",
                "The Matrix user identity is unavailable.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        if (MatrixManagedRecoveryAuthorityService.IsManagedRecoveryUser(user.MatrixUserId))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_managed_recovery_user_protected",
                "MEM's internal Matrix recovery administrator cannot be changed through the Users area.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
        }

        return user.MatrixUserId;
    }

    private static SynapseUserInventoryAccount RequireInventoryAccount(
        RuntimeStackUserInventorySynchronizationResult inventory,
        string matrixUserId)
    {
        return inventory.Accounts.FirstOrDefault(x => string.Equals(
                x.MatrixUserId,
                matrixUserId,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new MatrixAdminAuthorityException(
                "matrix_user_not_found_in_synapse",
                "Synapse did not report the requested Matrix account.",
                MatrixAdminAuthorityFailureKind.InvalidRequest);
    }

    private async Task<LifecycleInventoryRefresh> RefreshInventoryAfterLifecycleMutationAsync(
        RuntimeStackManifest manifest,
        CancellationToken ct)
    {
        try
        {
            var refreshed = await _inventoryReconciliation.SynchronizeAsync(manifest, ct);
            return new LifecycleInventoryRefresh(
                RuntimeStackUserInventoryStates.Synchronized,
                refreshed.UserCount,
                refreshed.ActiveAdminCount,
                null);
        }
        catch (RuntimeStackUserInventoryException ex)
        {
            return new LifecycleInventoryRefresh(
                RuntimeStackUserInventoryStates.Failed,
                null,
                null,
                ex.Code);
        }
    }

    private sealed record LifecycleInventoryRefresh(
        string Status,
        int? UserCount,
        int? ActiveAdminCount,
        string? ErrorCode);

    private async Task<RuntimeStackManifest> RequireManifestAsync(string slugOrId, CancellationToken ct)
    {
        var manifest = await _manifestStore.FindAsync(slugOrId, ct)
            ?? throw new InvalidOperationException($"Runtime stack '{slugOrId}' was not found.");
        if (string.IsNullOrWhiteSpace(manifest.Matrix.PublicBaseUrl) ||
            string.IsNullOrWhiteSpace(manifest.Matrix.ServerName))
        {
            throw new InvalidOperationException("Runtime stack Matrix routing identity is incomplete.");
        }
        return manifest;
    }

    private static void EnsureLocalMatrixUser(string matrixUserId, string serverName)
    {
        if (!matrixUserId.EndsWith($":{serverName}", StringComparison.OrdinalIgnoreCase))
        {
            throw new MatrixAdminAuthorityException(
                "matrix_admin_authority_wrong_homeserver",
                "The supplied administrator token does not belong to this Matrix homeserver.",
                MatrixAdminAuthorityFailureKind.Rejected);
        }
    }

    private async Task<AuthorityCaptureResult> CaptureAuthorityAfterCreationAsync(
        RuntimeStackManifest manifest,
        RuntimeStackUserResponse response,
        string? accessToken,
        CancellationToken ct)
    {
        if (!response.IsAdmin || string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(response.MatrixUserId))
        {
            return new AuthorityCaptureResult(
                response.IsAdmin ? MatrixAdminAuthorityStates.Required : "not_applicable", null);
        }

        try
        {
            var status = await _adminAuthority.StoreAsync(
                manifest.StackId, accessToken, response.MatrixUserId,
                "shared-secret-registration", DateTimeOffset.UtcNow, ct);
            return new AuthorityCaptureResult(status.Status, null);
        }
        catch
        {
            // The Matrix account already exists. Failure to persist reset authority is
            // recoverable through the explicit authority-import endpoint.
            return new AuthorityCaptureResult(MatrixAdminAuthorityStates.Required,
                "matrix_admin_authority_capture_failed");
        }
    }

    private sealed record AuthorityCaptureResult(string Status, string? ErrorCode);

    private async Task<PostCreationInventoryRefresh> RefreshInventoryAfterCreationAsync(
        RuntimeStackManifest manifest,
        CancellationToken ct)
    {
        try
        {
            var refreshed = await _inventoryReconciliation.SynchronizeAsync(manifest, ct);

            return new PostCreationInventoryRefresh(
                Status: RuntimeStackUserInventoryStates.Synchronized,
                UserCount: refreshed.UserCount,
                ActiveAdminCount: refreshed.ActiveAdminCount,
                ErrorCode: null);
        }
        catch (RuntimeStackUserInventoryException ex)
        {
            // The Matrix account was already created successfully. Inventory refresh is a
            // recoverable control-plane warning and must not rewrite that account as failed.
            return new PostCreationInventoryRefresh(
                Status: RuntimeStackUserInventoryStates.Failed,
                UserCount: null,
                ActiveAdminCount: null,
                ErrorCode: ex.Code);
        }
    }

    private sealed record PostCreationInventoryRefresh(
        string Status,
        int? UserCount,
        int? ActiveAdminCount,
        string? ErrorCode);

    private static string NormalizeUsername(string value)
    {
        var lower = value.Trim().ToLowerInvariant();

        var chars = lower
            .Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '=')
            .ToArray();

        return new string(chars);
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}

public sealed record CreateRuntimeStackUserRequest(
    string Username,
    string Password,
    bool IsAdmin,
    string? DisplayName,
    string? Email);

public sealed record DeactivateRuntimeStackUserRequest(
    bool Erase = false);

public sealed record ReactivateRuntimeStackUserRequest(
    string NewPassword);

public sealed record RuntimeStackUserLifecycleResponse(
    string Source,
    string Status,
    Guid RuntimeStackId,
    Guid UserId,
    string MatrixUserId,
    bool IsDeactivated,
    bool LogoutDevices,
    DateTime CompletedAtUtc);

public sealed record RuntimeStackUsersResponse(
    string Source,
    string Status,
    Guid StackId,
    string Slug,
    string? InventorySource,
    string InventoryStatus,
    DateTime? InventoryLastAttemptedAtUtc,
    DateTime? InventoryLastSynchronizedAtUtc,
    int? InventoryUserCount,
    int ActiveAdminCount,
    string? InventoryErrorCode,
    bool SynchronizationRequired,
    IReadOnlyList<RuntimeStackUserResponse> Users,
    bool RequiresFirstAdmin,
    bool CanCreateUsers,
    MatrixAdminAuthorityStatus AdminAuthority,
    string? Detail);

public sealed record RuntimeStackUserResponse(
    Guid Id,
    Guid RuntimeStackId,
    Guid MatrixInstanceId,
    string Username,
    string? MatrixUserId,
    bool IsAdmin,
    bool IsFirstAdmin,
    string Status,
    string Origin,
    string? DisplayName,
    string? Email,
    string? LastError,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? MatrixSyncedAtUtc)
{
    public static RuntimeStackUserResponse FromEntity(RuntimeStackUserEntity entity)
    {
        return new RuntimeStackUserResponse(
            Id: entity.Id,
            RuntimeStackId: entity.RuntimeStackId,
            MatrixInstanceId: entity.MatrixInstanceId,
            Username: entity.Username,
            MatrixUserId: entity.MatrixUserId,
            IsAdmin: entity.IsAdmin,
            IsFirstAdmin: entity.IsFirstAdmin,
            Status: entity.Status,
            Origin: RuntimeStackUserProjectionMetadata.ReadOrigin(entity),
            DisplayName: entity.DisplayName,
            Email: entity.Email,
            LastError: entity.LastError,
            CreatedAtUtc: entity.CreatedAtUtc,
            UpdatedAtUtc: entity.UpdatedAtUtc,
            MatrixSyncedAtUtc: entity.MatrixSyncedAtUtc);
    }
}
