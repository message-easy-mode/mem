using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Processes;
using Mem.Migrate.Infrastructure.Docker;

namespace Mem.Migrate.UnitTests;

public sealed class DockerCutoverSourceFreezerTests
{
    [Fact]
    public async Task Disables_restart_policies_stops_writers_and_verifies_state()
    {
        var runner = new RecordingProcessRunner();
        var freezer = new DockerCutoverSourceFreezer(runner);
        var plan = CreatePlan();

        var result = await freezer.FreezeAsync(
            plan,
            "docker",
            30,
            20,
            CancellationToken.None);

        Assert.Equal(4, result.Length);
        Assert.All(result, item => Assert.True(item.Stopped));
        Assert.All(result, item => Assert.True(item.RestartPolicyDisabled));
        Assert.Collection(
            runner.MutatingCommands,
            command => Assert.Equal("update --restart=no web-id", command),
            command => Assert.Equal("update --restart=no api-id", command),
            command => Assert.Equal("update --restart=no element-id", command),
            command => Assert.Equal("update --restart=no matrix-id", command),
            command => Assert.Equal("stop --time 20 web-id", command),
            command => Assert.Equal("stop --time 20 api-id", command),
            command => Assert.Equal("stop --time 20 element-id", command),
            command => Assert.Equal("stop --time 20 matrix-id", command));
    }

    private static CutoverPlanDocument CreatePlan()
    {
        CutoverSourceContainer Container(string role, string id) =>
            new(role, id, role, "image", "sha256:image", "running", null, "unless-stopped", true, null, null);
        return new CutoverPlanDocument(
            "mem-cutover-plan",
            1,
            "mm06a-proof",
            "test",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(30),
            "assessment",
            new string('a', 64),
            AssessmentClassification.ConfirmedSupportedV010,
            false,
            false,
            [
                Container("matrix", "matrix-id"),
                Container("element", "element-id"),
                Container("legacy-api", "api-id"),
                Container("legacy-web", "web-id")
            ],
            [], [], [],
            new CutoverTargetEvidence(
                "stage", "import", "profile", "http://target", "catalog", "restore", "staging",
                DateTimeOffset.UtcNow, true, true, true, true, true),
            [], [], [], [], []);
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<string> MutatingCommands { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            CancellationToken cancellationToken)
        {
            var command = string.Join(' ', request.Arguments);
            if (request.Arguments[0] is "update" or "stop" or "start")
            {
                MutatingCommands.Add(command);
            }
            var output = request.Arguments.Count >= 4 && request.Arguments[0] == "inspect"
                ? request.Arguments[2].Contains("RestartPolicy", StringComparison.Ordinal)
                    ? "no\n"
                    : "exited\n"
                : string.Empty;
            return Task.FromResult(new ProcessResult(
                true, 0, false, output, string.Empty, null, null));
        }
    }
}
