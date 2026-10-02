using System;
using Shared.Contracts.Dtos;
using Shared.ValueObjects;

namespace Shared.Mappers;


public static class AddressMappers
{
    public static Address ToValue(this AddressDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Address(
            Line1: dto.Line1,
            Line2: dto.Line2,
            City: dto.City,
            StateOrProvince: dto.StateOrProvince,
            PostalCode: dto.PostalCode,
            CountryCode: dto.Country,   // ✅ maps dto.Country -> Address.CountryCode
            Company: dto.Company
        );
    }

    public static AddressDto ToDto(this Address value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new AddressDto(
            Line1: value.Line1,
            Line2: value.Line2,
            City: value.City,
            StateOrProvince: value.StateOrProvince,
            PostalCode: value.PostalCode,
            Country: value.CountryCode, // ✅ maps Address.CountryCode -> dto.Country
            Company: value.Company
        );
    }
}