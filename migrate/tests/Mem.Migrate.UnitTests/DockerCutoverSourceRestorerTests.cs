using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Processes;
using Mem.Migrate.Infrastructure.Docker;

namespace Mem.Migrate.UnitTests;

public sealed class DockerCutoverSourceRestorerTests
{
    [Fact]
    public async Task Restores_restart_policies_starts_original_writers_in_order_and_verifies_services()
    {
        var checkpoint = CreateCheckpoint();
        var runner = new StatefulDockerRunner(checkpoint.SourceContainers);
        var restorer = new DockerCutoverSourceRestorer(runner);

        var results = await restorer.RestoreAsync(
            checkpoint,
            CreateFrozenResults(checkpoint),
            "docker",
            30,
            30,
            CancellationToken.None);

        Assert.Equal(4, results.Length);
        Assert.All(results, item => Assert.True(item.RestartPolicyRestored));
        Assert.All(results, item => Assert.True(item.RunningStateRestored));
        Assert.All(results, item => Assert.True(item.ServiceVerified));
        Assert.Equal(
        [
            "update --restart=unless-stopped matrix-id",
            "update --restart=unless-stopped element-id",
            "update --restart=unless-stopped api-id",
            "update --restart=unless-stopped web-id",
            "start matrix-id",
            "start element-id",
            "start api-id",
            "start web-id"
        ], runner.MutatingCommands);
    }

    [Fact]
    public async Task Refuses_container_image_identity_drift_before_mutation()
    {
        var checkpoint = CreateCheckpoint();
        var runner = new StatefulDockerRunner(checkpoint.SourceContainers);
        runner.SetImage("matrix-id", "sha256:drifted");
        var restorer = new DockerCutoverSourceRestorer(runner);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            restorer.RestoreAsync(
                checkpoint,
                CreateFrozenResults(checkpoint),
                "docker",
                30,
                30,
                CancellationToken.None));

        Assert.Empty(runner.MutatingCommands);
    }

    private static CutoverRollbackCheckpoint CreateCheckpoint()
    {
        CutoverSourceContainer Container(string role, string id) =>
            new(
                role,
                id,
                role,
                "image",
                $"sha256:{role}",
                "running",
                "healthy",
                "unless-stopped",
                true,
                null,
                null);

        return new CutoverRollbackCheckpoint(
            "mm06a-proof",
            new string('1', 64),
            new string('2', 64),
            DateTimeOffset.UtcNow,
        [
            Container("matrix", "matrix-id"),
            Container("element", "element-id"),
            Container("legacy-api", "api-id"),
            Container("legacy-web", "web-id")
        ],
            [],
            [],
            ["matrix", "element", "legacy-api", "legacy-web"],
            "restore exact source state");
    }

    private static CutoverFreezeContainerResult[] CreateFrozenResults(
        CutoverRollbackCheckpoint checkpoint) =>
        checkpoint.SourceContainers.Select(item =>
            new CutoverFreezeContainerResult(
                item.Role,
                item.ContainerId,
                item.ContainerName,
                item.RestartPolicy,
                item.WasRunning,
                "exited",
                "no",
                true,
                true)).ToArray();

    private sealed class StatefulDockerRunner : IProcessRunner
    {
        private readonly Dictionary<string, State> _states;

        public StatefulDockerRunner(IEnumerable<CutoverSourceContainer> containers)
        {
            _states = containers.ToDictionary(
                item => item.ContainerId,
                item => new State(
                    item.ContainerId,
                    item.ContainerName,
                    item.ImageId,
                    "exited",
                    "no",
                    "none"),
                StringComparer.Ordinal);
        }

        public List<string> MutatingCommands { get; } = [];

        public void SetImage(string id, string imageId) =>
            _states[id] = _states[id] with { ImageId = imageId };

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            CancellationToken cancellationToken)
        {
            var arguments = request.Arguments;
            var command = string.Join(' ', arguments);
            if (arguments[0] == "update")
            {
                MutatingCommands.Add(command);
                var id = arguments[2];
                var policy = arguments[1]["--restart=".Length..];
                _states[id] = _states[id] with { RestartPolicy = policy };
                return Success(string.Empty);
            }

            if (arguments[0] == "start")
            {
                MutatingCommands.Add(command);
                var id = arguments[1];
                _states[id] = _states[id] with
                {
                    StateValue = "running",
                    Health = "healthy"
                };
                return Success(string.Empty);
            }

            if (arguments[0] == "inspect")
            {
                var format = arguments[2];
                var id = arguments[3];
                var state = _states[id];
                var output = format switch
                {
                    "{{.Id}}" => state.Id,
                    "{{.Name}}" => "/" + state.Name,
                    "{{.Image}}" => state.ImageId,
                    "{{.State.Status}}" => state.StateValue,
                    "{{.HostConfig.RestartPolicy.Name}}" => state.RestartPolicy,
                    "{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}" => state.Health,
                    _ => throw new InvalidOperationException($"Unexpected inspect format '{format}'.")
                };
                return Success(output + "\n");
            }

            throw new InvalidOperationException($"Unexpected Docker command '{command}'.");
        }

        private static Task<ProcessResult> Success(string output) =>
            Task.FromResult(new ProcessResult(
                true,
                0,
                false,
                output,
                string.Empty,
                null,
                null));

        private sealed record State(
            string Id,
            string Name,
            string ImageId,
            string StateValue,
            string RestartPolicy,
            string Health);
    }
}
