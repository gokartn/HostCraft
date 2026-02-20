using System.Linq;
using System.Text.Json;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;

namespace HostCraft.Infrastructure.Proxy;

/// <summary>
/// Builds Traefik labels for Docker containers and services.
/// Provides feature parity with Coolify and Dokploy for domain routing.
/// </summary>
public static class TraefikLabelBuilder
{
    /// <summary>
    /// Build complete Traefik labels for an application.
    /// </summary>
    public static Dictionary<string, string> BuildLabels(Application application, string? traefikNetwork = null, bool includeApplicationOverrides = true)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var serviceName = SanitizeName(application.ServiceName);

        var activeDomains = application.Domains?.Where(d => d.IsActive).ToList() ?? new List<Domain>();
        var httpDomains = activeDomains.Where(d => d.ProxyProtocol == ProxyProtocol.Http).ToList();
        var tcpDomains = activeDomains.Where(d => d.ProxyProtocol == ProxyProtocol.Tcp).ToList();
        var hasLegacyDomain = !string.IsNullOrEmpty(application.Domain);

        if (!httpDomains.Any() && !tcpDomains.Any() && !hasLegacyDomain)
        {
            if (includeApplicationOverrides)
            {
                ApplyStoredOverrides(application, labels);
            }

            return labels;
        }

        labels["traefik.enable"] = "true";

        if (!string.IsNullOrEmpty(traefikNetwork))
        {
            labels["traefik.swarm.network"] = traefikNetwork;
        }

