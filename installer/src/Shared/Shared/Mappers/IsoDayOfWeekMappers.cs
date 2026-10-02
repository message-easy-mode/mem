using System;
using Shared.Contracts.Dtos;
using Shared.ValueObjects;

namespace Shared.Mappers;

public static class IsoDayOfWeekMappers
{
    public static IsoDayOfWeek ToValue(this IsoDayOfWeekDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return IsoDayOfWeek.FromIso(dto.IsoDayOfWeek);
    }

    public static IsoDayOfWeekDto ToDto(this IsoDayOfWeek value)
        => new(value.Value);
}
