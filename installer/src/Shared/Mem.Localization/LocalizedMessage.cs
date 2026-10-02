namespace Mem.Localization;

/// <summary>
/// A stable semantic message code plus named arguments. CLI presentation may
/// render a known code through a local catalogue; API problem responses and
/// structured events may expose the same descriptor without shipping English
/// prose as their machine contract. See StructuredApiMessageConvention.md.
/// </summary>
public sealed record LocalizedMessage(
    string Code,
    IReadOnlyDictionary<string, object?> Arguments);
