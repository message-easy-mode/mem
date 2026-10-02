using HostAgent.Runtime.Backups.AdvancedCutover.Execution.Catalog;

namespace HostAgent.Tests.Runtime.Backups.AdvancedCutover.Execution.Catalog;

public sealed class CatalogPublicCutoverExecutionGateEvaluationRequestTests
{
    [Fact]
    public void Gate_evaluation_only_is_explicit_and_defaults_to_false()
    {
        var defaultRequest = CreateRequest();
        var gateOnlyRequest = CreateRequest(GateEvaluationOnly: true);

        Assert.False(defaultRequest.GateEvaluationOnly);
        Assert.True(gateOnlyRequest.GateEvaluationOnly);
        Assert.True(gateOnlyRequest.Execute);
    }

    private static CatalogPublicCutoverExecutionRequest CreateRequest(
        bool GateEvaluationOnly = false)
        => new(
            ConfirmationId: "confirmation-catalog",
            CandidateId: "candidate-catalog",
            OldRuntimeStackSlug: "demo-stack",
            Operator: "Nigel",
            Note: "Gate evaluation.",
            Execute: true,
            AcknowledgeFinalApproval: false,
            AcknowledgeFinalBackupWillBeCaptured: true,
            AcknowledgeOldRuntimeWillBeStopped: true,
            AcknowledgePublicRouteMutation: true,
            AcknowledgeManualRollback: true,
            GateEvaluationOnly: GateEvaluationOnly);
}
