namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Applies OpenClash-like structured overrides (DNS override and policy override)
/// to a parsed Clash (mihomo) configuration. The source file is never modified.
/// </summary>
public static class ClashOverrideService
{
    private static readonly string _tag = "ClashOverrideService";
    private const string DefaultHealthCheckUrl = "https://www.gstatic.com/generate_204";
    private const int DefaultHealthCheckInterval = 300;
    private const int DefaultRuleProviderInterval = 86400;

    private static readonly HashSet<string> _groupIntOptions =
        ["interval", "tolerance", "timeout", "max-failed-times", "routing-mark"];

    private static readonly HashSet<string> _groupBoolOptions =
        ["lazy", "include-all", "include-all-proxies", "include-all-providers", "hidden", "disable-udp"];

    private static readonly HashSet<string> _groupStringOptions =
        ["url", "strategy", "filter", "exclude-filter", "exclude-type", "icon", "expected-status", "interface-name"];

    private static readonly HashSet<string> _groupMemberSources =
        ["include-all", "include-all-proxies", "include-all-providers", "use"];

    public static void Apply(Dictionary<string, object> fileContent, ClashOverrideItem? item, bool enableIPv6)
    {
        if (item == null)
        {
            return;
        }
        if (item.EnableDnsOverride)
        {
            ApplyDns(fileContent, item, enableIPv6);
        }
        if (item.EnablePolicyOverride)
        {
            ApplyPolicy(fileContent, item);
        }
    }

    /// <summary>
    /// Returns the lines that cannot be parsed, grouped by section, so the UI can report them.
    /// </summary>
    public static ClashOverrideValidationResult Validate(ClashOverrideItem item)
    {
        var result = new ClashOverrideValidationResult();
        if (item.EnableDnsOverride)
        {
            result.NameserverMissing = SplitList(item.Nameserver).Count == 0;
            ParseNameserverPolicy(item.NameserverPolicy, result.NameserverPolicy);
        }
        if (item.EnablePolicyOverride)
        {
            ParseProxyGroups(item.ProxyGroups, result.ProxyGroups);
            ParseRuleProviders(item.RuleProviders, result.RuleProviders);
            foreach (var rule in SplitLines(item.Rules))
            {
                if (SplitTopLevel(rule).Count < 2)
                {
                    result.Rules.Add(rule);
                }
            }
        }
        return result;
    }

    #region DNS

    private static void ApplyDns(Dictionary<string, object> fileContent, ClashOverrideItem item, bool enableIPv6)
    {
        var nameserver = SplitList(item.Nameserver);
        if (nameserver.Count == 0)
        {
            Logging.SaveLog($"{_tag}: DNS override skipped, nameserver is empty");
            return;
        }

        var dns = new Dictionary<object, object>
        {
            ["enable"] = true,
        };
        if (fileContent.GetValueOrDefault("dns") is Dictionary<object, object> oldDns
            && oldDns.GetValueOrDefault("listen") is { } listen)
        {
            dns["listen"] = listen;
        }
        dns["ipv6"] = enableIPv6;

        var mode = item.EnhancedMode.IsNullOrEmpty() ? Global.ClashEnhancedModes.First() : item.EnhancedMode;
        if (mode != "normal")
        {
            dns["enhanced-mode"] = mode;
        }
        if (mode == "fake-ip")
        {
            dns["fake-ip-range"] = item.FakeIPRange.IsNullOrEmpty() ? Global.FakeIPRanges.First() : item.FakeIPRange.Trim();
            AddList(dns, "fake-ip-filter", SplitList(item.FakeIPFilter));
        }

        AddList(dns, "default-nameserver", SplitList(item.DefaultNameserver));
        dns["nameserver"] = nameserver;

        var fallback = SplitList(item.Fallback);
        AddList(dns, "fallback", fallback);
        if (fallback.Count > 0 && item.FallbackFilterGeoIP)
        {
            dns["fallback-filter"] = new Dictionary<object, object>
            {
                ["geoip"] = true,
                ["geoip-code"] = item.FallbackFilterGeoIPCode.IsNullOrEmpty() ? "CN" : item.FallbackFilterGeoIPCode.Trim(),
            };
        }

        var proxyServerNameserver = SplitList(item.ProxyServerNameserver);
        if (item.RespectRules)
        {
            dns["respect-rules"] = true;
            // mihomo requires proxy-server-nameserver when respect-rules is enabled
            if (proxyServerNameserver.Count == 0)
            {
                proxyServerNameserver = [.. nameserver];
            }
        }
        AddList(dns, "proxy-server-nameserver", proxyServerNameserver);
        AddList(dns, "direct-nameserver", SplitList(item.DirectNameserver));

        var policy = ParseNameserverPolicy(item.NameserverPolicy, null);
        if (policy.Count > 0)
        {
            dns["nameserver-policy"] = policy;
        }

        fileContent["dns"] = dns;
    }

