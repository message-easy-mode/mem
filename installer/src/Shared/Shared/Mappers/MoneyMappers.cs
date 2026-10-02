using System;
using Shared.Contracts.Dtos;
using Shared.ValueObjects;

namespace Shared.Mappers;


public static class MoneyMappers
{
    public static Money ToValue(this MoneyDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Money(
            Amount: dto.Amount,
            Currency: string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency.Trim().ToUpperInvariant()
        );
    }

    public static MoneyDto ToDto(this Money value)
        => new(value.Amount, value.Currency);
}