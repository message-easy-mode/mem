using System.Security.Cryptography;
using System.Text;

namespace Mem.Migrate.Web.Security;

internal sealed record GeneratedAccessCode(AccessCodeCredential Credential, string DisplayCode);

internal sealed class AccessCodeCredential
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CharacterCount = 28;

    private readonly byte[] salt;
    private readonly byte[] verifier;

    private AccessCodeCredential(byte[] salt, byte[] verifier)
    {
        this.salt = salt;
        this.verifier = verifier;
    }

    public static GeneratedAccessCode Generate()
    {
        var random = RandomNumberGenerator.GetBytes(CharacterCount);
        Span<char> characters = stackalloc char[CharacterCount];
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = Alphabet[random[index] & 31];
        }

        CryptographicOperations.ZeroMemory(random);
        var normalized = new string(characters);
        var displayCode = string.Join("-", Enumerable.Range(0, CharacterCount / 4)
            .Select(group => normalized.Substring(group * 4, 4)));
        return new GeneratedAccessCode(Create(normalized), displayCode);
    }

    internal static AccessCodeCredential Create(string code)
    {
        var normalized = Normalize(code);
        if (normalized.Length != CharacterCount || normalized.Any(character => Alphabet.IndexOf(character) < 0))
        {
            throw new ArgumentException("Access code must use the generated Source Assistant format.", nameof(code));
        }

        var salt = RandomNumberGenerator.GetBytes(32);
        var verifier = CalculateVerifier(salt, normalized);
        return new AccessCodeCredential(salt, verifier);
    }

    public bool Verify(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalized = Normalize(candidate);
        if (normalized.Length != CharacterCount)
        {
            return false;
        }

        var candidateVerifier = CalculateVerifier(salt, normalized);
        try
        {
            return CryptographicOperations.FixedTimeEquals(verifier, candidateVerifier);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(candidateVerifier);
        }
    }

    private static byte[] CalculateVerifier(byte[] key, string normalized)
    {
        var bytes = Encoding.UTF8.GetBytes(normalized);
        try
        {
            return HMACSHA256.HashData(key, bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is '-' or ' ' or '\t' or '\r' or '\n')
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }
}
