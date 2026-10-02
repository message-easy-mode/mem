using System;
using Shared.Contracts.Dtos;
using Shared.ValueObjects;

namespace Shared.Mappers;


public static class ContactMappers
{
    public static Contact ToValue(this ContactDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Contact(
            FirstName: dto.FirstName.Trim(),
            LastName: dto.LastName.Trim(),
            Email: dto.Email.Trim(),
            Phone: string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim()
        );
    }

    public static ContactDto ToDto(this Contact value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new ContactDto(
            FirstName: value.FirstName,
            LastName: value.LastName,
            Email: value.Email,
            Phone: value.Phone
        );
    }
}