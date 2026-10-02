namespace Shared.Contracts.Dtos;

public record AddressDto(
    string Line1,
    string? Line2,
    string City,
    string StateOrProvince,
    string PostalCode,
    string Country,
    string? Company
);