using System;

namespace Shared.ValueObjects;

public record Dimensions(
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm)
{
    public decimal VolumeCubicCm => LengthCm * WidthCm * HeightCm;
}