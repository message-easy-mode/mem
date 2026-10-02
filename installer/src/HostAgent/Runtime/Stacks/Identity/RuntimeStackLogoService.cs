using System.Buffers.Binary;
using System.Security.Cryptography;
using HostAgent.Runtime.Manifests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Stacks.Identity;

public sealed class RuntimeStackLogoService
{
    public const long MaximumBytes = 2 * 1024 * 1024;
    public const int MinimumDimension = 32;
    public const int MaximumDimension = 1024;
    public const string ContentType = "image/png";

    private static readonly byte[] PngSignature = [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
    ];
    private static readonly SemaphoreSlim MutationGate = new(1, 1);

    private readonly IConfiguration _configuration;
    private readonly RuntimeStackManifestStore _manifestStore;

    public RuntimeStackLogoService(
        IConfiguration configuration,
        RuntimeStackManifestStore manifestStore)
    {
        _configuration = configuration;
        _manifestStore = manifestStore;
    }

    public async Task<RuntimeStackManifest> UploadAsync(
        RuntimeStackManifest manifest,
        IFormFile file,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(file);

        await MutationGate.WaitAsync(ct);
        try
        {
            return await UploadCoreAsync(manifest, file, ct);
        }
        finally
        {
            MutationGate.Release();
        }
    }

    public async Task<RuntimeStackManifest> RemoveAsync(
        RuntimeStackManifest manifest,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        await MutationGate.WaitAsync(ct);
        try
        {
            var updated = await _manifestStore.UpdateLogoMetadataAsync(
                manifest.StackId,
                logo: null,
                ct);

            if (updated is null)
            {
                throw new InvalidOperationException(
                    $"Runtime stack '{manifest.Slug}' disappeared while its logo was being removed.");
            }

            DeleteStackAssetsBestEffort(manifest.StackId);
            return updated;
        }
        finally
        {
            MutationGate.Release();
        }
    }

    /// <summary>
    /// Restores a stack logo from managed backup material. The source bytes must
    /// validate as a bounded PNG and must exactly match the metadata captured in
    /// the backup. This never trusts a browser-supplied path or filename.
    /// </summary>
    public async Task<RuntimeStackManifest> RestoreFromBackupAsync(
        RuntimeStackManifest manifest,
        string sourcePath,
        RuntimeStackLogoMetadata expected,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(expected);

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new InvalidDataException("Stack logo backup path is required.");
        }

