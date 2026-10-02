using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Modules.Operator.Diagnostics.Services;
using Shared.Exceptions;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsQueryAndProjectionTests
{
    [Fact]
    public void Query_parser_rejects_oversized_windows()
    {
        var options = new DiagnosticsApiOptions
        {
            MaximumQueryWindowHours = 24,
            MaximumPageSize = 100
        };
        var parser = new DiagnosticsQueryParser(
            options,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero)));
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["sinceUtc"] = "2026-08-01T00:00:00Z",
            ["untilUtc"] = "2026-08-03T00:00:00Z",
            ["pageSize"] = "100"
        });

        var exception = Assert.Throws<MemProblemException>(() => parser.Parse(query));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal("diagnostics_query_range_too_large", exception.Code);
    }

    [Fact]
    public void Query_parser_rejects_oversized_page_sizes()
    {
        var options = new DiagnosticsApiOptions
        {
            MaximumQueryWindowHours = 24,
            MaximumPageSize = 100
        };
        var parser = new DiagnosticsQueryParser(
            options,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero)));
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["pageSize"] = "101"
        });

        var exception = Assert.Throws<MemProblemException>(() => parser.Parse(query));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal("diagnostics_page_size_invalid", exception.Code);
    }

    [Fact]
    public void Query_parser_rejects_unknown_severity_values()
    {
        var parser = new DiagnosticsQueryParser(
            new DiagnosticsApiOptions(),
            TimeProvider.System);
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["severity"] = "catastrophic"
        });

        var exception = Assert.Throws<MemProblemException>(() => parser.Parse(query));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Equal("diagnostics_severity_invalid", exception.Code);
    }

    [Theory]
    [InlineData("restore", "restore-session", "/restores/restore-session")]
    [InlineData("migration", "migration-session", "/migrations/migration-session")]
    [InlineData("stack", "stack-id", "/stacks/stack-id")]
    [InlineData("coturn", "platform", "/services/coturn")]
    [InlineData("installation", "install-1", "/setup")]
    public void Workspace_links_are_built_only_from_known_resource_kinds(
        string kind,
        string id,
        string expected)
    {
        var builder = new DiagnosticsWorkspaceLinkBuilder();

        var result = builder.Build(new Shared.Diagnostics.MemDiagnosticResource(kind, id));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Workspace_link_builder_does_not_echo_unknown_workspace_paths()
    {
        var builder = new DiagnosticsWorkspaceLinkBuilder();

        var result = builder.Build(new Shared.Diagnostics.MemDiagnosticResource(
            "unknown",
            "unknown-1",
            StackId: "stack-should-not-be-used",
            WorkspacePath: "/srv/private/secret"));

        Assert.Null(result);
    }
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

}
