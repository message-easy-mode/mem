using System;

namespace Shared.ValueObjects;

public record Weight(decimal Value, string Unit = "kg")
{
    public static Weight Zero(string unit = "kg") => new(0m, unit);
}