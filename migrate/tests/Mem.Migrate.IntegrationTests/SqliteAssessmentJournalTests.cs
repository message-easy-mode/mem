using Mem.Migrate.Infrastructure.Persistence;

namespace Mem.Migrate.IntegrationTests;

public sealed class SqliteAssessmentJournalTests
{
    [Fact]
    public async Task Saves_and_loads_latest_assessment()
    {
        var directory = Directory.CreateTempSubdirectory(
            "mem-migrate-journal-");

        try
        {
            var journal = new SqliteAssessmentJournal(directory.FullName);
            await journal.InitializeAsync(CancellationToken.None);
            var result = IntegrationTestData.Result();

            await journal.SaveAsync(
                result,
                "{\"classification\":\"ConfirmedSupportedV010\"}",
                "# report",
                CancellationToken.None);

            var stored = await journal.GetLatestAsync(
                CancellationToken.None);

            Assert.NotNull(stored);
            Assert.Equal(result.AssessmentId, stored.AssessmentId);
            Assert.Equal(result.SourceFingerprint, stored.SourceFingerprint);
            Assert.Equal("# report", stored.PublicMarkdown);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
