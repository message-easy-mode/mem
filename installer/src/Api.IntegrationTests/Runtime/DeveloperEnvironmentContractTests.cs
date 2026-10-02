namespace Api.IntegrationTests.Runtime;

public sealed class DeveloperEnvironmentContractTests
{
    [Fact]
    public void Production_shaped_image_builds_web_and_api_without_shipping_build_toolchains()
    {
        var root = FindRepositoryRoot();
        var dockerfile = Read(root, "installer/Dockerfile");
        var dockerIgnore = Read(root, "installer/.dockerignore");

        Assert.Contains("AS web-build", dockerfile, StringComparison.Ordinal);
        Assert.Contains("npm ci", dockerfile, StringComparison.Ordinal);
        Assert.Contains("npm run build", dockerfile, StringComparison.Ordinal);
        Assert.Contains("AS api-build", dockerfile, StringComparison.Ordinal);
        Assert.Contains("dotnet restore Api/Api.csproj", dockerfile, StringComparison.Ordinal);
        Assert.Contains("dotnet publish Api/Api.csproj", dockerfile, StringComparison.Ordinal);
        Assert.Contains("AS runtime", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY --from=web-build", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY --from=api-build", dockerfile, StringComparison.Ordinal);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"Api.dll\"]", dockerfile, StringComparison.Ordinal);
        Assert.Contains("HEALTHCHECK", dockerfile, StringComparison.Ordinal);
        Assert.Contains("/health/ready", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("mem_test123", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("MEM_CONTROL_PLANE_SETUP_TOKEN=", dockerfile, StringComparison.Ordinal);

        Assert.Contains("**/node_modules", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("**/bin", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("**/obj", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("**/*.db", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("**/secrets", dockerIgnore, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_declares_one_scoped_container_runtime_and_shared_interactive_state_boundary()
    {
        var root = FindRepositoryRoot();
        var compose = Read(root, "dev/compose.control-plane.yml");

        Assert.Contains("name: ${MEM_CONTROL_PLANE_COMPOSE_PROJECT:-mem-control-plane-dev}", compose, StringComparison.Ordinal);
        Assert.Contains("container_name: ${MEM_CONTROL_PLANE_CONTAINER_NAME:-mem-control-plane-dev}", compose, StringComparison.Ordinal);
        Assert.Contains("image: ${MEM_CONTROL_PLANE_IMAGE:-mem-control-plane:local}", compose, StringComparison.Ordinal);
        Assert.Contains("MEM_RUNTIME_MODE: containerized-development", compose, StringComparison.Ordinal);
        Assert.Contains("MEM_STATE_ROOT: /data", compose, StringComparison.Ordinal);
        Assert.Contains("MemRuntime__ContainerName: ${MEM_CONTROL_PLANE_CONTAINER_NAME:-mem-control-plane-dev}", compose, StringComparison.Ordinal);
        Assert.Contains("MemRuntime__UiDeliveryMode: embedded-spa", compose, StringComparison.Ordinal);
        Assert.Contains("${MEM_CONTROL_PLANE_STATE_ROOT}:/data", compose, StringComparison.Ordinal);
        Assert.Contains("${MEM_CONTROL_PLANE_MEM_DATA_ROOT}:${MEM_CONTROL_PLANE_MEM_DATA_ROOT}", compose, StringComparison.Ordinal);
        Assert.Contains("${MEM_CONTROL_PLANE_LEGACY_MEM_DATA_ROOT}:${MEM_CONTROL_PLANE_LEGACY_MEM_DATA_ROOT}", compose, StringComparison.Ordinal);
        Assert.Contains("MEM_DATA_ROOT: ${MEM_CONTROL_PLANE_MEM_DATA_ROOT}", compose, StringComparison.Ordinal);
        Assert.Contains("${MEM_CONTROL_PLANE_HOST_DATA_ROOT}:${MEM_CONTROL_PLANE_HOST_DATA_ROOT}", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("MEM_CONTROL_PLANE_VOLUME_NAME", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("mem-control-plane-dev-data:/data", compose, StringComparison.Ordinal);
        Assert.Contains("Provisioning__InstanceDataRoot: ${MEM_CONTROL_PLANE_HOST_DATA_ROOT}/instances", compose, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:${MEM_CONTROL_PLANE_HTTPS_PORT:-8443}:8443", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("MEM_CONTROL_PLANE_SETUP_TOKEN:", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("MEM_DEV_ALLOW_SHARED_DOCKER_HOST: \"true\"", compose, StringComparison.Ordinal);
    }


    [Fact]
    public void Local_debug_profiles_use_the_same_interactive_authority_as_container_development()
    {
        var root = FindRepositoryRoot();
        var launchSettings = Read(root, "installer/src/Api/Properties/launchSettings.json");
        var harness = Read(root, "dev/mem-env");

        Assert.Contains("../../../dev/.state/interactive/data", launchSettings, StringComparison.Ordinal);
        Assert.Contains("../../../dev/.state/interactive/mem-data", launchSettings, StringComparison.Ordinal);
        Assert.Contains("../../../dev/.state/container/host-data/instances", launchSettings, StringComparison.Ordinal);
        Assert.Contains("../dev/.state/interactive/data/mem-control-plane.dev.db", launchSettings, StringComparison.Ordinal);
        Assert.Contains("../dev/.state/interactive/data/logs/control-plane/mem-control-plane-.clef", launchSettings, StringComparison.Ordinal);
        Assert.Contains("InstallerAuth__DevelopmentSetupToken", launchSettings, StringComparison.Ordinal);
        Assert.Contains("\"InstallerAuth__DevelopmentSetupToken\": \"\"", launchSettings, StringComparison.Ordinal);

        Assert.Contains("MEM_STATE_ROOT=\"${INTERACTIVE_DATA_ROOT}\"", harness, StringComparison.Ordinal);
        Assert.Contains("Aio__SqlitePath=\"${INTERACTIVE_SQLITE_PATH}\"", harness, StringComparison.Ordinal);
        Assert.Contains("Provisioning__InstanceDataRoot=\"${INTERACTIVE_HOST_DATA_ROOT}/instances\"", harness, StringComparison.Ordinal);
        Assert.Contains("Shared Control Plane identity preserved across switch", harness, StringComparison.Ordinal);
    }

    [Fact]
    public void Embedded_spa_shell_is_available_before_authentication_while_api_auth_remains_enabled()
    {
        var root = FindRepositoryRoot();
        var program = Read(root, "installer/src/Api/Program.cs");

        const string fallbackMarker = "app.MapFallbackToFile(\"index.html\")";
        var fallbackIndex = program.IndexOf(fallbackMarker, StringComparison.Ordinal);

        Assert.True(fallbackIndex >= 0, "Embedded SPA fallback mapping was not found.");

        var fallbackContract = program.Substring(
            fallbackIndex,
            Math.Min(240, program.Length - fallbackIndex));

        Assert.Contains(".AllowAnonymous();", fallbackContract, StringComparison.Ordinal);
        Assert.Contains("app.UseAuthentication();", program, StringComparison.Ordinal);
        Assert.Contains("app.UseAuthorization();", program, StringComparison.Ordinal);
        Assert.Contains("app.MapCarter();", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Harness_preserves_normal_state_and_requires_exact_destructive_acknowledgement()
    {
        var root = FindRepositoryRoot();
        var harness = Read(root, "dev/mem-env");
        var ignore = Read(root, ".gitignore");

        Assert.Contains("DELETE_MEM_DEV_STATE", harness, StringComparison.Ordinal);
        Assert.Contains("compose down --remove-orphans", harness, StringComparison.Ordinal);
        Assert.Contains("reset interactive", harness, StringComparison.Ordinal);
        Assert.Contains("authority adopt local|container", harness, StringComparison.Ordinal);
        Assert.Contains("INTERACTIVE_STATE=\"${STATE_ROOT}/interactive\"", harness, StringComparison.Ordinal);
        Assert.Contains("MEM_CONTROL_PLANE_STATE_ROOT=${INTERACTIVE_DATA_ROOT}", harness, StringComparison.Ordinal);
        Assert.Contains("MEM_CONTROL_PLANE_MEM_DATA_ROOT=${INTERACTIVE_MEM_DATA_ROOT}", harness, StringComparison.Ordinal);
        Assert.Contains("MEM_CONTROL_PLANE_LEGACY_MEM_DATA_ROOT=${LEGACY_LOCAL_MEM_DATA_ROOT}", harness, StringComparison.Ordinal);
        Assert.Contains("runtime-manifest reconstruct", harness, StringComparison.Ordinal);
        Assert.Contains("mem_data_manifest_set_matches_active_database", harness, StringComparison.Ordinal);
        Assert.Contains("Exact active manifests imported", harness, StringComparison.Ordinal);
        Assert.Contains("runtime_json", harness, StringComparison.Ordinal);
        Assert.Contains("/health/runtime", harness, StringComparison.Ordinal);
        Assert.Contains("require_exclusive_docker_host", harness, StringComparison.Ordinal);
        Assert.Contains("Refusing to stop PID", harness, StringComparison.Ordinal);
        Assert.Contains("The harness will not kill or remap an unrelated process", harness, StringComparison.Ordinal);
        Assert.Contains("/dev/.state/", ignore, StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_manifest_authority_uses_exact_stack_identity_and_never_slug_as_ownership()
    {
        var root = FindRepositoryRoot();
        var reconciliation = Read(root, "installer/src/HostAgent/Runtime/Maintenance/RuntimeReconciliationReportService.cs");
        var manifestStore = Read(root, "installer/src/HostAgent/Runtime/Manifests/RuntimeStackManifestStore.cs");
        var program = Read(root, "installer/src/Api/Program.cs");

        Assert.Contains("var hasManifest = manifestIds.Contains(stack.Id);", reconciliation, StringComparison.Ordinal);
        Assert.DoesNotContain("manifestSlugs.Contains", reconciliation, StringComparison.Ordinal);
        Assert.Contains("requestedStackId = activeCandidates.SingleOrDefault()?.Id;", manifestStore, StringComparison.Ordinal);
        Assert.Contains("manifest.StackId == requestedStackId.Value", manifestStore, StringComparison.Ordinal);
        Assert.Contains("MemRuntimeManifestHostRecoveryCommand.IsRequested(args)", program, StringComparison.Ordinal);
    }

    private static string Read(string root, string relativePath) =>
        File.ReadAllText(Path.Combine(root, relativePath));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "installer", "src", "MemInstaller.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate repository root containing installer/src/MemInstaller.sln.");
    }
}
