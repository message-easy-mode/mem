namespace Api.IntegrationTests.Domains;

public sealed class DomainCertificateRenewalActivationSourceContractTests
{
    [Fact]
    public void DOMAINS_CERTIFICATE_RENEWAL_01C_NPM_reuses_expected_certificate_and_verifies_consumers_before_MEM_pointer_switch()
    {
        var root = FindRepositoryRoot();
        var importProbe = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Shared", "Domains", "Certificates", "Npm", "NpmCertificateImportProbe.cs"));
        var proxyService = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Integrations", "Npm", "Services", "NpmProxyHostService.cs"));
        var activator = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Shared", "Domains", "Renewal", "DomainCertificateRenewalCandidateActivator.cs"));

        var replacementStart = importProbe.IndexOf(
            "public async Task<NpmCertificateProbeResult> ImportReplacementAsync(",
            StringComparison.Ordinal);
        var replacementEnd = importProbe.IndexOf(
            "private async Task WaitForCertificateApiReadyAsync",
            replacementStart,
            StringComparison.Ordinal);
        Assert.True(replacementStart >= 0 && replacementEnd > replacementStart);

        var replacement = importProbe[replacementStart..replacementEnd];
        var exactRecordIndex = replacement.IndexOf(
            "item.id == expectedNpmCertificateId",
            StringComparison.Ordinal);
        var uploadIndex = replacement.IndexOf(
            "UploadCustomCertificateFilesAsync",
            StringComparison.Ordinal);
        var reapplyIndex = replacement.IndexOf(
            "ReapplyCertificateConsumersAsync",
            StringComparison.Ordinal);
        Assert.True(exactRecordIndex >= 0);
        Assert.True(uploadIndex > exactRecordIndex);
        Assert.True(reapplyIndex > uploadIndex);
        Assert.DoesNotContain("CreateCustomCertificateRecordAsync", replacement, StringComparison.Ordinal);
        Assert.DoesNotContain("PersistNpmLinkAsync(", replacement, StringComparison.Ordinal);

        Assert.Contains("SnapshotMatches(item.Snapshot, updated, requireSameId: true)", proxyService, StringComparison.Ordinal);
        Assert.Contains("updated.meta?.nginx_online == false", proxyService, StringComparison.Ordinal);

        var ingressIndex = activator.IndexOf("ingressActivator.ActivateAsync", StringComparison.Ordinal);
        var pointerIndex = activator.IndexOf("currentDomain.ActiveCertificateId = currentCandidate.Id", StringComparison.Ordinal);
        var mainRoleIndex = activator.IndexOf("currentCandidate.IsMainPlatformCertificate = true", StringComparison.Ordinal);
        Assert.True(ingressIndex >= 0 && pointerIndex > ingressIndex);
        Assert.True(mainRoleIndex > pointerIndex);
        Assert.Contains("currentCandidate.NpmCertificateId = npmCertificateId", activator, StringComparison.Ordinal);
        Assert.Contains("NpmActivationStateChangedAfterIngress", activator, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteAsync(", activator, StringComparison.Ordinal);
    }

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
