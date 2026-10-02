namespace Shared.ValueObjects;

public static class MoneyRounding
{
    public static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}