using System;

namespace HostAgent.Runtime.Databases;

public sealed record RuntimeStackPostgresSettings(
    string Host,
    int Port,
    string DatabaseName,
    string Username,
    string Password);

public sealed record RuntimeStackDatabaseProvisioningResult(
    Guid RuntimeStackId,
    string DatabaseEngine,
    string DatabaseHost,
    int DatabasePort,
    string DatabaseName,
    string DatabaseUsername,
    string PasswordSecretKind,
    string Status,
    bool DatabaseCreatedOrVerified,
    bool UserCreatedOrVerified)
{
    public RuntimeStackPostgresSettings ToSynapseSettings(string password) =>
        new(DatabaseHost, DatabasePort, DatabaseName, DatabaseUsername, password);
}