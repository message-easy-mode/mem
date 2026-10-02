using System;

namespace Shared.ValueObjects;


/// <summary>
/// ISO-8601 day-of-week: Monday=1 ... Sunday=7.
/// Use this for business/calendar logic (hours, staffing, schedules).
/// </summary>
public readonly record struct IsoDayOfWeek(int Value)
{
    // === FACTORIES ===========================================================

    public static IsoDayOfWeek Monday => new(1);
    public static IsoDayOfWeek Tuesday => new(2);
    public static IsoDayOfWeek Wednesday => new(3);
    public static IsoDayOfWeek Thursday => new(4);
    public static IsoDayOfWeek Friday => new(5);
    public static IsoDayOfWeek Saturday => new(6);
    public static IsoDayOfWeek Sunday => new(7);

    public static IsoDayOfWeek FromIso(int iso)
        => iso is >= 1 and <= 7
            ? new IsoDayOfWeek(iso)
            : throw new ArgumentOutOfRangeException(nameof(iso), iso, "ISO day-of-week must be 1..7 (Mon..Sun).");

    /// <summary>
    /// Transitional helper: accepts either ISO(1..7) or .NET numeric (0..6, Sunday=0).
    /// ONLY use while migrating old persisted data. Emit ISO everywhere.
    /// </summary>
    public static IsoDayOfWeek FromIsoOrDotNetInt(int value)
    {
        if (value is >= 1 and <= 7) return new IsoDayOfWeek(value);
        if (value is >= 0 and <= 6) return FromDotNet((DayOfWeek)value);
        throw new ArgumentOutOfRangeException(nameof(value), value, "Expected ISO 1..7 or .NET 0..6.");
    }

    public static IsoDayOfWeek FromDotNet(DayOfWeek dow)
        => dow == DayOfWeek.Sunday ? Sunday : new IsoDayOfWeek((int)dow); // Mon=1..Sat=6

    // === PROPERTIES ==========================================================

    public bool IsWeekend => Value is 6 or 7;

    // === METHODS =============================================================

    public DayOfWeek ToDotNet()
        => Value switch
        {
            1 => DayOfWeek.Monday,
            2 => DayOfWeek.Tuesday,
            3 => DayOfWeek.Wednesday,
            4 => DayOfWeek.Thursday,
            5 => DayOfWeek.Friday,
            6 => DayOfWeek.Saturday,
            7 => DayOfWeek.Sunday,
            _ => throw new InvalidOperationException("Invalid ISO day-of-week state.")
        };

    /// <summary>
    /// JS Date.getDay(): Sunday=0..Saturday=6
    /// </summary>
    public int ToJsDay() => Value == 7 ? 0 : Value;

    public override string ToString() => Value switch
    {
        1 => "Mon",
        2 => "Tue",
        3 => "Wed",
        4 => "Thu",
        5 => "Fri",
        6 => "Sat",
        7 => "Sun",
        _ => $"IsoDay({Value})"
    };

    // === OPERATORS ===========================================================

    public static implicit operator int(IsoDayOfWeek d) => d.Value;
    public static explicit operator IsoDayOfWeek(int v) => FromIso(v);
}