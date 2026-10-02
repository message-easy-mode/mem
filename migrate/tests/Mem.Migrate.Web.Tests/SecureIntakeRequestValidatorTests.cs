using Mem.Migrate.Application.Workflow;
using Mem.Migrate.Core.Capture;

namespace Mem.Migrate.Web.Tests;

public sealed class SecureIntakeRequestValidatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Accepts_a_current_preview_request_with_matching_recipient_fingerprint()
    {
        var recipient = "age1r5cmtjs7qft4w44jqh0y4w5w23jlcccztfrlqq8x3r2g8r60vqkqnkvu6f";
        var validator = new SecureIntakeRequestValidator(
            new ManualTimeProvider(Now));

        var result = validator.Validate(new SecureIntakeRequestInput(
            Schema: SecureIntakeRequestValidator.Schema,
            SchemaVersion: 1,
            IntakeId: "mig_20260728_preview",
            PackageRevisionId: null,
            RequestKind: "preview",
            AgeRecipient: recipient,
            RecipientFingerprint: PackageForIntakeOptions.CalculateRecipientFingerprint(recipient),
            ExpiresAtUtc: Now.AddHours(2),
            TargetControlPlaneVersion: "0.2.0",
            SourceStackId: Guid.NewGuid().ToString("D")));

        Assert.Equal("preview", result.RequestKind);
        Assert.Equal("mig_20260728_preview", result.IntakeId);
    }

    [Fact]
    public void Rejects_an_expired_request()
    {
        var recipient = "age1r5cmtjs7qft4w44jqh0y4w5w23jlcccztfrlqq8x3r2g8r60vqkqnkvu6f";
        var validator = new SecureIntakeRequestValidator(
            new ManualTimeProvider(Now));

        var exception = Assert.Throws<ArgumentException>(() =>
            validator.Validate(new SecureIntakeRequestInput(
                SecureIntakeRequestValidator.Schema,
                1,
                "mig_20260728_expired",
                null,
                "preview",
                recipient,
                PackageForIntakeOptions.CalculateRecipientFingerprint(recipient),
                Now.AddSeconds(-1),
                "0.2.0",
                null)));

        Assert.Contains("expired", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_a_recipient_fingerprint_mismatch()
    {
        var recipient = "age1r5cmtjs7qft4w44jqh0y4w5w23jlcccztfrlqq8x3r2g8r60vqkqnkvu6f";
        var validator = new SecureIntakeRequestValidator(
            new ManualTimeProvider(Now));

        var exception = Assert.Throws<ArgumentException>(() =>
            validator.Validate(new SecureIntakeRequestInput(
                SecureIntakeRequestValidator.Schema,
                1,
                "mig_20260728_mismatch",
                null,
                "preview",
                recipient,
                "0000-0000-0000-0000",
                Now.AddHours(1),
                "0.2.0",
                null)));

        Assert.Contains("fingerprint mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
