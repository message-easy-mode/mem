using System.Reflection;
using Mem.Migrate.Infrastructure.Conversion;

namespace Mem.Migrate.UnitTests;

public sealed class SynapseSequenceConsistencyRepairTests
{
    [Fact]
    public void Required_value_covers_to_device_stream_position_when_port_db_sequence_lags()
    {
        var method = typeof(SynapseConversionService).GetMethod(
            "CalculateRequiredSequenceValue",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "CalculateRequiredSequenceValue was not found.");

        var required = method.Invoke(null, [2L, false, 0L, 6L]);

        Assert.Equal(6L, Assert.IsType<long>(required));
    }

    [Fact]
    public void Required_value_never_reduces_an_already_advanced_sequence()
    {
        var method = typeof(SynapseConversionService).GetMethod(
            "CalculateRequiredSequenceValue",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "CalculateRequiredSequenceValue was not found.");

        var required = method.Invoke(null, [8L, true, 4L, 6L]);

        Assert.Equal(8L, Assert.IsType<long>(required));
    }

    [Fact]
    public void Repair_sql_is_allowlisted_and_does_not_delete_stream_positions()
    {
        var inspectionMethod = typeof(SynapseConversionService).GetMethod(
            "BuildToDeviceSequenceInspectionSql",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "BuildToDeviceSequenceInspectionSql was not found.");
        var setValueMethod = typeof(SynapseConversionService).GetMethod(
            "BuildSequenceSetValueSql",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "BuildSequenceSetValueSql was not found.");

        var inspectionSql = Assert.IsType<string>(inspectionMethod.Invoke(null, null));
        var setValueSql = Assert.IsType<string>(setValueMethod.Invoke(null, [6L]));
        var combined = inspectionSql + "\n" + setValueSql;

        Assert.Contains("device_inbox_sequence", combined, StringComparison.Ordinal);
        Assert.Contains("device_inbox", inspectionSql, StringComparison.Ordinal);
        Assert.Contains("device_federation_outbox", inspectionSql, StringComparison.Ordinal);
        Assert.Contains("stream_name = 'to_device'", inspectionSql, StringComparison.Ordinal);
        Assert.Equal(
            "SELECT setval('device_inbox_sequence', 6, true);",
            setValueSql);
        Assert.DoesNotContain("DELETE", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE stream_positions", combined, StringComparison.OrdinalIgnoreCase);
    }
}
