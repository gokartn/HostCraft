using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;

namespace HostCraft.Core.Models;

/// <summary>
/// Central place for database template hardening guidance and credential generation.
/// </summary>
public static class DatabaseTemplateBestPractices
{
    private const int DefaultSlugLength = 32;

    private static readonly IReadOnlyDictionary<DatabaseType, IReadOnlyList<TemplateEnvironmentVariableDefinition>> DefinitionMap
        = BuildDefinitionMap();

    public static IReadOnlyList<TemplateEnvironmentVariableDefinition> GetDefinitions(DatabaseType type)
    {
        if (!DefinitionMap.TryGetValue(type, out var definitions))
        {
            return Array.Empty<TemplateEnvironmentVariableDefinition>();
        }

        return definitions.Select(def => def.CreateCopy()).ToList();
    }

    public static ResolvedEnvironmentVariablesResult ResolveEnvironmentVariables(
        DatabaseTemplate template,
        string applicationName,
        IDictionary<string, string> source)
    {
        var effective = new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);
        var resolvedDefinitions = new List<ResolvedTemplateEnvironmentVariable>();
        var definitions = GetDefinitions(template.Type);

        foreach (var definition in definitions)
        {
            var hasValue = effective.TryGetValue(definition.Key, out var currentValue) &&
                           !string.IsNullOrWhiteSpace(currentValue);

            if (!hasValue)
            {
                var generated = GenerateValue(definition, applicationName, template.Name);
                effective[definition.Key] = generated;
                resolvedDefinitions.Add(ToResolved(definition, generated, false));
            }
            else
            {
                resolvedDefinitions.Add(ToResolved(definition, currentValue!.Trim(), true));
            }
        }

