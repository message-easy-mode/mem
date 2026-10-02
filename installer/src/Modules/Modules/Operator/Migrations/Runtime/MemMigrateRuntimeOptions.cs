namespace Modules.Operator.Migrations.Runtime;

public sealed class MemMigrateRuntimeOptions
{
    public const string SectionName = "Migration";

    public string MemMigrateCommand { get; set; } = string.Empty;
}