        if (httpDomains.Any() || hasLegacyDomain)
        {
            string? primaryDomain = null;
            string? additionalDomains = null;
            int port = application.Port ?? 80;
            bool enableHttps = application.EnableHttps;
            bool forceHttps = application.ForceHttps;

            if (httpDomains.Any())
            {
                var primaryDomainEntity = httpDomains.FirstOrDefault(d => d.IsPrimary)
                                       ?? httpDomains.First();

                primaryDomain = primaryDomainEntity.Host;
                port = primaryDomainEntity.Port;
                enableHttps = primaryDomainEntity.HttpsEnabled;
                forceHttps = primaryDomainEntity.ForceHttps;

                var otherDomains = httpDomains
                    .Where(d => d.Id != primaryDomainEntity.Id)
                    .Select(d => d.Host);

                if (otherDomains.Any())
                {
                    additionalDomains = string.Join(",", otherDomains);
                }
            }
            else if (hasLegacyDomain)
            {
                primaryDomain = application.Domain;
                additionalDomains = application.AdditionalDomains;
            }

            if (!string.IsNullOrEmpty(primaryDomain))
            {
                var hostRules = BuildHostRules(primaryDomain, additionalDomains);

                if (enableHttps)
                {
                    var httpsRouterName = $"{serviceName}-https";
                    labels[$"traefik.http.routers.{httpsRouterName}.rule"] = hostRules;
                    labels[$"traefik.http.routers.{httpsRouterName}.entrypoints"] = "websecure";
                    labels[$"traefik.http.routers.{httpsRouterName}.tls"] = "true";
                    labels[$"traefik.http.routers.{httpsRouterName}.tls.certresolver"] = "letsencrypt";
                    labels[$"traefik.http.routers.{httpsRouterName}.service"] = serviceName;

                    var httpRouterName = $"{serviceName}-http";
                    labels[$"traefik.http.routers.{httpRouterName}.rule"] = hostRules;
                    labels[$"traefik.http.routers.{httpRouterName}.entrypoints"] = "web";

                    if (forceHttps)
                    {
                        // Redirect all HTTP traffic to HTTPS
                        var redirectMiddleware = $"{serviceName}-redirect-https";
                        labels[$"traefik.http.middlewares.{redirectMiddleware}.redirectscheme.scheme"] = "https";
                        labels[$"traefik.http.middlewares.{redirectMiddleware}.redirectscheme.permanent"] = "true";
                        labels[$"traefik.http.routers.{httpRouterName}.middlewares"] = redirectMiddleware;
                        // HTTP router still points to the service for ACME challenge fallback
                        labels[$"traefik.http.routers.{httpRouterName}.service"] = serviceName;
                    }
                    else
                    {
                        // Serve app on both HTTP and HTTPS (no redirect)
                        labels[$"traefik.http.routers.{httpRouterName}.service"] = serviceName;
                    }
                }
                else
                {
                    labels[$"traefik.http.routers.{serviceName}.rule"] = hostRules;
                    labels[$"traefik.http.routers.{serviceName}.entrypoints"] = "web";
                    labels[$"traefik.http.routers.{serviceName}.service"] = serviceName;
                }

                labels[$"traefik.http.services.{serviceName}.loadbalancer.server.port"] = port.ToString();

                // Create separate middlewares for HTTP and HTTPS with correct static protocol values
                // NOTE: Traefik's customrequestheaders does NOT support Go templates - only static values

                // HTTPS middleware - for requests on websecure entrypoint
                labels[$"traefik.http.middlewares.{serviceName}-headers-https.headers.customrequestheaders.X-Forwarded-Proto"] = "https";
                labels[$"traefik.http.middlewares.{serviceName}-headers-https.headers.customresponseheaders.X-Content-Type-Options"] = "nosniff";
                labels[$"traefik.http.middlewares.{serviceName}-headers-https.headers.customresponseheaders.X-Frame-Options"] = "SAMEORIGIN";

                // HTTP middleware - for requests on web entrypoint
                labels[$"traefik.http.middlewares.{serviceName}-headers-http.headers.customrequestheaders.X-Forwarded-Proto"] = "http";
                labels[$"traefik.http.middlewares.{serviceName}-headers-http.headers.customresponseheaders.X-Content-Type-Options"] = "nosniff";
                labels[$"traefik.http.middlewares.{serviceName}-headers-http.headers.customresponseheaders.X-Frame-Options"] = "SAMEORIGIN";

                // Apply correct middleware to each router based on entrypoint
                if (enableHttps)
                {
                    // Apply HTTPS headers middleware to HTTPS router
                    var httpsRouterName = $"{serviceName}-https";
                    var existingHttpsMiddlewares = labels.ContainsKey($"traefik.http.routers.{httpsRouterName}.middlewares")
                        ? labels[$"traefik.http.routers.{httpsRouterName}.middlewares"] + ","
                        : "";
                    labels[$"traefik.http.routers.{httpsRouterName}.middlewares"] = $"{existingHttpsMiddlewares}{serviceName}-headers-https";

                    // Apply HTTP headers middleware to HTTP router
                    var httpRouterName = $"{serviceName}-http";
                    if (labels.ContainsKey($"traefik.http.routers.{httpRouterName}.rule"))
                    {
                        var existingHttpMiddlewares = labels.ContainsKey($"traefik.http.routers.{httpRouterName}.middlewares")
                            ? labels[$"traefik.http.routers.{httpRouterName}.middlewares"] + ","
                            : "";
                        labels[$"traefik.http.routers.{httpRouterName}.middlewares"] = $"{existingHttpMiddlewares}{serviceName}-headers-http";
                    }

                    // Apply HTTP headers middleware to ACME router (Let's Encrypt challenges use HTTP)
                    var acmeRouterName = $"{serviceName}-acme";
                    if (labels.ContainsKey($"traefik.http.routers.{acmeRouterName}.rule"))
                    {
                        var existingAcmeMiddlewares = labels.ContainsKey($"traefik.http.routers.{acmeRouterName}.middlewares")
                            ? labels[$"traefik.http.routers.{acmeRouterName}.middlewares"] + ","
                            : "";
                        labels[$"traefik.http.routers.{acmeRouterName}.middlewares"] = $"{existingAcmeMiddlewares}{serviceName}-headers-http";
                    }
                }
                else
                {
                    // HTTP only - apply HTTP headers middleware to main router
                    var existingMiddlewares = labels.ContainsKey($"traefik.http.routers.{serviceName}.middlewares")
                        ? labels[$"traefik.http.routers.{serviceName}.middlewares"] + ","
                        : "";
                    labels[$"traefik.http.routers.{serviceName}.middlewares"] = $"{existingMiddlewares}{serviceName}-headers-http";
                }

                // Health check configuration - disabled by default to avoid false negatives
                // Applications can enable via custom Traefik overrides if needed
                // Uncomment the following lines to enable basic health checks:
                // labels[$"traefik.http.services.{serviceName}.loadbalancer.healthcheck.path"] = "/";
                // labels[$"traefik.http.services.{serviceName}.loadbalancer.healthcheck.interval"] = "30s";
                // labels[$"traefik.http.services.{serviceName}.loadbalancer.healthcheck.timeout"] = "5s";
            }
        }

