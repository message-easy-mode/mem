using Api.Runtime;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth;
using Modules.Auth.Services.Identity;

namespace Api.Recovery;

/// <summary>
/// Host-only console entry point for emergency Platform Owner password recovery.
///
/// This command intentionally has no HTTP equivalent. Invoking it requires
/// local process/container execution authority on the Control Plane host.
/// </summary>
public static class MemOperatorHostRecoveryCommand
{
    private const string Command = "operator";
    private const string Action = "reset-password";

    public static bool IsRequested(string[] args) =>
        args.Length >= 2 &&
        string.Equals(args[0], Command, StringComparison.Ordinal) &&
        string.Equals(args[1], Action, StringComparison.Ordinal);

    public static async Task<int> RunAsync(string[] args)
    {
        if (!TryParseUsername(args, out var username))
        {
            WriteUsage();
            return 2;
        }

        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine(
                "ERROR: Host password recovery requires an interactive terminal. " +
                "Do not pass passwords through command arguments or redirected stdin.");
            return 2;
        }

        Console.WriteLine("MEM host-authoritative Platform Owner password recovery");
        Console.WriteLine();
        Console.WriteLine(
            "This changes only the selected Platform Owner password and session state.");
        Console.WriteLine(
            "Existing TOTP configuration, recovery codes, roles and platform data are preserved.");
        Console.WriteLine(
            "All existing MEM sessions for this account will be revoked.");
        Console.WriteLine();

        Console.Write($"Type the Platform Owner username '{username}' to continue: ");
        var confirmation = Console.ReadLine()?.Trim();

        if (!string.Equals(confirmation, username, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Recovery cancelled: username confirmation did not match.");
            return 3;
        }

        var newPassword = ReadSecret("New password: ");
        var confirmedPassword = ReadSecret("Confirm new password: ");

        if (!string.Equals(newPassword, confirmedPassword, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Recovery cancelled: password confirmation did not match.");
            return 3;
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            Console.Error.WriteLine("Recovery cancelled: the new password may not be empty.");
            return 3;
        }

        try
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    Args = Array.Empty<string>()
                });

            builder.Logging.ClearProviders();

            var sqlitePath = MemControlPlaneSqlitePathResolver.Resolve(
                builder.Configuration,
                builder.Environment);

            if (!File.Exists(sqlitePath))
            {
                Console.Error.WriteLine(
                    "ERROR: The MEM Control Plane database was not found. " +
                    "Recovery will not create a new database.");
                return 4;
            }

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = sqlitePath,
                Cache = SqliteCacheMode.Shared,
                Mode = SqliteOpenMode.ReadWrite
            }.ToString();

            builder.Services.AddDataProtection();
            builder.Services.AddDbContext<MemDbContext>(
                options => options.UseSqlite(connectionString));
            builder.Services.AddMemOperatorIdentity(
                builder.Configuration,
                builder.Environment);

            await using var provider = builder.Services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();

            var recovery = scope.ServiceProvider
                .GetRequiredService<IMemOperatorHostRecoveryService>();

            var result = await recovery.ResetPlatformOwnerPasswordAsync(
                username,
                newPassword,
                correlationId: $"host-recovery-{Guid.NewGuid():N}",
                CancellationToken.None);

            Console.WriteLine();
            Console.WriteLine($"Password reset succeeded for Platform Owner '{result.Username}'.");
            Console.WriteLine("Existing TOTP configuration was preserved.");
            Console.WriteLine(
                $"Existing recovery-code set was preserved ({result.RecoveryCodeCount} code(s) remaining).");
            Console.WriteLine("Existing MEM sessions were revoked.");
            Console.WriteLine("Sign in with the new password and the existing authenticator code.");

            return 0;
        }
        catch (MemOperatorHostRecoveryException exception)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            if (exception.IdentityErrors.Count > 0)
            {
                foreach (var error in exception.IdentityErrors)
                {
                    Console.Error.WriteLine($"  {error.Code}: {error.Description}");
                }
            }

            return 5;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                $"ERROR: Host recovery failed ({exception.GetType().Name}). " +
                "No password or authentication secret was written to the console.");
            return 6;
        }
    }

    private static bool TryParseUsername(
        string[] args,
        out string username)
    {
        username = string.Empty;

        if (args.Length != 3 ||
            !string.Equals(args[0], Command, StringComparison.Ordinal) ||
            !string.Equals(args[1], Action, StringComparison.Ordinal))
        {
            return false;
        }

        username = args[2].Trim();
        return !string.IsNullOrWhiteSpace(username) &&
            username.Length <= 256 &&
            username.All(character =>
                !char.IsControl(character) &&
                !char.IsWhiteSpace(character));
    }

    private static string ReadSecret(string prompt)
    {
        Console.Write(prompt);
        var characters = new List<char>();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return new string(characters.ToArray());
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (characters.Count > 0)
                {
                    characters.RemoveAt(characters.Count - 1);
                }

                continue;
            }

            if (key.Key == ConsoleKey.C &&
                key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                Console.WriteLine();
                throw new OperationCanceledException();
            }

            if (!char.IsControl(key.KeyChar))
            {
                characters.Add(key.KeyChar);
            }
        }
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine(
            "Usage: dotnet Api.dll operator reset-password <platform-owner-username>");
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            "Run this only from a trusted shell on the MEM Control Plane host.");
        Console.Error.WriteLine(
            "The command prompts for the new password without echoing it.");
    }
}
