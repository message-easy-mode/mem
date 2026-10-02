namespace Shared.Contracts.Dtos;

public record ContactDto(
    string FirstName,
    string LastName,
    string Email,
    string? Phone
);