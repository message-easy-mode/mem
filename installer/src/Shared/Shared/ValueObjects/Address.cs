using System;
using System.Text.Json.Serialization;

namespace Shared.ValueObjects;

/// <summary>
/// Postal address for shipping / billing / warehouses / customers.
/// CountryCode should be ISO 3166-1 alpha-2 (e.g. "US", "GB", "NZ").
/// </summary>
public record Address
{
    public string Line1 { get; init; }
    public string? Line2 { get; init; }
    public string City { get; init; }
    public string StateOrProvince { get; init; }
    public string PostalCode { get; init; }
    public string CountryCode { get; init; }
    public string? Company { get; init; }

    // ✅ Parameter names now match your named-argument usage
    [JsonConstructor]
    public Address(
        string Line1,
        string? Line2,
        string City,
        string StateOrProvince,
        string PostalCode,
        string CountryCode,
        string? Company = null)
    {
        if (string.IsNullOrWhiteSpace(Line1))
            throw new ArgumentException("Line1 is required.", nameof(Line1));
        if (string.IsNullOrWhiteSpace(City))
            throw new ArgumentException("City is required.", nameof(City));
        if (string.IsNullOrWhiteSpace(PostalCode))
            throw new ArgumentException("PostalCode is required.", nameof(PostalCode));
        if (string.IsNullOrWhiteSpace(StateOrProvince))
            throw new ArgumentException("StateOrProvince is required.", nameof(StateOrProvince));
        if (string.IsNullOrWhiteSpace(CountryCode))
            throw new ArgumentException("CountryCode is required.", nameof(CountryCode));

        this.Line1          = Line1.Trim();
        this.Line2          = string.IsNullOrWhiteSpace(Line2) ? null : Line2.Trim();
        this.City           = City.Trim();
        this.StateOrProvince = StateOrProvince.Trim();
        this.PostalCode     = PostalCode.Trim();
        this.CountryCode    = CountryCode.Trim().ToUpperInvariant();
        this.Company        = string.IsNullOrWhiteSpace(Company) ? null : Company.Trim();
    }

    public override string ToString()
        => $"{Line1}"
           + (Line2 is not null ? $", {Line2}" : string.Empty)
           + $", {City}, {StateOrProvince} {PostalCode}, {CountryCode}";
}
