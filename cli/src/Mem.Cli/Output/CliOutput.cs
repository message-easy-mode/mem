using System.Text.Json;
using System.Text.Json.Serialization;
using Mem.Cli.Config;
using Mem.Localization;

namespace Mem.Cli.Output;

/// <summary>
/// The single presentation boundary for mem-cli human output and JSON output.
/// Human-facing output may use the configured localiser. JSON output is a
/// stable machine contract and deliberately never creates or reads a localiser.
/// </summary>
public sealed class CliOutput
{
    private readonly Func<IMemLocalizer> _localizerFactory;
    private readonly TextWriter _standardOutput;
    private readonly TextWriter _standardError;
    private IMemLocalizer? _localizer;

    public CliOutput(
        bool json,
        Func<IMemLocalizer> localizerFactory,
        TextWriter? standardOutput = null,
        TextWriter? standardError = null)
    {
        ArgumentNullException.ThrowIfNull(localizerFactory);

        IsJson = json;
        _localizerFactory = localizerFactory;
        _standardOutput = standardOutput ?? Console.Out;
        _standardError = standardError ?? Console.Error;
    }

    /// <summary>
    /// Gets whether the active command requested the stable machine-readable
    /// JSON representation.
    /// </summary>
    public bool IsJson { get; }

    /// <summary>
    /// Creates output for one CLI invocation. The localiser is intentionally
    /// lazy so JSON commands do not initialise localisation at all.
    /// </summary>
    public static CliOutput Create(
        CliOptions options,
        TextWriter? standardOutput = null,
        TextWriter? standardError = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new CliOutput(
            options.Json,
            () => new MemLocalizer(options.Language),
            standardOutput,
            standardError);
    }


    /// <summary>
    /// Uses a newly selected language for the rest of the current command
    /// invocation. This is intentionally a human-output-only concern; JSON
    /// contracts remain stable English regardless of the active localiser.
    /// </summary>
    public void UseLanguage(MemLanguage language)
    {
        _localizer = new MemLocalizer(language);
    }

    /// <summary>
    /// Writes raw human-readable output. Use this for trusted raw diagnostics,
    /// API detail text preserved verbatim, and current human usage errors.
    /// </summary>
    public void WriteHumanLine(string? value = null)
    {
        _standardOutput.WriteLine(value);
    }

    /// <summary>
    /// Writes raw human-readable error output. Use this for trusted raw
    /// diagnostics and current human usage errors.
    /// </summary>
    public void WriteHumanErrorLine(string? value = null)
    {
        _standardError.WriteLine(value);
    }

    /// <summary>
    /// Resolves and writes a localised human-facing message.
    /// </summary>
    public void WriteLocalizedLine(
        string messageKey,
        IReadOnlyDictionary<string, object?>? values = null)
    {
        WriteHumanLine(
            FormatLocalized(
                messageKey,
                values));
    }

    /// <summary>
    /// Resolves a localised human-facing message for use in composed terminal
    /// output such as tables and label/value fields.
    /// </summary>
    public string FormatLocalized(
        string messageKey,
        IReadOnlyDictionary<string, object?>? values = null)
    {
        return GetLocalizer().Format(
            messageKey,
            values);
    }

    /// <summary>
    /// Resolves a pluralised localised human-facing message.
    /// </summary>
    public string FormatLocalizedPlural(
        string messageKey,
        decimal count,
        IReadOnlyDictionary<string, object?>? values = null)
    {
        return GetLocalizer().FormatPlural(
            messageKey,
            count,
            values);
    }

    /// <summary>
    /// Formats a numeric human-facing value with the configured culture.
    /// </summary>
    public string FormatNumber(
        decimal value,
        string format = "N2")
    {
        return GetLocalizer().FormatNumber(
            value,
            format);
    }

    /// <summary>
    /// Formats a date/time human-facing value with the configured culture.
    /// </summary>
    public string FormatDateTime(
        DateTimeOffset value,
        string format = "G")
    {
        return GetLocalizer().FormatDateTime(
            value,
            format);
    }

    /// <summary>
    /// Resolves and writes a localised human-facing error message.
    /// </summary>
    public void WriteLocalizedErrorLine(
        string messageKey,
        IReadOnlyDictionary<string, object?>? values = null)
    {
        WriteHumanErrorLine(
            FormatLocalized(
                messageKey,
                values));
    }

    /// <summary>
    /// Renders global CLI help through the configured localiser.
    /// </summary>
    public void WriteHelp()
    {
        CliHelp.Write(
            _standardOutput,
            GetLocalizer());
    }

    /// <summary>
    /// Renders contextual help for one operational command group without
    /// requiring profile, credential, or network authority.
    /// </summary>
    public void WriteGroupHelp(string command)
    {
        CliHelp.WriteGroup(
            _standardOutput,
            GetLocalizer(),
            command);
    }

    /// <summary>
    /// Writes the stable JSON projection. This path intentionally does not
    /// resolve, construct, or otherwise depend on localisation.
    /// </summary>
    public void WriteJson<T>(T value)
    {
        _standardOutput.WriteLine(JsonSerializer.Serialize(
            value,
            JsonOptions()));
    }

    public static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    private IMemLocalizer GetLocalizer()
    {
        return _localizer ??= _localizerFactory();
    }
}