    public static Dictionary<object, object> ParseNameserverPolicy(string? text, List<string>? invalidLines)
    {
        var policy = new Dictionary<object, object>();
        foreach (var line in SplitLines(text))
        {
            var idx = line.IndexOf('=');
            var key = idx > 0 ? line[..idx].Trim() : string.Empty;
            var servers = idx > 0 ? SplitList(line[(idx + 1)..]) : [];
            if (key.IsNullOrEmpty() || servers.Count == 0)
            {
                invalidLines?.Add(line);
                continue;
            }
            policy[key] = servers.Count == 1 ? servers[0] : servers;
        }
        return policy;
    }

    #endregion DNS

    #region Policy

    private static void ApplyPolicy(Dictionary<string, object> fileContent, ClashOverrideItem item)
    {
        var customGroups = ParseProxyGroups(item.ProxyGroups, null);
        if (customGroups.Count > 0)
        {
            var groups = fileContent.GetValueOrDefault("proxy-groups") as List<object> ?? [];
            foreach (var group in customGroups)
            {
                var index = groups.FindIndex(t => t is Dictionary<object, object> g && Equals(g.GetValueOrDefault("name"), group["name"]));
                if (index >= 0)
                {
                    groups[index] = group;
                }
                else
                {
                    groups.Add(group);
                }
            }
            fileContent["proxy-groups"] = groups;
        }

        var knownPolicies = GetKnownPolicies(fileContent);
        foreach (var group in customGroups)
        {
            if (group.GetValueOrDefault("proxies") is not List<object> members)
            {
                continue;
            }
            var removed = members.RemoveAll(t => !knownPolicies.Contains(t.ToString() ?? string.Empty));
            if (removed > 0)
            {
                Logging.SaveLog($"{_tag}: removed {removed} unknown member(s) from proxy group {group["name"]}");
            }
            if (members.Count == 0)
            {
                if (_groupMemberSources.Any(group.ContainsKey))
                {
                    group.Remove("proxies");
                }
                else
                {
                    members.Add("DIRECT");
                }
            }
        }

        var providers = ParseRuleProviders(item.RuleProviders, null);
        if (providers.Count > 0)
        {
            var oldProviders = fileContent.GetValueOrDefault("rule-providers") as Dictionary<object, object> ?? [];
            foreach (var provider in providers)
            {
                oldProviders[provider.Key] = provider.Value;
            }
            fileContent["rule-providers"] = oldProviders;
        }

        var customRules = new List<object>();
        foreach (var rule in SplitLines(item.Rules))
        {
            var target = GetRulePolicy(rule);
            if (target != null && !knownPolicies.Contains(target))
            {
                Logging.SaveLog($"{_tag}: skipped rule with unknown policy: {rule}");
                continue;
            }
            customRules.Add(rule);
        }
        if (customRules.Count == 0 && !item.ReplaceRules)
        {
            return;
        }
        if (!item.ReplaceRules && fileContent.GetValueOrDefault("rules") is List<object> oldRules)
        {
            customRules.AddRange(oldRules);
        }
        fileContent["rules"] = customRules;
    }

