using HostAgent.Runtime.Backups.AdvancedCutover.Execution.Catalog;

namespace HostAgent.Tests.Runtime.Backups.AdvancedCutover.Execution.Catalog;

public sealed class CatalogPublicCutoverExecutionRequestTests
{
    [Fact]
    public void Keeps_all_high_risk_cutover_inputs_explicit()
    {
        var request = new CatalogPublicCutoverExecutionRequest(
            ConfirmationId: "confirmation-catalog",
            CandidateId: "candidate-catalog",
            OldRuntimeStackSlug: "demo-stack",
            Operator: "Nigel",
            Note: "Controlled development cutover.",
            Execute: true,
            AcknowledgeFinalApproval: true,
            AcknowledgeFinalBackupWillBeCaptured: true,
            AcknowledgeOldRuntimeWillBeStopped: true,
            AcknowledgePublicRouteMutation: true,
            AcknowledgeManualRollback: true);

        Assert.True(request.Execute);
        Assert.Equal("confirmation-catalog", request.ConfirmationId);
        Assert.Equal("candidate-catalog", request.CandidateId);
        Assert.Equal("demo-stack", request.OldRuntimeStackSlug);
        Assert.True(request.AcknowledgeFinalApproval);
        Assert.True(request.AcknowledgeFinalBackupWillBeCaptured);
        Assert.True(request.AcknowledgeOldRuntimeWillBeStopped);
        Assert.True(request.AcknowledgePublicRouteMutation);
        Assert.True(request.AcknowledgeManualRollback);
    }
}
