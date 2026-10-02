using Mem.Migrate.Core.Capture;

namespace Mem.Migrate.ArchiveTests;

public sealed class CaptureOptionsTests
{
    [Fact]
    public void Normalize_accepts_a_native_age_x25519_recipient_shape()
    {
        var recipient = "age1" + new string('q', 58);
        var result = new CaptureOptions
        {
            AgeRecipient = recipient
        }.Normalize();

        Assert.Equal(recipient, result.AgeRecipient);
    }

    [Theory]
    [InlineData("ssh-ed25519 AAAA...")]
    [InlineData("AGE1UPPERCASE")]
    [InlineData("age1too-short")]
    public void Normalize_rejects_non_native_or_malformed_age_recipients(
        string recipient)
    {
        Assert.Throws<ArgumentException>(() =>
            new CaptureOptions
            {
                AgeRecipient = recipient
            }.Normalize());
    }
    [Fact]
    public void Normalize_requires_an_explicit_capture_id_for_resume()
    {
        Assert.Throws<ArgumentException>(() =>
            new CaptureOptions
            {
                Resume = true
            }.Normalize());
    }

    [Fact]
    public void Normalize_requires_a_freeze_report_for_final_frozen_capture()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new CaptureOptions
            {
                FinalFrozenCapture = true
            }.Normalize());

        Assert.Contains("freeze-report", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_accepts_final_frozen_capture_with_absolute_freeze_report_path()
    {
        var result = new CaptureOptions
        {
            FinalFrozenCapture = true,
            FreezeReportPath = "freeze-report.json"
        }.Normalize();

        var freezeReportPath = Assert.IsType<string>(result.FreezeReportPath);
        Assert.True(Path.IsPathFullyQualified(freezeReportPath));
    }

    [Fact]
    public void Normalize_rejects_freeze_report_without_final_frozen_mode()
    {
        Assert.Throws<ArgumentException>(() =>
            new CaptureOptions
            {
                FreezeReportPath = "freeze-report.json"
            }.Normalize());
    }

    [Fact]
    public void Normalize_rejects_an_empty_source_stack_id()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new CaptureOptions
            {
                SourceStackId = Guid.Empty
            }.Normalize());

        Assert.Contains("Source stack ID", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_accepts_and_normalizes_a_stable_source_identity_hash()
    {
        var identity = new string('A', 64);

        var result = new CaptureOptions
        {
            ExpectedSourceIdentity = identity
        }.Normalize();

        Assert.Equal(identity.ToLowerInvariant(), result.ExpectedSourceIdentity);
    }

    [Fact]
    public void Normalize_rejects_a_malformed_stable_source_identity_hash()
    {
        Assert.Throws<ArgumentException>(() =>
            new CaptureOptions
            {
                ExpectedSourceIdentity = "not-a-sha256"
            }.Normalize());
    }

}