        await MutationGate.WaitAsync(ct);
        try
        {
            var bytes = await ReadBoundedFileAsync(sourcePath, ct);
            RuntimeStackLogoMetadata actual;
            try
            {
                actual = ValidatePng(bytes);
            }
            catch (RuntimeStackLogoValidationException ex)
            {
                throw new InvalidDataException(
                    $"Stack logo backup material is invalid: {ex.Message}",
                    ex);
            }

            if (!string.Equals(actual.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase) ||
                actual.Bytes != expected.Bytes ||
                actual.Width != expected.Width ||
                actual.Height != expected.Height)
            {
                throw new InvalidDataException(
                    "Stack logo backup material does not match its captured metadata.");
            }

            return await StoreValidatedAsync(manifest, bytes, actual, ct);
        }
        finally
        {
            MutationGate.Release();
        }
    }

    private async Task<RuntimeStackManifest> UploadCoreAsync(
        RuntimeStackManifest manifest,
        IFormFile file,
        CancellationToken ct)
    {
        var bytes = await ReadBoundedAsync(file, ct);
        var metadata = ValidatePng(bytes);
        return await StoreValidatedAsync(manifest, bytes, metadata, ct);
    }

    private async Task<RuntimeStackManifest> StoreValidatedAsync(
        RuntimeStackManifest manifest,
        byte[] bytes,
        RuntimeStackLogoMetadata metadata,
        CancellationToken ct)
    {
        var directory = GetStackAssetDirectory(manifest.StackId);
        Directory.CreateDirectory(directory);

        var finalPath = GetLogoPath(manifest.StackId, metadata.Sha256);
        var createdNewFile = false;

        if (!File.Exists(finalPath))
        {
            var temporary = Path.Combine(
                directory,
                $".logo-{Guid.NewGuid():N}.tmp");

            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, ct);
                File.Move(temporary, finalPath);
                createdNewFile = true;
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        RuntimeStackManifest? updated;
        try
        {
            updated = await _manifestStore.UpdateLogoMetadataAsync(
                manifest.StackId,
                metadata,
                ct);
        }
        catch
        {
            if (createdNewFile)
            {
                TryDeleteFile(finalPath);
            }

            throw;
        }

        if (updated is null)
        {
            if (createdNewFile)
            {
                TryDeleteFile(finalPath);
            }

            throw new InvalidOperationException(
                $"Runtime stack '{manifest.Slug}' disappeared while its logo was being updated.");
        }

        DeleteSupersededLogos(directory, finalPath);
        return updated;
    }

    public RuntimeStackLogoReadResult? Resolve(RuntimeStackManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var metadata = RuntimeStackIdentity.ResolveLogoMetadata(manifest);
        if (metadata is null)
        {
            return null;
        }

        var path = GetLogoPath(manifest.StackId, metadata.Sha256);
        if (!File.Exists(path))
        {
            return null;
        }

        return new RuntimeStackLogoReadResult(
            Path: path,
            ContentType: ContentType,
            Sha256: metadata.Sha256,
            Bytes: metadata.Bytes,
            Width: metadata.Width,
            Height: metadata.Height);
    }

    private async Task<byte[]> ReadBoundedAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length <= 0)
        {
            throw new RuntimeStackLogoValidationException(
                "stack_logo_empty",
                "Choose a non-empty PNG image.");
        }

        if (file.Length > MaximumBytes)
        {
            throw new RuntimeStackLogoValidationException(
                "stack_logo_too_large",
                "The stack logo exceeds the 2 MiB upload limit.");
        }

        await using var input = file.OpenReadStream();
        using var output = new MemoryStream((int)Math.Min(file.Length, MaximumBytes));
        var buffer = new byte[64 * 1024];
        long total = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaximumBytes)
            {
                throw new RuntimeStackLogoValidationException(
                    "stack_logo_too_large",
                    "The stack logo exceeds the 2 MiB upload limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        if (total == 0)
        {
            throw new RuntimeStackLogoValidationException(
                "stack_logo_empty",
                "Choose a non-empty PNG image.");
        }

        return output.ToArray();
    }

    private static async Task<byte[]> ReadBoundedFileAsync(
        string sourcePath,
        CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Stack logo backup material was not found.",
                fullPath);
        }

        var length = new FileInfo(fullPath).Length;
        if (length <= 0)
        {
            throw new InvalidDataException("Stack logo backup material is empty.");
        }

        if (length > MaximumBytes)
        {
            throw new InvalidDataException("Stack logo backup material exceeds the 2 MiB limit.");
        }

        return await File.ReadAllBytesAsync(fullPath, ct);
    }

    private static RuntimeStackLogoMetadata ValidatePng(byte[] bytes)
    {
        if (bytes.Length < 45 || !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            throw new RuntimeStackLogoValidationException(
                "stack_logo_not_png",
                "Stack logos must be PNG images. JPEG, WebP, SVG, and other formats are not accepted.");
        }

        var offset = PngSignature.Length;
        var sawHeader = false;
        var sawImageData = false;
        var sawEnd = false;
        var width = 0;
        var height = 0;

        while (offset + 12 <= bytes.Length)
        {
            var chunkLengthValue = BinaryPrimitives.ReadUInt32BigEndian(
                bytes.AsSpan(offset, 4));
            if (chunkLengthValue > int.MaxValue)
            {
                throw new RuntimeStackLogoValidationException(
                    "stack_logo_png_invalid",
                    "The PNG file contains an unsupported chunk length.");
            }

            var chunkLength = (int)chunkLengthValue;
            var chunkEnd = (long)offset + 12L + chunkLength;
            if (chunkEnd > bytes.Length)
            {
                throw new RuntimeStackLogoValidationException(
                    "stack_logo_png_invalid",
                    "The PNG file is truncated or has an invalid chunk length.");
            }

            var type = bytes.AsSpan(offset + 4, 4);
            var data = bytes.AsSpan(offset + 8, chunkLength);

            if (!sawHeader)
            {
                if (!type.SequenceEqual("IHDR"u8) || chunkLength != 13)
                {
                    throw new RuntimeStackLogoValidationException(
                        "stack_logo_png_invalid",
                        "The PNG file does not begin with a valid IHDR header.");
                }

                var widthValue = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
                var heightValue = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
                if (widthValue > int.MaxValue || heightValue > int.MaxValue)
                {
                    throw new RuntimeStackLogoValidationException(
                        "stack_logo_dimensions_too_large",
                        $"Stack logos may be at most {MaximumDimension}×{MaximumDimension} pixels.");
                }

                width = (int)widthValue;
                height = (int)heightValue;
                ValidateDimensions(width, height);
                sawHeader = true;
            }
            else if (type.SequenceEqual("IHDR"u8))
            {
                throw new RuntimeStackLogoValidationException(
                    "stack_logo_png_invalid",
                    "The PNG file contains more than one IHDR header.");
            }

            if (type.SequenceEqual("IDAT"u8))
            {
                sawImageData = true;
            }

            if (type.SequenceEqual("IEND"u8))
            {
                if (chunkLength != 0 || chunkEnd != bytes.Length)
                {
                    throw new RuntimeStackLogoValidationException(
                        "stack_logo_png_invalid",
                        "The PNG file has an invalid IEND marker or trailing data.");
                }

                sawEnd = true;
                break;
            }

            offset = checked(offset + 12 + chunkLength);
        }

        if (!sawHeader || !sawImageData || !sawEnd)
        {
            throw new RuntimeStackLogoValidationException(
                "stack_logo_png_invalid",
                "The PNG file is incomplete and cannot be used as a stack logo.");
        }

        return new RuntimeStackLogoMetadata(
            Sha256: Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Bytes: bytes.LongLength,
            Width: width,
            Height: height);
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width < MinimumDimension || height < MinimumDimension)
        {
            throw new RuntimeStackLogoValidationException(
                "stack_logo_too_small",
                $"Stack logos must be at least {MinimumDimension}×{MinimumDimension} pixels.");
        }

        if (width > MaximumDimension || height > MaximumDimension)
        {
            throw new RuntimeStackLogoValidationException(
                "stack_logo_dimensions_too_large",
                $"Stack logos may be at most {MaximumDimension}×{MaximumDimension} pixels.");
        }
    }

    private string GetStackAssetDirectory(Guid stackId) =>
        Path.Combine(
            ResolveDataRoot(),
            "control-plane",
            "runtime-stack-assets",
            stackId.ToString("N"));

    private string GetLogoPath(Guid stackId, string sha256) =>
        Path.Combine(
            GetStackAssetDirectory(stackId),
            $"logo-{sha256}.png");

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static void DeleteSupersededLogos(string directory, string retainedPath)
    {
        foreach (var path in Directory.EnumerateFiles(directory, "logo-*.png"))
        {
            if (!string.Equals(path, retainedPath, StringComparison.Ordinal))
            {
                TryDeleteFile(path);
            }
        }
    }

    private void DeleteStackAssetsBestEffort(Guid stackId)
    {
        var directory = GetStackAssetDirectory(stackId);
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // The manifest is already authoritative and no longer exposes the logo.
            // Stack destruction performs a strict bounded cleanup of this asset directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale as above. Do not restore a removed logo in operator state merely
            // because its now-unreferenced payload could not be cleaned immediately.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // The file is no longer referenced by stack metadata. A later replacement
            // or stack destruction will retry bounded cleanup of the dedicated asset root.
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale as above: never roll back truthful manifest state merely
            // because an unreferenced logo payload could not be cleaned immediately.
        }
    }
}

public sealed record RuntimeStackLogoReadResult(
    string Path,
    string ContentType,
    string Sha256,
    long Bytes,
    int Width,
    int Height);

public sealed class RuntimeStackLogoValidationException : Exception
{
    public RuntimeStackLogoValidationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
