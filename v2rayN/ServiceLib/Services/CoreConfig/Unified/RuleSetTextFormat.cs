namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Text form of the rule set list, one per line:
/// name = behavior, singbox=source, mihomo=source, interval=86400
/// </summary>
public static class RuleSetTextFormat
{
    public static List<RuleSetItem> Parse(string? text, List<string>? invalidLines)
    {
        var result = new List<RuleSetItem>();
        foreach (var line in UnifiedRuleParser.SplitLines(text))
        {
            var idx = line.IndexOf('=');
            var name = idx > 0 ? line[..idx].Trim() : string.Empty;
            var parts = idx > 0
                ? line[(idx + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [];
            if (name.IsNullOrEmpty() || parts.Length < 2 || !Global.ClashRuleProviderBehaviors.Contains(parts[0].ToLowerInvariant()))
            {
                invalidLines?.Add(line);
                continue;
            }
            var item = new RuleSetItem { Name = name, Behavior = parts[0].ToLowerInvariant() };
            foreach (var part in parts.Skip(1))
            {
                var optIdx = part.IndexOf('=');
                var key = optIdx > 0 ? part[..optIdx].Trim().ToLowerInvariant() : string.Empty;
                var value = optIdx > 0 ? part[(optIdx + 1)..].Trim() : string.Empty;
                switch (key)
                {
                    case "singbox" or "sing-box":
                        item.SingboxSource = value;
                        break;

                    case "mihomo" or "clash":
                        item.MihomoSource = value;
                        break;

                    case "interval":
                        item.UpdateInterval = value.ToInt(item.UpdateInterval);
                        break;
                }
            }
            if (item.SingboxSource.IsNullOrEmpty() && item.MihomoSource.IsNullOrEmpty())
            {
                invalidLines?.Add(line);
                continue;
            }
            result.Add(item);
        }
        return result;
    }

    public static string ToText(IEnumerable<RuleSetItem>? items)
    {
        var lines = (items ?? []).Select(item =>
        {
            var parts = new List<string> { item.Behavior };
            if (item.SingboxSource.IsNotEmpty())
            {
                parts.Add($"singbox={item.SingboxSource}");
            }
            if (item.MihomoSource.IsNotEmpty())
            {
                parts.Add($"mihomo={item.MihomoSource}");
            }
            parts.Add($"interval={item.UpdateInterval}");
            return $"{item.Name} = {string.Join(", ", parts)}";
        });
        return string.Join(Environment.NewLine, lines);
    }
}
