namespace ServiceLib.Services.CoreConfig;

public partial class CoreConfigSingboxService
{
    private void ConvertGeo2Ruleset()
    {
        static void AddRuleSets(List<string> ruleSets, List<string>? rule_set)
        {
            if (rule_set != null)
            {
                ruleSets.AddRange(rule_set);
            }
        }
        var geosite = "geosite";
        var geoip = "geoip";
        var ruleSets = new List<string>();

        //convert route geosite & geoip to ruleset
        foreach (var rule in _coreConfig.route.rules.Where(t => t.geosite?.Count > 0).ToList() ?? [])
        {
            rule.rule_set ??= [];
            rule.rule_set.AddRange(rule?.geosite?.Select(t => $"{geosite}-{t}").ToList() ?? []);
            rule.geosite = null;
            AddRuleSets(ruleSets, rule.rule_set);
        }
        foreach (var rule in _coreConfig.route.rules.Where(t => t.geoip?.Count > 0).ToList() ?? [])
        {
            rule.rule_set ??= [];
            rule.rule_set.AddRange(rule?.geoip?.Select(t => $"{geoip}-{t}").ToList() ?? []);
            rule.geoip = null;
            AddRuleSets(ruleSets, rule.rule_set);
        }

        // rule sets referenced directly (ruleset:name), also inside logical rules
        foreach (var rule in Flatten(_coreConfig.route.rules))
        {
            AddRuleSets(ruleSets, rule.rule_set);
        }

        //convert dns geosite & geoip to ruleset
        foreach (var rule in _coreConfig.dns?.rules.Where(t => t.geosite?.Count > 0).ToList() ?? [])
        {
            rule.rule_set ??= [];
            rule.rule_set.AddRange(rule?.geosite?.Select(t => $"{geosite}-{t}").ToList() ?? []);
            rule.geosite = null;
        }
        foreach (var rule in _coreConfig.dns?.rules.Where(t => t.geoip?.Count > 0).ToList() ?? [])
        {
            rule.rule_set ??= [];
            rule.rule_set.AddRange(rule?.geoip?.Select(t => $"{geoip}-{t}").ToList() ?? []);
            rule.geoip = null;
        }
        foreach (var dnsRule in _coreConfig.dns?.rules.Where(t => t.rule_set?.Count > 0).ToList() ?? [])
        {
            AddRuleSets(ruleSets, dnsRule.rule_set);
        }
        //rules in rules
        foreach (var item in _coreConfig.dns?.rules.Where(t => t.rules?.Count > 0).Select(t => t.rules).ToList() ?? [])
        {
            foreach (var item2 in item ?? [])
            {
                AddRuleSets(ruleSets, item2.rule_set);
            }
        }

        //load custom ruleset file
        List<Ruleset4Sbox> customRulesets = [];

        var routing = context.RoutingItem;
        if (routing.CustomRulesetPath4Singbox.IsNotEmpty())
        {
            var result = EmbedUtils.LoadResource(routing.CustomRulesetPath4Singbox);
            if (result.IsNotEmpty())
            {
                customRulesets = (JsonUtils.Deserialize<List<Ruleset4Sbox>>(result) ?? [])
                    .Where(t => t.tag != null)
                    .Where(t => t.type != null)
                    .Where(t => t.format != null)
                    .ToList();
            }
        }

        //Local srs files address
        var localSrss = Utils.GetBinPath("srss");

        //Add ruleset srs
        _coreConfig.route.rule_set = [];
        var containRemoteRuleset = false;
        foreach (var item in new HashSet<string>(ruleSets))
        {
            if (item.IsNullOrEmpty())
            { continue; }
            var customRuleset = customRulesets.FirstOrDefault(t => t.tag != null && t.tag.Equals(item));
            if (customRuleset is null
                && ToSingboxRuleset(_config.RuleSetItems?.FirstOrDefault(t => t.Name == item && t.SingboxSource.IsNotEmpty())) is { } declared)
            {
                // Declared rule sets download like the built-in ones; custom rule set files keep their own settings.
                if (declared.type == "remote")
                {
                    containRemoteRuleset = true;
                    declared.http_client = Global.SingboxSrsDownloadHttpClientTag;
                }
                customRuleset = declared;
            }
            if (customRuleset is null)
            {
                var pathSrs = Path.Combine(localSrss, $"{item}.srs");
                if (File.Exists(pathSrs))
                {
                    customRuleset = new()
                    {
                        type = "local",
                        format = "binary",
                        tag = item,
                        path = pathSrs,
                    };
                }
                else
                {
                    containRemoteRuleset = true;

                    var srsUrl = string.IsNullOrEmpty(_config.ConstItem.SrsSourceUrl)
                        ? Global.SingboxRulesetUrl
                        : _config.ConstItem.SrsSourceUrl;

                    customRuleset = new()
                    {
                        type = "remote",
                        format = "binary",
                        tag = item,
                        url = string.Format(srsUrl, item.StartsWith(geosite) ? geosite : geoip, item),
                        http_client = Global.SingboxSrsDownloadHttpClientTag,
                    };
                }
            }
            _coreConfig.route.rule_set.Add(customRuleset);
        }

        if (containRemoteRuleset)
        {
            _coreConfig.http_clients ??= [];
            _coreConfig.http_clients.Add(new()
            {
                tag = Global.SingboxSrsDownloadHttpClientTag,
                detour = Global.ProxyTag,
            });
        }
    }

    private static IEnumerable<Rule4Sbox> Flatten(IEnumerable<Rule4Sbox>? rules)
    {
        foreach (var rule in rules ?? [])
        {
            yield return rule;
            foreach (var inner in Flatten(rule.rules))
            {
                yield return inner;
            }
        }
    }

    /// <summary>
    /// Converts a rule set declared in the settings (referenced as ruleset:name) to a sing-box rule set.
    /// </summary>
    private static Ruleset4Sbox? ToSingboxRuleset(RuleSetItem? item)
    {
        if (item?.SingboxSource is not { Length: > 0 } source)
        {
            return null;
        }
        var format = source.Split('?', '#')[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "source" : "binary";
        var isRemote = source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        return new Ruleset4Sbox
        {
            tag = item.Name,
            type = isRemote ? "remote" : "local",
            format = format,
            url = isRemote ? source : null,
            path = isRemote ? null : source,
            update_interval = isRemote ? $"{Math.Max(1, item.UpdateInterval / 60)}m" : null,
        };
    }
}
