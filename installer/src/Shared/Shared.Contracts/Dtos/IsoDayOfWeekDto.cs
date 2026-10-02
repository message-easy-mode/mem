using System;

namespace Shared.Contracts.Dtos;

/// <summary>
/// ISO day-of-week: Monday=1 .. Sunday=7.
/// Named explicitly to prevent "day=3 means what?" bugs.
/// </summary>
public sealed record IsoDayOfWeekDto(int IsoDayOfWeek);