using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Diagnostics;

public sealed class MemDiagnosticCursorCodec
{
    private const int MaximumCursorLength = 4096;
    private readonly IDataProtector _protector;

    public MemDiagnosticCursorCodec(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector("MEM.Diagnostics.Cursor.v1");
    }

    internal string Encode(MemDiagnosticCursorPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        ValidatePosition(position);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(
            Version: 1,
            FileKey: position.FileKey,
            LineIndex: position.LineIndex,
            EventId: position.EventId));
        return ToBase64Url(_protector.Protect(payload));
    }

    internal MemDiagnosticCursorPosition Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > MaximumCursorLength)
        {
            throw new MemDiagnosticCursorException();
        }

        try
        {
            var protectedPayload = FromBase64Url(cursor);
            var payload = _protector.Unprotect(protectedPayload);
            var parsed = JsonSerializer.Deserialize<CursorPayload>(payload);
            if (parsed is null || parsed.Version != 1)
            {
                throw new MemDiagnosticCursorException();
            }

            var position = new MemDiagnosticCursorPosition(
                parsed.FileKey,
                parsed.LineIndex,
                parsed.EventId);
            ValidatePosition(position);
            return position;
        }
        catch (MemDiagnosticCursorException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MemDiagnosticCursorException(ex);
        }
    }

    private static void ValidatePosition(MemDiagnosticCursorPosition position)
    {
        if (string.IsNullOrWhiteSpace(position.FileKey) ||
            position.FileKey.Length > 160 ||
            Path.IsPathRooted(position.FileKey) ||
            position.FileKey.Contains('\\') ||
            position.FileKey.Split('/').Any(segment =>
                string.IsNullOrWhiteSpace(segment) ||
                segment is "." or "..") ||
            position.LineIndex < 0 ||
            string.IsNullOrWhiteSpace(position.EventId) ||
            !position.EventId.StartsWith("evt_", StringComparison.Ordinal) ||
            position.EventId.Length > 96)
        {
            throw new MemDiagnosticCursorException();
        }
    }

    private static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            0 => base64,
            2 => base64 + "==",
            3 => base64 + "=",
            _ => throw new FormatException("Invalid base64url length.")
        };
        return Convert.FromBase64String(base64);
    }

    private sealed record CursorPayload(
        int Version,
        string FileKey,
        int LineIndex,
        string EventId);
}

internal sealed record MemDiagnosticCursorPosition(
    string FileKey,
    int LineIndex,
    string EventId);
