using System.Security.Cryptography;
using HostAgent.Runtime.Diagnostics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Diagnostics;

namespace HostAgent.Tests.Runtime.Diagnostics;

internal sealed class MemDiagnosticTestFixture : IDisposable
{
    private readonly string _temporaryRoot;

    public MemDiagnosticTestFixture(
        MemDiagnosticsOptions? options = null,
        MutableTimeProvider? timeProvider = null,
        string? rootPath = null)
    {
        _temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"mem-diagnostics-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temporaryRoot);

        Options = options ?? new MemDiagnosticsOptions();
        Options.RootPath = rootPath ?? Path.Combine(_temporaryRoot, "diagnostics");
        TimeProvider = timeProvider ?? new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero));
        Health = new MemDiagnosticHealthState(Options);
        Redactor = new MemDiagnosticRedactor(Options);
        ExceptionProjector = new MemDiagnosticExceptionProjector(Options, Redactor);
        Factory = new MemDiagnosticEventFactory(
            Options,
            Redactor,
            ExceptionProjector,
            TimeProvider);
        CursorCodec = new MemDiagnosticCursorCodec(
            new TestDataProtectionProvider());
        Store = new MemDiagnosticEventFileStore(
            Options,
            new TestHostEnvironment(_temporaryRoot),
            Redactor,
            CursorCodec,
            Health,
            TimeProvider,
            NullLogger<MemDiagnosticEventFileStore>.Instance);
        Writer = new MemDiagnosticEventWriter(
            Options,
            Factory,
            Store,
            Health,
            NullLogger<MemDiagnosticEventWriter>.Instance);
        Reader = new MemDiagnosticEventReader(
            Options,
            Store,
            Health,
            NullLogger<MemDiagnosticEventReader>.Instance);
        Retention = new MemDiagnosticRetentionService(
            Options,
            Store,
            Health,
            TimeProvider,
            NullLogger<MemDiagnosticRetentionService>.Instance);
    }

    public MemDiagnosticsOptions Options { get; }

    public MutableTimeProvider TimeProvider { get; }

    public MemDiagnosticHealthState Health { get; }

    public MemDiagnosticRedactor Redactor { get; }

    public MemDiagnosticExceptionProjector ExceptionProjector { get; }

    public MemDiagnosticEventFactory Factory { get; }

    public MemDiagnosticCursorCodec CursorCodec { get; }

    public MemDiagnosticEventFileStore Store { get; }

    public MemDiagnosticEventWriter Writer { get; }

    public MemDiagnosticEventReader Reader { get; }

    public MemDiagnosticRetentionService Retention { get; }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
        {
            Directory.Delete(_temporaryRoot, recursive: true);
        }
    }

    internal sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "HostAgent.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestDataProtectionProvider : IDataProtectionProvider
    {
        public IDataProtector CreateProtector(string purpose) =>
            new TestDataProtector(purpose);
    }

    private sealed class TestDataProtector(string purpose) : IDataProtector
    {
        private readonly byte[] _key = SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"mem-tests:{purpose}"));

        public IDataProtector CreateProtector(string childPurpose) =>
            new TestDataProtector($"{purpose}:{childPurpose}");

        public byte[] Protect(byte[] plaintext)
        {
            using var hmac = new HMACSHA256(_key);
            var signature = hmac.ComputeHash(plaintext);
            return plaintext.Concat(signature).ToArray();
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            if (protectedData.Length < 32)
            {
                throw new CryptographicException("Protected payload is too short.");
            }

            var payload = protectedData[..^32];
            var suppliedSignature = protectedData[^32..];
            using var hmac = new HMACSHA256(_key);
            var expectedSignature = hmac.ComputeHash(payload);
            if (!CryptographicOperations.FixedTimeEquals(
                    suppliedSignature,
                    expectedSignature))
            {
                throw new CryptographicException("Protected payload was modified.");
            }

            return payload;
        }
    }
}