    private static HashSet<string> GetKnownPolicies(Dictionary<string, object> fileContent)
    {
        var names = new HashSet<string>(Global.ClashBuiltinPolicies);
        foreach (var key in new[] { "proxies", "proxy-groups" })
        {
            if (fileContent.GetValueOrDefault(key) is not List<object> list)
            {
                continue;
            }
            foreach (var entry in list.OfType<Dictionary<object, object>>())
            {
                if (entry.GetValueOrDefault("name")?.ToString() is { Length: > 0 } name)
                {
                    names.Add(name);
                }
            }
        }
        return names;
    }

    /// <summary>
    /// Returns the policy a rule points to, or null when the rule does not target a policy (e.g. SUB-RULE).
    /// </summary>
    public static string? GetRulePolicy(string rule)
    {
        var parts = SplitTopLevel(rule);
        if (parts.Count < 2)
        {
            return null;
        }
        var type = parts[0].ToUpperInvariant();
        if (type == "SUB-RULE")
        {
            return null;
        }
        if (type == "MATCH")
        {
            return parts[1];
        }
        return parts.Count >= 3 ? parts[2] : null;
    }

    /// <summary>
    /// Parses lines like: name = type, member1, member2, url=..., interval=300, filter="(?i)hk|sg"
    /// </summary>
    public static List<Dictionary<object, object>> ParseProxyGroups(string? text, List<string>? invalidLines)
    {
        var groups = new List<Dictionary<object, object>>();
        foreach (var line in SplitLines(text))
        {
            var idx = line.IndexOf('=');
            var name = idx > 0 ? Unquote(line[..idx]) : string.Empty;
            var parts = idx > 0 ? SplitTopLevel(line[(idx + 1)..]) : [];
            var type = parts.Count > 0 ? parts[0].ToLowerInvariant() : string.Empty;
            if (name.IsNullOrEmpty() || !Global.ClashProxyGroupTypes.Contains(type))
            {
                invalidLines?.Add(line);
                continue;
            }

            var group = new Dictionary<object, object>
            {
                ["name"] = name,
                ["type"] = type,
            };
            var members = new List<object>();
            foreach (var part in parts.Skip(1))
            {
                if (TryParseOption(part, out var key, out var value))
                {
                    group[key] = value;
                }
                else
                {
                    members.Add(Unquote(part));
                }
            }
            if (members.Count > 0)
            {
                group["proxies"] = members;
            }
            if (members.Count == 0 && !_groupMemberSources.Any(group.ContainsKey))
            {
                invalidLines?.Add(line);
                continue;
            }
            if (type is "url-test" or "fallback" or "load-balance")
            {
                group.TryAdd("url", DefaultHealthCheckUrl);
                group.TryAdd("interval", DefaultHealthCheckInterval);
            }
            groups.Add(group);
        }
        return groups;
    }

