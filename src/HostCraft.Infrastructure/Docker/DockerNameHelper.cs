using System.Text.RegularExpressions;

namespace HostCraft.Infrastructure.Docker;

public static class DockerNameHelper
{
    public static string NormalizeNetworkName(string name)
    {
        var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9-]", "-");
        slug = Regex.Replace(slug, "-+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "hostcraft" : slug;
    }

    /// <summary>
    /// Normalizes a service/container name for Docker compatibility.
    /// Docker service names must match: [a-zA-Z0-9][a-zA-Z0-9_.-]*
    /// </summary>
    public static string NormalizeServiceName(string name)
    {
        // Convert to lowercase and replace invalid chars with hyphen
        var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9_.-]", "-");
        // Collapse multiple hyphens and trim edges
        slug = Regex.Replace(slug, "-+", "-").Trim('-');
        // Ensure we have a valid name
        return string.IsNullOrWhiteSpace(slug) ? "app" : slug;
    }
}
