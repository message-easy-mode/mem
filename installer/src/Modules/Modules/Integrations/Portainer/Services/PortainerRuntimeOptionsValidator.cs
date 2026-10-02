using Core.RuntimeDefinition;

namespace Modules.Integrations.Portainer.Services;

public static class PortainerRuntimeOptionsValidator
{
    private static readonly HashSet<string> AllowedArchitectures =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "amd64",
            "arm64"
        };

    public static IReadOnlyList<string> Validate(PortainerRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        var image = options.ApprovedImageReference?.Trim() ?? string.Empty;
        var expectedVersion = options.ExpectedVersion?.Trim() ?? string.Empty;
        if (!string.Equals(
                image,
                PortainerRuntimeOptions.ApprovedImage,
                StringComparison.Ordinal))
        {
            errors.Add(
                $"Diagnostics:Portainer:ApprovedImageReference must remain the MEM-approved exact image '{PortainerRuntimeOptions.ApprovedImage}'.");
        }

        if (!string.Equals(
                expectedVersion,
                PortainerRuntimeOptions.ApprovedVersion,
                StringComparison.Ordinal))
        {
            errors.Add(
                $"Diagnostics:Portainer:ExpectedVersion must remain the MEM-approved version '{PortainerRuntimeOptions.ApprovedVersion}'.");
        }

        if (!string.Equals(
                options.ContainerName?.Trim(),
                ManagedContainerNames.Portainer,
                StringComparison.Ordinal))
        {
            errors.Add(
                $"Diagnostics:Portainer:ContainerName must remain '{ManagedContainerNames.Portainer}'.");
        }

        if (!string.Equals(
                options.DataVolumeName?.Trim(),
                ManagedVolumeNames.PortainerData,
                StringComparison.Ordinal))
        {
            errors.Add(
                $"Diagnostics:Portainer:DataVolumeName must remain '{ManagedVolumeNames.PortainerData}'.");
        }

        if (options.PreferredHttpsHostPort is < 1 or > 65535)
        {
            errors.Add(
                "Diagnostics:Portainer:PreferredHttpsHostPort must be between 1 and 65535.");
        }

        if (options.AllowOperationalPull)
        {
            errors.Add(
                "Diagnostics:Portainer:AllowOperationalPull must remain false.");
        }

        if (options.EnvironmentId is <= 0)
        {
            errors.Add(
                "Diagnostics:Portainer:EnvironmentId must be a positive integer when configured.");
        }

        if (!string.IsNullOrWhiteSpace(options.UiUrl) &&
            !TryNormalizeUiUrl(options.UiUrl, out _))
        {
            errors.Add(
                "Diagnostics:Portainer:UiUrl must be an absolute HTTP or HTTPS URL without credentials, query, or fragment.");
        }

        var architectures = options.SupportedArchitectures ?? [];
        if (architectures.Length == 0)
        {
            errors.Add(
                "Diagnostics:Portainer:SupportedArchitectures must contain at least one approved architecture.");
        }
        else
        {
            foreach (var architecture in architectures)
            {
                if (!AllowedArchitectures.Contains(architecture?.Trim() ?? string.Empty))
                {
                    errors.Add(
                        $"Diagnostics:Portainer:SupportedArchitectures contains unsupported value '{architecture}'.");
                }
            }
        }

        return errors;
    }

    public static void ThrowIfInvalid(PortainerRuntimeOptions options)
    {
        var errors = Validate(options);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }
    }

    public static bool TryNormalizeUiUrl(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp &&
             parsed.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrWhiteSpace(parsed.UserInfo) ||
            !string.IsNullOrWhiteSpace(parsed.Query) ||
            !string.IsNullOrWhiteSpace(parsed.Fragment))
        {
            return false;
        }

        uri = new UriBuilder(
            parsed.Scheme,
            parsed.Host,
            parsed.IsDefaultPort ? -1 : parsed.Port,
            parsed.AbsolutePath.TrimEnd('/')).Uri;
        return true;
    }
}