        return new ResolvedEnvironmentVariablesResult(effective, resolvedDefinitions);
    }

    public static string GetSuggestedValue(
        TemplateEnvironmentVariableDefinition definition,
        string applicationName,
        string templateName)
    {
        return GenerateValue(definition, applicationName, templateName, respectExisting: false);
    }

    private static ResolvedTemplateEnvironmentVariable ToResolved(
        TemplateEnvironmentVariableDefinition definition,
        string value,
        bool userProvided)
    {
        return new ResolvedTemplateEnvironmentVariable(
            definition.Key,
            definition.Label,
            value,
            definition.IsSecret,
            userProvided,
            definition.Description,
            definition.DisplayInWizard);
    }

    private static string GenerateValue(
        TemplateEnvironmentVariableDefinition definition,
        string applicationName,
        string templateName,
        bool respectExisting = true)
    {
        return definition.Strategy switch
        {
            TemplateEnvironmentValueStrategy.ApplicationSlug =>
                BuildSlug(applicationName, templateName, definition),
            TemplateEnvironmentValueStrategy.RandomSecure =>
                GenerateSecret(definition.Length ?? 32, includeSymbols: false),
            TemplateEnvironmentValueStrategy.RandomSecureWithSymbols =>
                GenerateSecret(definition.Length ?? 48, includeSymbols: true),
            TemplateEnvironmentValueStrategy.Literal =>
                definition.DefaultValue ?? string.Empty,
            _ => respectExisting && !string.IsNullOrWhiteSpace(definition.DefaultValue)
                ? definition.DefaultValue!
                : BuildSlug(applicationName, templateName, definition)
        };
    }

    private static string BuildSlug(
        string applicationName,
        string templateName,
        TemplateEnvironmentVariableDefinition definition)
    {
        var seed = string.IsNullOrWhiteSpace(applicationName) ? templateName : applicationName;
        var slug = NormalizeSlug(seed, definition.Length ?? DefaultSlugLength);

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(definition.Prefix))
        {
            builder.Append(definition.Prefix);
        }

        builder.Append(slug);

        if (!string.IsNullOrWhiteSpace(definition.Suffix))
        {
            builder.Append(definition.Suffix);
        }

        return builder.ToString();
    }

    private static string NormalizeSlug(string value, int maxLength)
    {
        if (maxLength <= 0)
        {
            maxLength = DefaultSlugLength;
        }

        Span<char> buffer = stackalloc char[value.Length];
        var index = 0;
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[index++] = ch;
            }
            else if (index == 0 || buffer[index - 1] != '-')
            {
                buffer[index++] = '-';
            }
        }

        var slug = new string(buffer[..index]).Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "hostcraft-db";
        }

        if (slug.Length > maxLength)
        {
            slug = slug[..maxLength];
        }

        return slug;
    }

    private static string GenerateSecret(int length, bool includeSymbols)
    {
        if (length <= 0)
        {
            length = 32;
        }

        const string baseSet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        const string symbolSet = "!@#$%^&*()-_=+[]{}:?";
        var charset = includeSymbols ? baseSet + symbolSet : baseSet;
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[length];

        for (int i = 0; i < length; i++)
        {
            chars[i] = charset[bytes[i] % charset.Length];
        }

        return new string(chars);
    }

    private static IReadOnlyDictionary<DatabaseType, IReadOnlyList<TemplateEnvironmentVariableDefinition>> BuildDefinitionMap()
    {
        return new Dictionary<DatabaseType, IReadOnlyList<TemplateEnvironmentVariableDefinition>>
        {
            [DatabaseType.PostgreSQL] = new List<TemplateEnvironmentVariableDefinition>
            {
                Def("POSTGRES_USER", "Superuser", "Primary PostgreSQL superuser for administration.", false, true, TemplateEnvironmentValueStrategy.ApplicationSlug, length: 32),
                Def("POSTGRES_PASSWORD", "Superuser Password", "Strong password for the PostgreSQL superuser account.", true, true, TemplateEnvironmentValueStrategy.RandomSecureWithSymbols, length: 40),
                Def("POSTGRES_DB", "Default Database", "Database created during initialization for your application.", false, true, TemplateEnvironmentValueStrategy.ApplicationSlug, length: 48),
                Def("POSTGRES_INITDB_ARGS", "Init Arguments", "Enable checksums and UTF-8 locale for durability and compatibility.", false, true, TemplateEnvironmentValueStrategy.Literal, defaultValue: "--data-checksums --encoding=UTF8 --locale=en_US.UTF-8", displayInWizard: false),
                Def("PGDATA", "Data Directory", "Stores WAL and heap files with checksums enabled.", false, true, TemplateEnvironmentValueStrategy.Literal, defaultValue: "/var/lib/postgresql/data/pgdata", displayInWizard: false)
            },
            [DatabaseType.MySQL] = new List<TemplateEnvironmentVariableDefinition>
            {
                Def("MYSQL_ROOT_PASSWORD", "Root Password", "Mandatory password for the MySQL root account.", true, true, TemplateEnvironmentValueStrategy.RandomSecureWithSymbols, length: 40),
                Def("MYSQL_DATABASE", "Application Database", "Database schema created during initialization.", false, true, TemplateEnvironmentValueStrategy.ApplicationSlug, length: 48),
                Def("MYSQL_USER", "App User", "Least-privileged application user.", false, true, TemplateEnvironmentValueStrategy.ApplicationSlug, length: 32, suffix: "_app"),
                Def("MYSQL_PASSWORD", "App User Password", "Password for the application user.", true, true, TemplateEnvironmentValueStrategy.RandomSecure, length: 36),
                Def("MYSQL_DEFAULT_AUTH", "Auth Plugin", "Use caching_sha2_password for modern auth.", false, true, TemplateEnvironmentValueStrategy.Literal, defaultValue: "caching_sha2_password", displayInWizard: false)
            },
            [DatabaseType.MongoDB] = new List<TemplateEnvironmentVariableDefinition>
            {
                Def("MONGO_INITDB_ROOT_USERNAME", "Root Username", "Administrative MongoDB user.", false, true, TemplateEnvironmentValueStrategy.ApplicationSlug, length: 32, suffix: "-root"),
                Def("MONGO_INITDB_ROOT_PASSWORD", "Root Password", "Password for the MongoDB admin user.", true, true, TemplateEnvironmentValueStrategy.RandomSecureWithSymbols, length: 36),
                Def("MONGO_INITDB_DATABASE", "Primary Database", "Default database created for your workloads.", false, true, TemplateEnvironmentValueStrategy.ApplicationSlug, length: 48)
            },
            [DatabaseType.Redis] = new List<TemplateEnvironmentVariableDefinition>
            {
                Def("REDIS_PASSWORD", "Redis Password", "Enforces ACL-authenticated connections.", true, true, TemplateEnvironmentValueStrategy.RandomSecure, length: 32),
                Def("REDIS_USER", "Redis User", "Logical ACL user name.", false, true, TemplateEnvironmentValueStrategy.ApplicationSlug, length: 32, suffix: "-client"),
                Def("REDIS_AOF_ENABLED", "Append Only", "Enable AOF persistence for durability.", false, true, TemplateEnvironmentValueStrategy.Literal, defaultValue: "yes", displayInWizard: false)
            },
            [DatabaseType.MariaDB] = new List<TemplateEnvironmentVariableDefinition>
            {
                Def("MARIADB_ROOT_PASSWORD", "Root Password", "Mandatory password for the MariaDB root account.", true, true, TemplateEnvironmentValueStrategy.RandomSecureWithSymbols, length: 38),
                Def("MARIADB_DATABASE", "Application Database", "Database schema created for your workload.", false, true, TemplateEnvironmentValueStrategy.ApplicationSlug, length: 48),
                Def("MARIADB_USER", "App User", "Least privileged MariaDB user.", false, true, TemplateEnvironmentValueStrategy.ApplicationSlug, length: 32, suffix: "_app"),
                Def("MARIADB_PASSWORD", "App User Password", "Password for the application user.", true, true, TemplateEnvironmentValueStrategy.RandomSecure, length: 34),
                Def("MARIADB_AUTO_UPGRADE", "Auto Upgrade", "Always apply MariaDB security upgrades on boot.", false, true, TemplateEnvironmentValueStrategy.Literal, defaultValue: "1", displayInWizard: false)
            },
            [DatabaseType.Clickhouse] = new List<TemplateEnvironmentVariableDefinition>
            {
                Def("CLICKHOUSE_USER", "Admin User", "ClickHouse user used for API access.", false, true, TemplateEnvironmentValueStrategy.ApplicationSlug, length: 32, suffix: "_admin"),
                Def("CLICKHOUSE_PASSWORD", "Admin Password", "Password for the ClickHouse admin user.", true, true, TemplateEnvironmentValueStrategy.RandomSecureWithSymbols, length: 40),
                Def("CLICKHOUSE_DB", "Default Database", "Primary ClickHouse database.", false, true, TemplateEnvironmentValueStrategy.ApplicationSlug, length: 48),
                Def("CLICKHOUSE_DEFAULT_ACCESS_MANAGEMENT", "Enable RBAC", "Turn on RBAC and password auth.", false, true, TemplateEnvironmentValueStrategy.Literal, defaultValue: "1", displayInWizard: false)
            },
            [DatabaseType.DragonFly] = new List<TemplateEnvironmentVariableDefinition>
            {
                Def("DRAGONFLY_PASSWORD", "Dragonfly Password", "Password protecting Dragonfly connections.", true, true, TemplateEnvironmentValueStrategy.RandomSecure, length: 32),
                Def("DRAGONFLY_PROTECTED_MODE", "Protected Mode", "Restricts access to authenticated clients.", false, true, TemplateEnvironmentValueStrategy.Literal, defaultValue: "true", displayInWizard: false)
            },
            [DatabaseType.KeyDB] = new List<TemplateEnvironmentVariableDefinition>
            {
                Def("KEYDB_PASSWORD", "KeyDB Password", "Password required for KeyDB clients.", true, true, TemplateEnvironmentValueStrategy.RandomSecure, length: 32),
                Def("KEYDB_APPENDONLY", "Append Only", "Enable append-only persistence.", false, true, TemplateEnvironmentValueStrategy.Literal, defaultValue: "yes", displayInWizard: false),
                Def("KEYDB_MAXMEMORY_POLICY", "Eviction Policy", "Prefer LRU eviction for caches.", false, true, TemplateEnvironmentValueStrategy.Literal, defaultValue: "allkeys-lru", displayInWizard: false)
            }
        };
    }

    private static TemplateEnvironmentVariableDefinition Def(
        string key,
        string label,
        string description,
        bool isSecret,
        bool isRequired,
        TemplateEnvironmentValueStrategy strategy,
        string? defaultValue = null,
        int? length = null,
        string? prefix = null,
        string? suffix = null,
        bool displayInWizard = true)
    {
        return new TemplateEnvironmentVariableDefinition
        {
            Key = key,
            Label = label,
            Description = description,
            IsSecret = isSecret,
            IsRequired = isRequired,
            Strategy = strategy,
            DefaultValue = defaultValue,
            Length = length,
            Prefix = prefix,
            Suffix = suffix,
            DisplayInWizard = displayInWizard
        };
    }
}
