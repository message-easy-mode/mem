namespace Shared.Contracts.Dtos;

public record MoneyDto(decimal Amount, string Currency)
{
    public static MoneyDto Zero(string currency = "USD") => new(0m, currency);
}