        if (tcpDomains.Any())
        {
            foreach (var domain in tcpDomains)
            {
                var tcpRouterName = $"{serviceName}-tcp-{domain.Id}";
                var tcpServiceName = $"{serviceName}-tcp-{domain.Id}";
                var entrypoint = GetTcpEntrypointName(domain.Port);

                labels[$"traefik.tcp.routers.{tcpRouterName}.entrypoints"] = entrypoint;
                labels[$"traefik.tcp.routers.{tcpRouterName}.service"] = tcpServiceName;

                if (domain.HttpsEnabled)
                {
                    labels[$"traefik.tcp.routers.{tcpRouterName}.rule"] = $"HostSNI(`{domain.Host}`)";
                    labels[$"traefik.tcp.routers.{tcpRouterName}.tls.passthrough"] = "true";
                }
                else
                {
                    labels[$"traefik.tcp.routers.{tcpRouterName}.rule"] = "HostSNI(`*`)";
                }

                // TargetPort = container's internal listening port (e.g. 5432 for PostgreSQL).
                // Port = external entrypoint port (e.g. 5435 for a non-default exposure).
                var containerPort = domain.TargetPort ?? domain.Port;
                labels[$"traefik.tcp.services.{tcpServiceName}.loadbalancer.server.port"] = containerPort.ToString();
            }
        }

        if (includeApplicationOverrides)
        {
            ApplyStoredOverrides(application, labels);
        }

