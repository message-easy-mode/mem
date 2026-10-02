using System;

namespace Shared.ValueObjects;

public sealed record Contact(string FirstName, string LastName, string Email, string? Phone);