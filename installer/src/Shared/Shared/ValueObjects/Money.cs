namespace Shared.ValueObjects;

public readonly record struct Money(decimal Amount, string Currency)
{
    // === FACTORIES ===========================================================

    public static Money Zero(string currency = "USD") => new(0m, currency);

    public static Money FromMinorUnits(long minorUnits, string currency, int decimals = 2)
        => new(minorUnits / (decimal)Math.Pow(10, decimals), currency);

    // === PROPERTIES ==========================================================

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    public bool IsPositive => Amount > 0m;

    // === METHODS =============================================================

    public Money Round(int decimals = 2) => new(Math.Round(Amount, decimals), Currency);

    public Money Abs() => new(Math.Abs(Amount), Currency);

    public Money Multiply(decimal factor) => new(Math.Round(Amount * factor, 2), Currency);

    public Money Divide(decimal divisor)
    {
        if (divisor == 0) throw new DivideByZeroException();
        return new(Math.Round(Amount / divisor, 2), Currency);
    }

    public override string ToString() => $"{Amount:0.00} {Currency.ToUpperInvariant()}";

    // === OPERATORS ===========================================================

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount + b.Amount, a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount - b.Amount, a.Currency);
    }

    public static bool operator >(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a.Amount > b.Amount;
    }

    public static bool operator <(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a.Amount < b.Amount;
    }

    public static bool operator >=(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a.Amount >= b.Amount;
    }

    public static bool operator <=(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return a.Amount <= b.Amount;
    }

    // === HELPERS =============================================================

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (!a.Currency.Equals(b.Currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Currency mismatch: {a.Currency} vs {b.Currency}");
    }
}