        return labels;
    }

    private static string GetTcpEntrypointName(int port)
    {
        if (port <= 0)
        {
            return "tcp";
        }

        return port switch
        {
            5432 => "postgres",
            5433 => "postgres-alt",
            5434 => "postgres-alt2",
            5435 => "postgres-alt3",
            3306 => "mysql",
            3307 => "mysql-alt",
            6379 => "redis",
            6380 => "redis-tls",
            27017 => "mongo",
            9000 => "clickhouse",
            8123 => "clickhouse-http",
            1433 => "mssql",
            _ => $"tcp-{port}"
        };
    }

    /// <summary>
    /// Build Traefik Host() rules from primary and additional domains.
    /// </summary>
    private static string BuildHostRules(string primaryDomain, string? additionalDomains)
    {
        var domains = new List<string> { primaryDomain.Trim() };

        if (!string.IsNullOrEmpty(additionalDomains))
        {
            var additional = additionalDomains
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => !string.IsNullOrEmpty(d));

            domains.AddRange(additional);
        }

        // Build Host rule: Host(`domain1.com`) || Host(`domain2.com`)
        if (domains.Count == 1)
        {
            return $"Host(`{domains[0]}`)";
        }

        return string.Join(" || ", domains.Select(d => $"Host(`{d}`)"));
    }

    private static readonly HashSet<string> ProtectedLabelKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "traefik.enable"
    };

    private static void ApplyStoredOverrides(Application application, Dictionary<string, string> labels)
    {
        if (!TryParseOverrides(application.TraefikLabelOverrides, out var overrides, out _, null))
        {
            return;
        }

        if (overrides.Count == 0)
        {
            return;
        }

        var merged = MergeWithOverrides(labels, overrides);
        labels.Clear();
        foreach (var kvp in merged)
        {
            labels[kvp.Key] = kvp.Value;
        }
    }

    public static Dictionary<string, string> MergeWithOverrides(IReadOnlyDictionary<string, string> baseLabels, IReadOnlyDictionary<string, string> overrides, List<string>? warnings = null)
    {
        var merged = new Dictionary<string, string>(baseLabels, StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in overrides)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key))
            {
                warnings?.Add("Skipped override with empty key.");
                continue;
            }

            if (ProtectedLabelKeys.Contains(kvp.Key))
            {
                warnings?.Add($"Override for '{kvp.Key}' is ignored to keep routing enabled.");
                continue;
            }

            merged[kvp.Key] = kvp.Value ?? string.Empty;
        }

        return merged;
    }

    public static bool TryParseOverrides(string? overridesJson, out Dictionary<string, string> overrides, out string? error, List<string>? warnings)
    {
        overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = null;

        if (string.IsNullOrWhiteSpace(overridesJson))
        {
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(overridesJson);

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Traefik overrides must be a JSON object of label keys and values.";
                return false;
            }

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var key = property.Name?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    warnings?.Add("Ignoring override with empty key.");
                    continue;
                }

                var value = ConvertScalar(property.Value);
                if (value is null)
                {
                    warnings?.Add($"Ignoring override '{property.Name}' because only scalar values are supported.");
                    continue;
                }

                overrides[key] = value;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }
    }

    private static string? ConvertScalar(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => null
        };
    }

    /// <summary>
    /// Sanitize a name for use in Traefik router/service names.
    /// </summary>
    private static string SanitizeName(string name)
    {
        // Replace spaces and special characters with hyphens
        var sanitized = name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-")
            .Replace(".", "-");

        // Remove any remaining non-alphanumeric characters except hyphens
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[^a-z0-9\-]", "");

        // Remove multiple consecutive hyphens
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"-+", "-");

        // Trim hyphens from start and end
        return sanitized.Trim('-');
    }

    /// <summary>
    /// Build labels for a middleware (e.g., basic auth, rate limiting).
    /// </summary>
    public static Dictionary<string, string> BuildBasicAuthLabels(string serviceName, string username, string passwordHash)
    {
        var sanitizedName = SanitizeName(serviceName);
        var middlewareName = $"{sanitizedName}-auth";

        return new Dictionary<string, string>
        {
            [$"traefik.http.middlewares.{middlewareName}.basicauth.users"] = $"{username}:{passwordHash}",
            [$"traefik.http.routers.{sanitizedName}-https.middlewares"] = middlewareName
        };
    }

    /// <summary>
    /// Build labels for rate limiting middleware.
    /// </summary>
    public static Dictionary<string, string> BuildRateLimitLabels(string serviceName, int average = 100, int burst = 50)
    {
        var sanitizedName = SanitizeName(serviceName);
        var middlewareName = $"{sanitizedName}-ratelimit";

        return new Dictionary<string, string>
        {
            [$"traefik.http.middlewares.{middlewareName}.ratelimit.average"] = average.ToString(),
            [$"traefik.http.middlewares.{middlewareName}.ratelimit.burst"] = burst.ToString()
        };
    }

    /// <summary>
    /// Build labels for IP whitelist middleware.
    /// </summary>
    public static Dictionary<string, string> BuildIpWhitelistLabels(string serviceName, IEnumerable<string> allowedIps)
    {
        var sanitizedName = SanitizeName(serviceName);
        var middlewareName = $"{sanitizedName}-ipwhitelist";

        return new Dictionary<string, string>
        {
            [$"traefik.http.middlewares.{middlewareName}.ipwhitelist.sourcerange"] = string.Join(",", allowedIps)
        };
    }
}
