namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Parsers for the Clash text syntax used by the mihomo extension settings:
/// proxy groups (name = type, member1, key=value), rule providers and Clash rule lines.
/// </summary>
public static class ClashSyntax
{
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

    #region Proxy groups and rule providers

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
        foreach (var line in UnifiedRuleParser.SplitLines(text))
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
        foreach (var line in UnifiedRuleParser.SplitLines(text))
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

    #endregion Proxy groups and rule providers

    #region Text helpers

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

    #endregion Text helpers
}