    private static bool TryParseOption(string part, out string key, out object value)
    {
        key = string.Empty;
        value = string.Empty;
        var idx = part.IndexOf('=');
        if (idx <= 0)
        {
            return false;
        }
        key = part[..idx].Trim().ToLowerInvariant();
        var raw = Unquote(part[(idx + 1)..]);
        if (_groupIntOptions.Contains(key) && int.TryParse(raw, out var number))
        {
            value = number;
        }
        else if (_groupBoolOptions.Contains(key) && bool.TryParse(raw, out var flag))
        {
            value = flag;
        }
        else if (key == "use")
        {
            value = raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Cast<object>().ToList();
        }
        else if (_groupStringOptions.Contains(key))
        {
            value = raw;
        }
        else
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Parses lines like: name = behavior, url-or-path, format=yaml, interval=86400, proxy=DIRECT
    /// </summary>
    public static Dictionary<object, object> ParseRuleProviders(string? text, List<string>? invalidLines)
    {
        var providers = new Dictionary<object, object>();
        foreach (var line in SplitLines(text))
        {
            var idx = line.IndexOf('=');
            var name = idx > 0 ? Unquote(line[..idx]) : string.Empty;
            var parts = idx > 0 ? SplitTopLevel(line[(idx + 1)..]) : [];
            if (name.IsNullOrEmpty()
                || parts.Count < 2
                || !Global.ClashRuleProviderBehaviors.Contains(parts[0].ToLowerInvariant()))
            {
                invalidLines?.Add(line);
                continue;
            }

            var source = Unquote(parts[1]);
            var options = new Dictionary<string, string>();
            foreach (var part in parts.Skip(2))
            {
                var optIdx = part.IndexOf('=');
                if (optIdx > 0)
                {
                    options[part[..optIdx].Trim().ToLowerInvariant()] = Unquote(part[(optIdx + 1)..]);
                }
            }

            var format = options.GetValueOrDefault("format") ?? InferRuleProviderFormat(source);
            var provider = new Dictionary<object, object>
            {
                ["behavior"] = parts[0].ToLowerInvariant(),
                ["format"] = format,
            };
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                provider["type"] = "http";
                provider["url"] = source;
                provider["path"] = $"./ruleset/{GetSafeFileName(name)}.{(format == "text" ? "txt" : format)}";
                provider["interval"] = options.GetValueOrDefault("interval").ToInt(DefaultRuleProviderInterval);
                if (options.GetValueOrDefault("proxy") is { Length: > 0 } proxy)
                {
                    provider["proxy"] = proxy;
                }
            }
            else
            {
                provider["type"] = "file";
                provider["path"] = source;
            }
            providers[name] = provider;
        }
        return providers;
    }

    private static string InferRuleProviderFormat(string source)
    {
        var path = source.Split('?', '#')[0];
        if (path.EndsWith(".mrs", StringComparison.OrdinalIgnoreCase))
        {
            return "mrs";
        }
        if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".list", StringComparison.OrdinalIgnoreCase))
        {
            return "text";
        }
        return "yaml";
    }

    private static string GetSafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string([.. name.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : c)]);
        return safe.IsNullOrEmpty() ? "provider" : safe;
    }

    #endregion Policy

    #region Text helpers

    public static List<string> SplitLines(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return [];
        }
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0 && !t.StartsWith('#'))
            .ToList();
    }

    /// <summary>
    /// Splits by newlines and commas, for plain lists such as DNS servers.
    /// </summary>
    public static List<string> SplitList(string? text)
    {
        return SplitLines(text)
            .SelectMany(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
    }

    /// <summary>
    /// Splits by commas that are outside double quotes and parentheses.
    /// </summary>
    public static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        var depth = 0;
        var inQuotes = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && c == '(')
            {
                depth++;
            }
            else if (!inQuotes && c == ')' && depth > 0)
            {
                depth--;
            }
            else if (!inQuotes && depth == 0 && c == ',')
            {
                parts.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }
            sb.Append(c);
        }
        parts.Add(sb.ToString().Trim());
        return parts.Where(t => t.Length > 0).ToList();
    }

    private static string Unquote(string text)
    {
        text = text.Trim();
        return text.Length >= 2 && text.StartsWith('"') && text.EndsWith('"') ? text[1..^1] : text;
    }

    private static void AddList(Dictionary<object, object> dict, string key, List<string> values)
    {
        if (values.Count > 0)
        {
            dict[key] = values;
        }
    }

    #endregion Text helpers
}

public class ClashOverrideValidationResult
{
    public bool NameserverMissing { get; set; }
    public List<string> NameserverPolicy { get; } = [];
    public List<string> ProxyGroups { get; } = [];
    public List<string> RuleProviders { get; } = [];
    public List<string> Rules { get; } = [];

    public bool IsValid => !NameserverMissing
        && NameserverPolicy.Count == 0
        && ProxyGroups.Count == 0
        && RuleProviders.Count == 0
        && Rules.Count == 0;
}
