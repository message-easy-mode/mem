using Shared.ControlPlane;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Runtime;

public sealed class MemControlPlaneRuntimeContextFactoryTests
{
    [Fact]
    public void Local_runtime_identity_survives_restart_while_process_identity_rotates()
    {
        var root = TemporaryRoot();
        try
        {
            var options = Options(root, MemRuntimeModes.LocalDevelopment);
            var environment = EnvironmentSnapshot(
                root,
                environmentName: "Development",
                runningInContainer: false);

            var first = MemControlPlaneRuntimeContextFactory.Create(options, environment);
            var second = MemControlPlaneRuntimeContextFactory.Create(options, environment);

            Assert.Equal(MemRuntimeModes.LocalDevelopment, first.RuntimeMode);
            Assert.Equal(first.ControlPlaneInstanceId, second.ControlPlaneInstanceId);
            Assert.NotEqual(first.ApiProcessInstanceId, second.ApiProcessInstanceId);
            Assert.Equal("repository-local", first.StateRootKind);
            Assert.Equal(MemStateRootProfiles.Custom, first.StateRootProfile);
            Assert.Equal(MemUiDeliveryModes.Vite, first.UiDeliveryMode);
            Assert.True(first.ShowDevelopmentBanner);
            Assert.True(first.MutationsAllowed);
            Assert.True(File.Exists(Path.Combine(
                first.StateRootPath,
                "control-plane/runtime-instance.json")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Relative_local_state_root_is_resolved_from_the_api_content_root()
    {
        var root = TemporaryRoot();
        try
        {
            var contentRoot = Path.Combine(root, "installer", "src", "Api");
            Directory.CreateDirectory(contentRoot);
            var options = new MemRuntimeContextOptions
            {
                Mode = MemRuntimeModes.LocalDevelopment,
                StateRoot = "../../data",
                DockerEndpoint = "unix:///var/run/docker.sock",
                UiDeliveryMode = MemUiDeliveryModes.Vite,
                InstanceIdentityFileName = "control-plane/runtime-instance.json"
            };

            var context = MemControlPlaneRuntimeContextFactory.Create(
                options,
                EnvironmentSnapshot(
                    contentRoot,
                    environmentName: "Development",
                    runningInContainer: false));

            Assert.Equal(
                Path.Combine(root, "installer", "data"),
                context.StateRootPath);
            Assert.Equal(MemStateRootProfiles.Default, context.StateRootProfile);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Containerized_production_accepts_the_reviewed_legacy_container_during_transition()
    {
        var root = TemporaryRoot();
        try
        {
            var webRoot = Path.Combine(root, "wwwroot");
            Directory.CreateDirectory(webRoot);
            File.WriteAllText(Path.Combine(webRoot, "index.html"), "<html></html>");
            var options = Options(root, MemRuntimeModes.ContainerizedProduction);
            options.ContainerName = MemControlPlaneIdentity.Legacy.ContainerName;
            options.UiDeliveryMode = MemUiDeliveryModes.EmbeddedSpa;

            var context = MemControlPlaneRuntimeContextFactory.Create(
                options,
                EnvironmentSnapshot(
                    root,
                    environmentName: "Production",
                    runningInContainer: true,
                    webRootPath: webRoot));

            Assert.Equal(MemRuntimeModes.ContainerizedProduction, context.RuntimeMode);
            Assert.Equal(MemControlPlaneIdentity.Legacy.ContainerName, context.ConfiguredContainerName);
            Assert.Equal(MemUiDeliveryModes.EmbeddedSpa, context.UiDeliveryMode);
            Assert.Equal("persistent-volume", context.StateRootKind);
            Assert.Equal(MemStateRootProfiles.Custom, context.StateRootProfile);
            Assert.False(context.ShowDevelopmentBanner);
            Assert.Equal(MemRuntimeValidationStates.Valid, context.ValidationState);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Missing_embedded_spa_is_projected_as_api_only_without_inventing_browser_delivery()
    {
        var root = TemporaryRoot();
        try
        {
            var options = Options(root, MemRuntimeModes.ContainerizedDevelopment);
            options.ContainerName = MemControlPlaneIdentity.CanonicalContainerName;
            options.UiDeliveryMode = MemUiDeliveryModes.EmbeddedSpa;

            var context = MemControlPlaneRuntimeContextFactory.Create(
                options,
                EnvironmentSnapshot(
                    root,
                    environmentName: "Development",
                    runningInContainer: true,
                    webRootPath: Path.Combine(root, "missing-wwwroot")));

            Assert.Equal(MemUiDeliveryModes.ApiOnly, context.UiDeliveryMode);
            Assert.Equal(MemRuntimeValidationStates.Warning, context.ValidationState);
            Assert.Contains("runtime_embedded_spa_not_observed", context.Warnings);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData(MemRuntimeModes.LocalDevelopment, true, "runtime_mode_container_contradiction")]
    [InlineData(MemRuntimeModes.ContainerizedDevelopment, false, "runtime_mode_host_contradiction")]
    [InlineData(MemRuntimeModes.ContainerizedProduction, false, "runtime_mode_host_contradiction")]
    public void Declared_runtime_mode_must_agree_with_observed_container_state(
        string mode,
        bool runningInContainer,
        string expectedCode)
    {
        var root = TemporaryRoot();
        try
        {
            var options = Options(root, mode);
            if (MemRuntimeModes.IsContainerized(mode))
            {
                options.ContainerName = MemControlPlaneIdentity.CanonicalContainerName;
            }

            var exception = Assert.Throws<MemRuntimeContextValidationException>(() =>
                MemControlPlaneRuntimeContextFactory.Create(
                    options,
                    EnvironmentSnapshot(
                        root,
                        environmentName: "Production",
                        runningInContainer: runningInContainer)));

            Assert.Equal(expectedCode, exception.Code);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Containerized_development_accepts_the_scoped_developer_container_identity()
    {
        var root = TemporaryRoot();
        try
        {
            var webRoot = Path.Combine(root, "wwwroot");
            Directory.CreateDirectory(webRoot);
            File.WriteAllText(Path.Combine(webRoot, "index.html"), "<html></html>");
            var options = Options(root, MemRuntimeModes.ContainerizedDevelopment);
            options.ContainerName = MemControlPlaneIdentity.DevelopmentContainerName;
            options.UiDeliveryMode = MemUiDeliveryModes.EmbeddedSpa;

            var context = MemControlPlaneRuntimeContextFactory.Create(
                options,
                EnvironmentSnapshot(
                    root,
                    environmentName: "Production",
                    runningInContainer: true,
                    webRootPath: webRoot));

            Assert.Equal(MemRuntimeModes.ContainerizedDevelopment, context.RuntimeMode);
            Assert.Equal(
                MemControlPlaneIdentity.DevelopmentContainerName,
                context.ConfiguredContainerName);
            Assert.Equal(MemUiDeliveryModes.EmbeddedSpa, context.UiDeliveryMode);
            Assert.True(context.ShowDevelopmentBanner);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Containerized_production_rejects_the_developer_container_identity()
    {
        var root = TemporaryRoot();
        try
        {
            var options = Options(root, MemRuntimeModes.ContainerizedProduction);
            options.ContainerName = MemControlPlaneIdentity.DevelopmentContainerName;

            var exception = Assert.Throws<MemRuntimeContextValidationException>(() =>
                MemControlPlaneRuntimeContextFactory.Create(
                    options,
                    EnvironmentSnapshot(
                        root,
                        environmentName: "Production",
                        runningInContainer: true)));

            Assert.Equal("runtime_container_identity_unrecognized", exception.Code);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Production_rejects_a_deterministic_development_setup_token()
    {
        var root = TemporaryRoot();
        try
        {
            var options = Options(root, MemRuntimeModes.ContainerizedProduction);
            options.ContainerName = MemControlPlaneIdentity.CanonicalContainerName;

            var exception = Assert.Throws<MemRuntimeContextValidationException>(() =>
                MemControlPlaneRuntimeContextFactory.Create(
                    options,
                    EnvironmentSnapshot(
                        root,
                        environmentName: "Production",
                        runningInContainer: true,
                        developmentSetupTokenConfigured: true)));

            Assert.Equal(
                "runtime_production_development_token_forbidden",
                exception.Code);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Configured_instance_identity_must_match_the_persisted_state_root_identity()
    {
        var root = TemporaryRoot();
        try
        {
            var firstOptions = Options(root, MemRuntimeModes.AutomatedTest);
            firstOptions.ExpectedInstanceId = Guid.NewGuid().ToString("D");
            var environment = EnvironmentSnapshot(
                root,
                environmentName: "Test",
                runningInContainer: false);
            var first = MemControlPlaneRuntimeContextFactory.Create(
                firstOptions,
                environment);

            var secondOptions = Options(root, MemRuntimeModes.AutomatedTest);
            secondOptions.ExpectedInstanceId = Guid.NewGuid().ToString("D");
            var exception = Assert.Throws<MemRuntimeContextValidationException>(() =>
                MemControlPlaneRuntimeContextFactory.Create(
                    secondOptions,
                    environment));

            Assert.NotEqual(
                Guid.Parse(secondOptions.ExpectedInstanceId),
                first.ControlPlaneInstanceId);
            Assert.Equal("runtime_instance_identity_mismatch", exception.Code);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Host_access_IPv4_is_normalized_server_side_and_omitted_from_the_safe_projection()
    {
        var root = TemporaryRoot();
        try
        {
            var options = Options(root, MemRuntimeModes.ContainerizedProduction);
            options.ContainerName = "mem-control-plane";
            options.HostAccessIpv4 = " 192.168.40.12 ";
            var webRoot = Path.Combine(root, "wwwroot");
            Directory.CreateDirectory(webRoot);
            File.WriteAllText(Path.Combine(webRoot, "index.html"), "<html></html>");

            var context = MemControlPlaneRuntimeContextFactory.Create(
                options,
                EnvironmentSnapshot(
                    root,
                    environmentName: "Production",
                    runningInContainer: true,
                    webRootPath: webRoot));
            var serialized = System.Text.Json.JsonSerializer.Serialize(
                context.ToSafeProjection());

            Assert.Equal("192.168.40.12", context.HostAccessIpv4);
            Assert.DoesNotContain("192.168.40.12", serialized, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Invalid_host_access_IPv4_fails_runtime_context_validation()
    {
        var root = TemporaryRoot();
        try
        {
            var options = Options(root, MemRuntimeModes.LocalDevelopment);
            options.HostAccessIpv4 = "not-an-ip";

            var exception = Assert.Throws<MemRuntimeContextValidationException>(() =>
                MemControlPlaneRuntimeContextFactory.Create(
                    options,
                    EnvironmentSnapshot(
                        root,
                        environmentName: "Development",
                        runningInContainer: false)));

            Assert.Equal("runtime_host_ipv4_invalid", exception.Code);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public void Safe_projection_omits_exact_filesystem_and_docker_authority_values()
    {
        var root = TemporaryRoot();
        try
        {
            var context = TestRuntimeContext.Create(root);
            var projection = context.ToSafeProjection();
            var serialized = System.Text.Json.JsonSerializer.Serialize(projection);

            Assert.Equal(context.ControlPlaneInstanceId, projection.ControlPlaneInstanceId);
            Assert.Equal(context.ApiProcessInstanceId, projection.ApiProcessInstanceId);
            Assert.DoesNotContain(context.ContentRootPath, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(context.StateRootPath, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(context.DockerEndpoint.ToString(), serialized, StringComparison.Ordinal);
            Assert.Equal("disposable-test", projection.StateRootKind);
            Assert.Equal("local-unix-socket", projection.DockerEndpointKind);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static MemRuntimeContextOptions Options(string root, string mode) =>
        new()
        {
            Mode = mode,
            StateRoot = Path.Combine(root, "state"),
            DockerEndpoint = "unix:///var/run/docker.sock",
            UiDeliveryMode = mode switch
            {
                MemRuntimeModes.LocalDevelopment => MemUiDeliveryModes.Vite,
                MemRuntimeModes.AutomatedTest => MemUiDeliveryModes.TestHost,
                _ => MemUiDeliveryModes.EmbeddedSpa
            },
            InstanceIdentityFileName = "control-plane/runtime-instance.json"
        };

    private static MemRuntimeEnvironmentSnapshot EnvironmentSnapshot(
        string root,
        string environmentName,
        bool runningInContainer,
        string? webRootPath = null,
        bool developmentSetupTokenConfigured = false) =>
        new(
            EnvironmentName: environmentName,
            ApplicationName: "mem-control-plane",
            ContentRootPath: root,
            WebRootPath: webRootPath,
            RunningInContainer: runningInContainer,
            DevelopmentSetupTokenConfigured: developmentSetupTokenConfigured,
            Version: "0.2.0-test",
            Commit: "abc123");

    private static string TemporaryRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-runtime-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
