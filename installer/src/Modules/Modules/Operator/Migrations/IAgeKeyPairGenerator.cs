namespace Modules.Operator.Migrations;

public sealed record AgeKeyPair(string Recipient, string Identity);

public interface IAgeKeyPairGenerator
{
    Task<AgeKeyPair> GenerateAsync(CancellationToken cancellationToken);
}
