namespace Api.IntegrationTests.Runtime;

public sealed class CreateChatStackTurnContractTests
{
    [Fact]
    public void Required_platform_turn_is_resolved_before_stack_specific_mutation()
    {
        var source = File.ReadAllText(HandlerSourcePath());

        var requireTurn = source.IndexOf(
            "currentStep = \"require-platform-turn\";",
            StringComparison.Ordinal);
        var resolveSecrets = source.IndexOf(
            "currentStep = \"resolve-secrets\";",
            StringComparison.Ordinal);
        var provisionDatabase = source.IndexOf(
            "currentStep = \"provision-database\";",
            StringComparison.Ordinal);
        var prepareFilesystem = source.IndexOf(
            "currentStep = \"prepare-filesystem\";",
            StringComparison.Ordinal);

        Assert.True(requireTurn >= 0);
        Assert.True(resolveSecrets > requireTurn);
        Assert.True(provisionDatabase > requireTurn);
        Assert.True(prepareFilesystem > requireTurn);
        Assert.DoesNotContain(
            "created without TURN relay configuration",
            source,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generated_turn_is_verified_before_Matrix_starts()
    {
        var source = File.ReadAllText(HandlerSourcePath());

        var generate = source.IndexOf(
            "currentStep = \"generate-synapse-config\";",
            StringComparison.Ordinal);
        var verifyTurn = source.IndexOf(
            "currentStep = \"verify-turn-config\";",
            StringComparison.Ordinal);
        var startMatrix = source.IndexOf(
            "currentStep = \"start-matrix\";",
            StringComparison.Ordinal);

        Assert.True(generate >= 0);
        Assert.True(verifyTurn > generate);
        Assert.True(startMatrix > verifyTurn);
    }

    [Fact]
    public void Created_Matrix_manifest_records_complete_MEM_managed_turn_association()
    {
        var source = File.ReadAllText(HandlerSourcePath());

        foreach (var required in new[]
                 {
                     "[\"turnConfigured\"] = \"true\"",
                     "[\"turnSharedSecretPresent\"]",
                     "[\"turnConfigurationSource\"] = \"platform-coturn\"",
                     "[\"turnManagement\"] = RuntimeStackTurnManagementKinds.MemManaged",
                     "[\"turnConfigurationSha256\"] = generatedTurnConfiguration.FileSha256",
                     "[\"turnLastOperationMode\"] = \"create\""
                 })
        {
            Assert.Contains(required, source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            "[\"turnSharedSecret\"]",
            source,
            StringComparison.Ordinal);
    }

    private static string HandlerSourcePath() =>
        Path.Combine(
            FindRepositoryRoot(),
            "installer",
            "src",
            "HostAgent",
            "Services",
            "CreateChatStackRuntimeHandlerParts",
            "CreateChatStackRuntimeHandler.Handle.cs");

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "installer",
                    "src",
                    "MemInstaller.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the MEM repository root from the test runtime.");
    }
}
