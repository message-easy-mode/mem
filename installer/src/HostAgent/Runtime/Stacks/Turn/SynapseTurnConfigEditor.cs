using System.Text;
using HostAgent.Runtime.Coturn;

namespace HostAgent.Runtime.Stacks.Turn;

public sealed class SynapseTurnConfigEditor
{
    private static readonly string[] TurnKeys =
    [
        "turn_uris",
        "turn_shared_secret",
        "turn_shared_secret_path",
        "turn_user_lifetime",
        "turn_allow_guests"
    ];

    private const string ManagedMarker = "# MEM-managed TURN configuration for Matrix voice/video calls.";
    private const string PublicHostComment = "# TURN public host:";
    private const string RealmComment = "# TURN realm:";

    public byte[] RenderConnect(
        byte[] currentBytes,
        CoturnSynapseConfig coturn)
    {
        ArgumentNullException.ThrowIfNull(currentBytes);
        ArgumentNullException.ThrowIfNull(coturn);

        var lines = ReadLines(
            currentBytes,
            "turn_connect_config_invalid_utf8");
        foreach (var key in TurnKeys)
        {
            RemoveTopLevelBlock(lines, key);
        }

        lines.RemoveAll(line =>
        {
            var trimmed = line.Trim();
            return string.Equals(trimmed, ManagedMarker, StringComparison.Ordinal) ||
                   trimmed.StartsWith(PublicHostComment, StringComparison.Ordinal) ||
                   trimmed.StartsWith(RealmComment, StringComparison.Ordinal);
        });

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        lines.Add(string.Empty);
        lines.Add(ManagedMarker);
        lines.Add($"{PublicHostComment} {EscapeYaml(coturn.PublicHost)}");
        lines.Add($"{RealmComment} {EscapeYaml(coturn.Realm)}");
        lines.Add("turn_uris:");
        foreach (var uri in coturn.TurnUris)
        {
            lines.Add($"  - \"{EscapeYaml(uri)}\"");
        }

        lines.Add($"turn_shared_secret: \"{EscapeYaml(coturn.SharedSecret)}\"");
        lines.Add($"turn_user_lifetime: \"{EscapeYaml(coturn.UserLifetime)}\"");
        lines.Add($"turn_allow_guests: {(coturn.AllowGuests ? "true" : "false")}");
        lines.Add(string.Empty);

        return new UTF8Encoding(false).GetBytes(string.Join("\n", lines));
    }


    public byte[] RenderPreserve(
        byte[] currentBytes,
        byte[] sourceBytes)
    {
        ArgumentNullException.ThrowIfNull(currentBytes);
        ArgumentNullException.ThrowIfNull(sourceBytes);

        var lines = ReadLines(
            currentBytes,
            "turn_preserve_target_config_invalid_utf8");
        var sourceLines = ReadLines(
            sourceBytes,
            "turn_preserve_source_config_invalid_utf8");

        foreach (var key in TurnKeys)
        {
            RemoveTopLevelBlock(lines, key);
        }

        lines.RemoveAll(IsManagedTurnComment);

        var comments = sourceLines
            .Where(IsManagedTurnComment)
            .Select(line => line.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var blocks = TurnKeys
            .Select(key => CaptureTopLevelBlock(sourceLines, key))
            .Where(block => block.Count > 0)
            .ToArray();

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (comments.Length > 0 || blocks.Length > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(comments);
            foreach (var block in blocks)
            {
                lines.AddRange(block);
            }
        }

        lines.Add(string.Empty);
        return new UTF8Encoding(false).GetBytes(string.Join("\n", lines));
    }

    public byte[] RenderDisconnect(byte[] currentBytes)
    {
        ArgumentNullException.ThrowIfNull(currentBytes);

        var lines = ReadLines(
            currentBytes,
            "turn_disconnect_config_invalid_utf8");
        foreach (var key in TurnKeys)
        {
            RemoveTopLevelBlock(lines, key);
        }

        lines.RemoveAll(line =>
        {
            var trimmed = line.Trim();
            return string.Equals(trimmed, ManagedMarker, StringComparison.Ordinal) ||
                   trimmed.StartsWith(PublicHostComment, StringComparison.Ordinal) ||
                   trimmed.StartsWith(RealmComment, StringComparison.Ordinal);
        });

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        lines.Add(string.Empty);
        return new UTF8Encoding(false).GetBytes(string.Join("\n", lines));
    }

    private static List<string> ReadLines(
        byte[] currentBytes,
        string errorCode)
    {
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(currentBytes);
        }
        catch (DecoderFallbackException)
        {
            throw new RuntimeStackTurnConnectionException(
                errorCode,
                "The active Synapse configuration is not valid UTF-8 text and cannot be edited safely.");
        }

        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        return Normalise(text).Split('\n').ToList();
    }

    private static void RemoveTopLevelBlock(
        List<string> lines,
        string key)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            var candidate = StripInlineComment(line).TrimEnd();
            if (!candidate.StartsWith(key + ":", StringComparison.Ordinal))
            {
                continue;
            }

            var end = index + 1;
            while (end < lines.Count)
            {
                var next = lines[end];
                if (next.Length > 0 && !char.IsWhiteSpace(next[0]))
                {
                    break;
                }

                end++;
            }

            lines.RemoveRange(index, end - index);
            index--;
        }
    }


    private static IReadOnlyList<string> CaptureTopLevelBlock(
        IReadOnlyList<string> lines,
        string key)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            var candidate = StripInlineComment(line).TrimEnd();
            if (!candidate.StartsWith(key + ":", StringComparison.Ordinal))
            {
                continue;
            }

            var block = new List<string> { line };
            for (var cursor = index + 1; cursor < lines.Count; cursor++)
            {
                var next = lines[cursor];
                if (next.Length > 0 && !char.IsWhiteSpace(next[0]))
                {
                    break;
                }

                block.Add(next);
            }

            while (block.Count > 1 && block[^1].Length == 0)
            {
                block.RemoveAt(block.Count - 1);
            }

            return block;
        }

        return [];
    }

    private static bool IsManagedTurnComment(string line)
    {
        var trimmed = line.Trim();
        return string.Equals(trimmed, ManagedMarker, StringComparison.Ordinal) ||
               trimmed.StartsWith(PublicHostComment, StringComparison.Ordinal) ||
               trimmed.StartsWith(RealmComment, StringComparison.Ordinal);
    }

    private static string StripInlineComment(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '#' &&
                (index == 0 || char.IsWhiteSpace(value[index - 1])))
            {
                return value[..index];
            }
        }

        return value;
    }

    private static string EscapeYaml(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
