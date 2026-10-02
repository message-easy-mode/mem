using System;

namespace Shared.Contracts.Dtos;

public sealed record PaginatedResultDto<T>(
    int PageIndex,
    int PageSize,
    long Count,
    IReadOnlyList<T> Data
);