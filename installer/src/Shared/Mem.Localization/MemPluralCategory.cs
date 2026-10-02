namespace Mem.Localization;

/// <summary>
/// Resource suffixes are deliberately explicit so languages with richer plural
/// systems can be added without changing the message-code shape later.
/// </summary>
public enum MemPluralCategory
{
    Zero,
    One,
    Two,
    Few,
    Many,
    Other
}
