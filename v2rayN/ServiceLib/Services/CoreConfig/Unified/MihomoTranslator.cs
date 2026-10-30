namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Translates the validated <see cref="UnifiedPlan" /> into a Clash (mihomo) configuration.
/// The proxies and proxy groups come from the user's configuration file; the shared DNS settings,
/// routing rules, policy groups and rule sets are applied on top according to <see cref="EMihomoUnifiedMode" />.
/// </summary>
public class MihomoTranslator(
    UnifiedPlan plan,
    MihomoExtension ext,
    bool enableIPv6,
    UnifiedDiagnostics diagnostics)
{
    private const ECoreType Core = ECoreType.mihomo;
    private const string DefaultHealthCheckUrl = "https://www.gstatic.com/generate_204";

    private static readonly string[] _privateCidrs =
    [
        "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12",
        "192.168.0.0/16", "224.0.0.0/4", "240.0.0.0/4", "::1/128", "fc00::/7", "fe80::/10",
    ];

    // Equivalents of the keyword / regex entries of the built-in fake-ip filter, which mihomo cannot express.
    private static readonly string[] _fakeIpFilterPatterns =
    [
        "*", "+.lan", "+.local", "+.localdomain", "localhost.*.weixin.qq.com", "+.xboxlive.com",
        "time.*.com", "time.*.gov", "time.*.edu.cn", "time.*.apple.com", "ntp.*.com", "+.ntp.org",
        "+.stun.*.*", "+.stun.*.*.*", "+.stun.*.*.*.*", "+.stun.*.*.*.*.*",
    ];

    public void Apply(Dictionary<string, object> fileContent)
    {
        if (ext.UnifiedMode == EMihomoUnifiedMode.Off)
        {
            return;
        }
        fileContent["dns"] = BuildDns(fileContent);
        var hosts = BuildHosts();
        if (hosts.Count > 0)
        {
            fileContent["hosts"] = hosts;
        }
        if (plan.Dns.UseSystemHosts || hosts.Count > 0)
        {
            fileContent["use-system-hosts"] = plan.Dns.UseSystemHosts;
        }

        if (ext.UnifiedMode is EMihomoUnifiedMode.DnsAndRules or EMihomoUnifiedMode.DnsAndReplaceRules)
        {
            ApplyPolicy(fileContent);
        }
    }

    #region DNS

    private Dictionary<object, object> BuildDns(Dictionary<string, object> fileContent)
    {
        var dnsPlan = plan.Dns;
        var dns = new Dictionary<object, object> { ["enable"] = true };
        if (fileContent.GetValueOrDefault("dns") is Dictionary<object, object> oldDns
            && oldDns.GetValueOrDefault("listen") is { } listen)
        {
            dns["listen"] = listen;
        }
        dns["ipv6"] = enableIPv6 && !dnsPlan.BlockAAAA;
        if (ext.PreferH3)
        {
            dns["prefer-h3"] = true;
        }

        if (dnsPlan.FakeIp)
        {
            dns["enhanced-mode"] = "fake-ip";
            dns["fake-ip-range"] = dnsPlan.FakeIpRange;
            if (dnsPlan.GlobalFakeIp)
            {
                dns["fake-ip-filter"] = BuildFakeIpFilter();
            }
            else
            {
                // Only domains resolved by the remote DNS get fake IPs.
                dns["fake-ip-filter-mode"] = "whitelist";
                dns["fake-ip-filter"] = dnsPlan.Policies
                    .Where(p => p.Target == EDnsPolicyTarget.Remote && p.Servers == null)
                    .SelectMany(p => p.Domains).Select(ToDnsPattern).OfType<string>().Distinct().ToList();
            }
        }
        else
        {
            dns["enhanced-mode"] = "redir-host";
        }

        var direct = NormalizeServers(dnsPlan.DirectServers);
        var remote = NormalizeServers(dnsPlan.RemoteServers);
        var bootstrap = NormalizeServers(dnsPlan.BootstrapServers);
        if (bootstrap.Count > 0)
        {
            dns["default-nameserver"] = bootstrap;
        }
        dns["nameserver"] = dnsPlan.FinalDirect ? direct : remote;
        dns["proxy-server-nameserver"] = direct;
        dns["direct-nameserver"] = direct;
        if (ext.RespectRules && !dnsPlan.FinalDirect)
        {
            dns["respect-rules"] = true;
        }

        var policy = new Dictionary<object, object>();
        foreach (var item in dnsPlan.Policies)
        {
            var servers = item.Servers != null
                ? NormalizeServers(item.Servers)
                : item.Target == EDnsPolicyTarget.Direct ? direct : remote;
            if (servers.Count == 0)
            {
                continue;
            }
            foreach (var key in item.Domains.Select(ToDnsPattern).OfType<string>())
            {
                // The first policy that mentions a domain wins, like the rule order elsewhere.
                policy.TryAdd(key, servers.Count == 1 ? servers[0] : servers);
            }
        }
        if (policy.Count > 0)
        {
            dns["nameserver-policy"] = policy;
        }
        return dns;
    }

    private List<string> BuildFakeIpFilter()
    {
        var custom = UnifiedRuleParser.SplitLines(ext.FakeIpFilter);
        if (custom.Count > 0)
        {
            return custom;
        }
        var result = new List<string>(_fakeIpFilterPatterns);
        var builtin = JsonUtils.Deserialize<Rule4Sbox>(EmbedUtils.GetEmbedText(Global.SingboxFakeIPFilterFileName));
        result.AddRange(builtin?.domain ?? []);
        result.AddRange(builtin?.domain_suffix?.Select(t => $"+.{t}") ?? []);
        return result.Distinct().ToList();
    }

    private Dictionary<object, object> BuildHosts()
    {
        var hosts = new Dictionary<object, object>();
        if (plan.Dns.AddCommonHosts)
        {
            foreach (var (domain, ips) in Global.PredefinedHosts)
            {
                hosts[domain] = ips.Count == 1 ? ips[0] : ips.ToList();
            }
        }
        foreach (var (key, values) in plan.Dns.Hosts)
        {
            var match = UnifiedRuleParser.ParseDomain(key);
            var pattern = match?.Kind switch
            {
                EDomainMatchKind.Full or EDomainMatchKind.Plain => match.Value,
                EDomainMatchKind.Suffix => $"+.{match.Value}",
                _ => null,
            };
            var answers = values.Where(v => !v.StartsWith('#')).ToList();
            if (pattern == null || answers.Count == 0)
            {
                diagnostics.Info(Core, "DNS", string.Format(ResUI.MsgUnifiedDnsIgnored, $"hosts: {key}", CoreCapabilities.CoreDisplayName(Core)));
                continue;
            }
            hosts[pattern] = answers.Count == 1 ? answers[0] : answers;
        }
        return hosts;
    }

    /// <summary>
    /// Converts a domain match to a mihomo domain pattern (nameserver-policy / fake-ip-filter key).
    /// </summary>
    public static string? ToDnsPattern(DomainMatch match) => match.Kind switch
    {
        EDomainMatchKind.Full => match.Value,
        EDomainMatchKind.Suffix => $"+.{match.Value}",
        EDomainMatchKind.GeoSite => $"geosite:{match.Value}",
        EDomainMatchKind.RuleSet => $"rule-set:{match.Value}",
        _ => null,
    };

    /// <summary>
    /// v2rayN maps "dhcp" to the system resolver; mihomo calls it "system".
    /// </summary>
    private static List<string> NormalizeServers(IEnumerable<string> servers) => servers
        .Select(s => s.StartsWith("dhcp", StringComparison.OrdinalIgnoreCase) || s == "localhost" ? "system" : s)
        .Distinct()
        .ToList();

    #endregion DNS

    #region Policy

    private void ApplyPolicy(Dictionary<string, object> fileContent)
    {
        var groups = fileContent.GetValueOrDefault("proxy-groups") as List<object> ?? [];
        var proxyNames = GetNames(fileContent.GetValueOrDefault("proxies"));
        var groupNames = GetNames(groups);

        // v2rayN policy groups referenced by rules, built from members that exist in the file.
        foreach (var group in plan.Groups.Where(g => !groupNames.Contains(g.Name) && !proxyNames.Contains(g.Name)))
        {
            var members = group.Members.Where(m => proxyNames.Contains(m) || groupNames.Contains(m)).ToList();
            if (members.Count == 0)
            {
                continue;
            }
            groups.Add(ToClashGroup(group, members));
            groupNames.Add(group.Name);
        }

        // Extra groups written in Clash syntax; same-name groups replace the original ones.
        var extraGroups = ClashSyntax.ParseProxyGroups(ext.ExtraProxyGroups, null);
        foreach (var group in extraGroups)
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
            groupNames.Add(group["name"].ToString()!);
        }
        if (groups.Count > 0)
        {
            fileContent["proxy-groups"] = groups;
        }

        var known = new HashSet<string>(Global.ClashBuiltinPolicies);
        known.UnionWith(proxyNames);
        known.UnionWith(groupNames);
        foreach (var group in extraGroups)
        {
            RemoveUnknownMembers(group, known);
        }
        var proxyPolicy = ResolveProxyPolicy(groups, proxyNames, known);

        var rules = new List<object>();
        foreach (var line in UnifiedRuleParser.SplitLines(ext.ExtraRules))
        {
            if (ClashSyntax.GetRulePolicy(line) is { } target && !known.Contains(target))
            {
                diagnostics.Warn(Core, line, string.Format(ResUI.MsgUnifiedPolicyMissing, line, target));
                continue;
            }
            rules.Add(line);
        }

        var usedRuleSets = new HashSet<string>();
        foreach (var rule in plan.Rules.Where(r => r.Scope != ERuleType.DNS))
        {
            var target = ResolveTarget(rule, proxyPolicy, known);
            rules.AddRange(TranslateRule(rule, target, usedRuleSets));
        }

        if (ext.UnifiedMode == EMihomoUnifiedMode.DnsAndReplaceRules)
        {
            rules.Add($"MATCH,{proxyPolicy}");
        }
        else if (fileContent.GetValueOrDefault("rules") is List<object> oldRules)
        {
            rules.AddRange(oldRules);
        }
        fileContent["rules"] = rules;

        AddRuleProviders(fileContent, usedRuleSets);
    }

    /// <summary>
    /// A group referencing a missing proxy makes mihomo refuse the whole configuration.
    /// </summary>
    private void RemoveUnknownMembers(Dictionary<object, object> group, HashSet<string> known)
    {
        if (group.GetValueOrDefault("proxies") is not List<object> members)
        {
            return;
        }
        foreach (var member in members.Where(m => !known.Contains(m.ToString() ?? string.Empty)).ToList())
        {
            members.Remove(member);
            diagnostics.Warn(Core, group["name"].ToString()!, string.Format(ResUI.MsgUnifiedPolicyMissing, group["name"], member));
        }
        if (members.Count > 0)
        {
            return;
        }
        if (group.ContainsKey("use") || group.Keys.Any(k => k.ToString()!.StartsWith("include-all")))
        {
            group.Remove("proxies");
        }
        else
        {
            members.Add("DIRECT");
        }
    }

    private static HashSet<string> GetNames(object? list)
    {
        var names = new HashSet<string>();
        foreach (var entry in (list as List<object> ?? []).OfType<Dictionary<object, object>>())
        {
            if (entry.GetValueOrDefault("name")?.ToString() is { Length: > 0 } name)
            {
                names.Add(name);
            }
        }
        return names;
    }

    private string ResolveProxyPolicy(List<object> groups, HashSet<string> proxyNames, HashSet<string> known)
    {
        if (ext.ProxyGroupName.IsNotEmpty() && known.Contains(ext.ProxyGroupName))
        {
            return ext.ProxyGroupName;
        }
        var firstSelect = groups.OfType<Dictionary<object, object>>()
            .FirstOrDefault(g => g.GetValueOrDefault("type")?.ToString() == "select")
            ?.GetValueOrDefault("name")?.ToString();
        return firstSelect ?? groups.OfType<Dictionary<object, object>>().FirstOrDefault()?.GetValueOrDefault("name")?.ToString()
            ?? proxyNames.FirstOrDefault()
            ?? "DIRECT";
    }

    private string ResolveTarget(UnifiedRule rule, string proxyPolicy, HashSet<string> known)
    {
        switch (rule.Target.Kind)
        {
            case ERuleTargetKind.Direct:
                return "DIRECT";

            case ERuleTargetKind.Block:
                return "REJECT";

            case ERuleTargetKind.Named when known.Contains(rule.Target.Name):
                return rule.Target.Name;

            case ERuleTargetKind.Named:
                diagnostics.Warn(Core, rule.DisplayName, string.Format(ResUI.MsgUnifiedPolicyFallback,
                    rule.DisplayName, rule.Target.Name, CoreCapabilities.CoreDisplayName(Core), proxyPolicy));
                return proxyPolicy;

            default:
                return proxyPolicy;
        }
    }

    private static Dictionary<object, object> ToClashGroup(UnifiedGroup group, List<string> members)
    {
        var result = new Dictionary<object, object>
        {
            ["name"] = group.Name,
            ["type"] = group.Kind switch
            {
                EGroupKind.Select => "select",
                EGroupKind.Fallback => "fallback",
                EGroupKind.LoadBalance => "load-balance",
                EGroupKind.Chain => "relay",
                _ => "url-test",
            },
            ["proxies"] = members.Cast<object>().ToList(),
        };
        if (group.Kind is EGroupKind.UrlTest or EGroupKind.Fallback or EGroupKind.LoadBalance)
        {
            result["url"] = DefaultHealthCheckUrl;
            result["interval"] = 300;
        }
        if (group.Kind == EGroupKind.LoadBalance)
        {
            result["strategy"] = group.MultipleLoad == EMultipleLoad.RoundRobin ? "round-robin" : "consistent-hashing";
        }
        return result;
    }

    /// <summary>
    /// One IR rule becomes one Clash rule per selector value; constraints are AND-ed onto each.
    /// </summary>
    public List<string> TranslateRule(UnifiedRule rule, string target, HashSet<string>? usedRuleSets = null)
    {
        if (rule.CoreExt?.MihomoRules.IsNullOrEmpty() == false)
        {
            return UnifiedRuleParser.SplitLines(rule.CoreExt.MihomoRules).Select(t => t.Replace("{target}", target)).ToList();
        }

        var noResolve = plan.DomainStrategy == Global.AsIs;
        var selectors = new List<string>();
        foreach (var domain in rule.Domains)
        {
            if (ToDomainAtom(domain) is { } atom)
            {
                selectors.Add(atom);
                if (domain.Kind == EDomainMatchKind.RuleSet)
                {
                    usedRuleSets?.Add(domain.Value);
                }
            }
            else
            {
                diagnostics.Warn(Core, rule.DisplayName, string.Format(ResUI.MsgUnifiedValueIgnored, rule.DisplayName, domain.Raw, CoreCapabilities.CoreDisplayName(Core)));
            }
        }
        foreach (var ip in rule.Ips)
        {
            if (ip.Kind == EIpMatchKind.RuleSet)
            {
                usedRuleSets?.Add(ip.Value);
            }
            var atom = ToIpAtom(ip, "IP-CIDR", "GEOIP", noResolve);
            selectors.Add(ip.Negated ? $"NOT,(({atom}))" : atom);
        }
        foreach (var process in rule.Processes)
        {
            selectors.Add(process.Kind == EProcessMatchKind.Path ? $"PROCESS-PATH,{process.Value}" : $"PROCESS-NAME,{process.Value}");
        }

        if (rule.HasSelectors && selectors.Count == 0)
        {
            diagnostics.Warn(Core, rule.DisplayName, string.Format(ResUI.MsgUnifiedRuleSkipped, rule.DisplayName, ResUI.MsgUnifiedNoSupportedCondition, CoreCapabilities.CoreDisplayName(Core)));
            return [];
        }

        var constraints = BuildConstraints(rule);
        if (selectors.Count == 0)
        {
            return constraints.Count switch
            {
                0 => [],
                1 => [$"{constraints[0]},{target}"],
                _ => [$"AND,({string.Join(",", constraints.Select(c => $"({c})"))}),{target}"],
            };
        }
        if (constraints.Count == 0)
        {
            return selectors.Select(s => $"{s},{target}").ToList();
        }
        return selectors
            .Select(s => $"AND,({string.Join(",", new[] { s }.Concat(constraints).Select(c => $"({c})"))}),{target}")
            .ToList();
    }

    private static List<string> BuildConstraints(UnifiedRule rule)
    {
        var constraints = new List<string>();
        if (rule.Ports.Count > 0 && !rule.Ports.Any(p => p.IsAny))
        {
            constraints.Add(OrOf(rule.Ports.Select(p => $"DST-PORT,{p}")));
        }
        var networks = rule.Networks.Where(n => n is "tcp" or "udp").Distinct().ToList();
        if (networks.Count == 1)
        {
            constraints.Add($"NETWORK,{networks[0].ToUpperInvariant()}");
        }
        if (rule.SourceIps.Count > 0)
        {
            constraints.Add(OrOf(rule.SourceIps.Select(ip => ToIpAtom(ip, "SRC-IP-CIDR", "SRC-GEOIP", false))));
        }
        if (rule.SourcePorts.Count > 0)
        {
            constraints.Add(OrOf(rule.SourcePorts.Select(p => $"SRC-PORT,{p}")));
        }
        return constraints;
    }

    private static string OrOf(IEnumerable<string> atoms)
    {
        var list = atoms.ToList();
        return list.Count == 1 ? list[0] : $"OR,({string.Join(",", list.Select(a => $"({a})"))})";
    }

    public static string? ToDomainAtom(DomainMatch match) => match.Kind switch
    {
        EDomainMatchKind.Full => $"DOMAIN,{match.Value}",
        EDomainMatchKind.Suffix => $"DOMAIN-SUFFIX,{match.Value}",
        EDomainMatchKind.Keyword or EDomainMatchKind.Plain => $"DOMAIN-KEYWORD,{match.Value}",
        // Clash splits rule payloads on commas, so a regex containing one cannot be expressed.
        EDomainMatchKind.Regex when !match.Value.Contains(',') => $"DOMAIN-REGEX,{match.Value}",
        EDomainMatchKind.Dotless => $"DOMAIN-REGEX,^[^.]*{Regex.Escape(match.Value)}[^.]*$",
        EDomainMatchKind.GeoSite => $"GEOSITE,{match.Value}",
        EDomainMatchKind.RuleSet => $"RULE-SET,{match.Value}",
        _ => null,
    };

    private static string ToIpAtom(IpMatch ip, string cidrType, string geoType, bool noResolve)
    {
        var suffix = noResolve && cidrType == "IP-CIDR" ? ",no-resolve" : string.Empty;
        return ip.Kind switch
        {
            EIpMatchKind.Private => OrOf(_privateCidrs.Select(c => $"{(c.Contains(':') ? cidrType + "6" : cidrType)},{c}{suffix}")),
            EIpMatchKind.GeoIP => $"{geoType},{ip.Value.ToUpperInvariant()}{suffix}",
            EIpMatchKind.RuleSet => $"RULE-SET,{ip.Value}{suffix}",
            _ => $"{(ip.Value.Contains(':') ? cidrType + "6" : cidrType)},{ToCidr(ip.Value)}{suffix}",
        };
    }

    private static string ToCidr(string value) => value.Contains('/') ? value : value.Contains(':') ? $"{value}/128" : $"{value}/32";

    private void AddRuleProviders(Dictionary<string, object> fileContent, HashSet<string> usedRuleSets)
    {
        if (usedRuleSets.Count == 0)
        {
            return;
        }
        var providers = fileContent.GetValueOrDefault("rule-providers") as Dictionary<object, object> ?? [];
        foreach (var ruleSet in plan.RuleSets.Where(r => usedRuleSets.Contains(r.Name) && r.MihomoSource.IsNotEmpty()))
        {
            var line = $"{ruleSet.Name} = {ruleSet.Behavior}, {ruleSet.MihomoSource}, interval={ruleSet.UpdateInterval}";
            foreach (var (name, provider) in ClashSyntax.ParseRuleProviders(line, null))
            {
                providers[name] = provider;
            }
        }
        fileContent["rule-providers"] = providers;
    }

    #endregion Policy
}
