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
    private const string RuleSetPatternPrefix = "rule-set:";

    /// <summary>Rule sets referenced by the generated DNS section; they need rule-providers too.</summary>
    private readonly HashSet<string> _dnsRuleSets = [];

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
        var hosts = BuildHosts(fileContent.GetValueOrDefault("hosts") as Dictionary<object, object>);
        if (hosts.Count > 0)
        {
            fileContent["hosts"] = hosts;
        }
        // Only ever enable system hosts: mihomo uses them by default and the file may rely on that.
        if (plan.Dns.UseSystemHosts)
        {
            fileContent["use-system-hosts"] = true;
        }

        // nameserver-policy and fake-ip-filter may reference rule sets as well
        var usedRuleSets = new HashSet<string>(_dnsRuleSets);
        if (ext.UnifiedMode is EMihomoUnifiedMode.DnsAndRules or EMihomoUnifiedMode.DnsAndReplaceRules)
        {
            ApplyPolicy(fileContent, usedRuleSets);
        }
        AddRuleProviders(fileContent, usedRuleSets);
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
        if (ext.DnsCacheAlgorithm is "lru" or "arc")
        {
            dns["cache-algorithm"] = ext.DnsCacheAlgorithm;
        }

        if (dnsPlan.FakeIp)
        {
            dns["enhanced-mode"] = "fake-ip";
            dns["fake-ip-range"] = dnsPlan.FakeIpRange;
            List<string> filter;
            if (dnsPlan.GlobalFakeIp)
            {
                filter = BuildFakeIpFilter();
            }
            else
            {
                // Only domains resolved by the remote DNS get fake IPs.
                dns["fake-ip-filter-mode"] = "whitelist";
                // Explicit policies pick a DNS server and resolve real IPs, as in Xray and sing-box.
                filter = dnsPlan.Policies
                    .Where(p => p.Target == EDnsPolicyTarget.Remote && p.Servers == null && !p.IsExplicit)
                    .SelectMany(p => p.Domains).Select(ToDnsPattern).OfType<string>().Distinct().ToList();
            }
            dns["fake-ip-filter"] = filter;
            TrackRuleSets(filter);
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
            // Custom servers are queried directly, e.g. a LAN or corporate resolver.
            var servers = item.Servers != null
                ? item.Servers.Select(t => NormalizeServer(t, forceDirect: true)).Distinct().ToList()
                : item.Target == EDnsPolicyTarget.Direct ? direct : remote;
            if (servers.Count == 0)
            {
                continue;
            }
            foreach (var key in item.Domains.Select(ToDnsPattern).OfType<string>())
            {
                // The first policy that mentions a domain wins, like the rule order elsewhere.
                if (policy.TryAdd(key, servers.Count == 1 ? servers[0] : servers))
                {
                    TrackRuleSets([key]);
                }
            }
        }
        if (policy.Count > 0)
        {
            dns["nameserver-policy"] = policy;
        }
        return dns;
    }

    private void TrackRuleSets(IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns.Where(p => p.StartsWith(RuleSetPatternPrefix, StringComparison.Ordinal)))
        {
            _dnsRuleSets.Add(pattern[RuleSetPatternPrefix.Length..]);
        }
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

    /// <summary>
    /// Precedence: hosts written in the DNS settings, then the file's own hosts, then the built-in common hosts.
    /// </summary>
    private Dictionary<object, object> BuildHosts(Dictionary<object, object>? fileHosts)
    {
        var hosts = fileHosts ?? [];
        if (plan.Dns.AddCommonHosts)
        {
            foreach (var (domain, ips) in Global.PredefinedHosts)
            {
                hosts.TryAdd(domain, ips.Count == 1 ? ips[0] : ips.ToList());
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
        EDomainMatchKind.RuleSet => $"{RuleSetPatternPrefix}{match.Value}",
        _ => null,
    };

    private static List<string> NormalizeServers(IEnumerable<string> servers) => servers
        .Select(t => NormalizeServer(t))
        .Distinct()
        .ToList();

    /// <summary>
    /// Maps the DNS address forms accepted by v2rayN to mihomo's:
    /// dhcp / localhost -> system, Xray "+local" -> queried directly (#DIRECT), sing-box h3:// -> DoH over HTTP/3.
    /// Parameters are merged with any the address already carries after '#'.
    /// </summary>
    public static string NormalizeServer(string server, bool forceDirect = false)
    {
        server = server.Trim();
        if (server.StartsWith("dhcp", StringComparison.OrdinalIgnoreCase) || server is "localhost" or "local" or "system")
        {
            return "system";
        }
        var hashIndex = server.IndexOf('#');
        var address = hashIndex >= 0 ? server[..hashIndex] : server;
        var parameters = hashIndex >= 0
            ? server[(hashIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : [];

        var schemeEnd = address.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd > 0)
        {
            var scheme = address[..schemeEnd].ToLowerInvariant();
            var rest = address[(schemeEnd + 3)..];
            if (scheme.EndsWith("+local", StringComparison.Ordinal))
            {
                scheme = scheme[..^"+local".Length];
                forceDirect = true;
            }
            if (scheme == "h3")
            {
                scheme = "https";
                parameters.Add("h3=true");
            }
            address = $"{scheme}://{rest}";
        }

        // mihomo takes the outbound as a bare parameter (a proxy or group name), e.g. #DIRECT or #DIRECT&h3=true.
        var hasOutbound = parameters.Any(p => !p.Contains('='));
        if (forceDirect && !hasOutbound)
        {
            parameters.Insert(0, "DIRECT");
        }
        return parameters.Count == 0 ? address : $"{address}#{string.Join("&", parameters.Distinct())}";
    }

    #endregion DNS

    #region Policy

    private void ApplyPolicy(Dictionary<string, object> fileContent, HashSet<string> usedRuleSets)
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

        var availableRuleSets = GetAvailableRuleSets(fileContent);
        var rules = new List<object>();
        rules.AddRange(FilterHandWrittenRules(ResUI.TbMihomoExtraRules, UnifiedRuleParser.SplitLines(ext.ExtraRules), known, availableRuleSets, usedRuleSets));

        var catchAll = false;
        foreach (var rule in plan.Rules.Where(r => r.Scope != ERuleType.DNS))
        {
            var target = ResolveTarget(rule, proxyPolicy, known);
            var translated = TranslateRule(rule, target, usedRuleSets);
            if (rule.CoreExt?.MihomoRules.IsNullOrEmpty() == false)
            {
                // Hand-written lines are checked like the extra rules: mihomo refuses the whole file otherwise.
                translated = FilterHandWrittenRules(rule.DisplayName, translated, known, availableRuleSets, usedRuleSets);
            }
            rules.AddRange(translated);
            if (translated.Any(t => t.StartsWith("MATCH,", StringComparison.Ordinal)))
            {
                // Nothing after a catch-all rule is reached, so stop like the core would.
                catchAll = true;
                if (ext.UnifiedMode == EMihomoUnifiedMode.DnsAndRules)
                {
                    diagnostics.Info(Core, rule.DisplayName, string.Format(ResUI.MsgUnifiedCatchAllRule, rule.DisplayName));
                }
                break;
            }
        }

        // After a catch-all rule neither the default nor the original rules can be reached.
        if (!catchAll)
        {
            if (ext.UnifiedMode == EMihomoUnifiedMode.DnsAndReplaceRules)
            {
                // Same default as Xray / sing-box: unmatched traffic goes through the proxy.
                rules.Add($"MATCH,{proxyPolicy}");
            }
            else if (fileContent.GetValueOrDefault("rules") is List<object> oldRules)
            {
                rules.AddRange(oldRules);
            }
        }
        fileContent["rules"] = rules;
    }

    /// <summary>
    /// Rule sets a rule may reference: declared ones with a mihomo source and the file's own rule-providers.
    /// </summary>
    private HashSet<string> GetAvailableRuleSets(Dictionary<string, object> fileContent)
    {
        var names = new HashSet<string>(plan.RuleSets.Where(r => r.MihomoSource.IsNotEmpty()).Select(r => r.Name));
        if (fileContent.GetValueOrDefault("rule-providers") is Dictionary<object, object> providers)
        {
            names.UnionWith(providers.Keys.Select(k => k.ToString()).OfType<string>());
        }
        return names;
    }

    /// <summary>
    /// Keeps the hand-written Clash rule lines mihomo can load: a valid shape, an existing policy and
    /// rule sets that exist. Rule sets they reference are added to rule-providers.
    /// </summary>
    private List<string> FilterHandWrittenRules(string subject, IEnumerable<string> lines, HashSet<string> known,
        HashSet<string> availableRuleSets, HashSet<string> usedRuleSets)
    {
        var result = new List<string>();
        foreach (var line in lines)
        {
            if (!ClashSyntax.IsValidRuleLine(line))
            {
                diagnostics.Warn(Core, subject, string.Format(ResUI.MsgUnifiedRuleLineInvalid, subject, line));
                continue;
            }
            if (ClashSyntax.GetRulePolicy(line) is { } target && !known.Contains(target))
            {
                diagnostics.Warn(Core, subject, string.Format(ResUI.MsgUnifiedPolicyMissing, line, target));
                continue;
            }
            var ruleSets = ClashSyntax.GetRuleSetReferences(line).ToList();
            if (ruleSets.FirstOrDefault(r => !availableRuleSets.Contains(r)) is { } missing)
            {
                diagnostics.Warn(Core, subject, string.Format(ResUI.MsgUnifiedRuleLineRuleSetMissing, subject, line, missing));
                continue;
            }
            usedRuleSets.UnionWith(ruleSets);
            result.Add(line);
        }
        return result;
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
        if (ext.ProxyGroupName.IsNotEmpty())
        {
            if (known.Contains(ext.ProxyGroupName))
            {
                return ext.ProxyGroupName;
            }
            diagnostics.Warn(Core, ext.ProxyGroupName, string.Format(ResUI.MsgUnifiedProxyGroupMissing, ext.ProxyGroupName));
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
            // mihomo matches the full executable name, including .exe on Windows
            selectors.Add(process.Kind == EProcessMatchKind.Path ? $"PROCESS-PATH,{process.Value}" : $"PROCESS-NAME,{Utils.GetExeName(process.Value)}");
        }

        if (rule.HasSelectors && selectors.Count == 0)
        {
            diagnostics.Warn(Core, rule.DisplayName, string.Format(ResUI.MsgUnifiedRuleSkipped, rule.DisplayName, ResUI.MsgUnifiedNoSupportedCondition, CoreCapabilities.CoreDisplayName(Core)));
            return [];
        }

        if (BuildConstraints(rule) is not { } constraints)
        {
            diagnostics.Warn(Core, rule.DisplayName, string.Format(ResUI.MsgUnifiedRuleSkipped, rule.DisplayName,
                $"network: {string.Join(",", rule.Networks)}", CoreCapabilities.CoreDisplayName(Core)));
            return [];
        }
        if (constraints.Count > 0)
        {
            // Logical rules delimit sub-rules with parentheses, so a payload with unbalanced ones would break the rule.
            foreach (var selector in selectors.Where(s => !HasBalancedParentheses(s)).ToList())
            {
                selectors.Remove(selector);
                diagnostics.Warn(Core, rule.DisplayName, string.Format(ResUI.MsgUnifiedValueIgnored, rule.DisplayName, selector, CoreCapabilities.CoreDisplayName(Core)));
            }
            if (rule.HasSelectors && selectors.Count == 0)
            {
                diagnostics.Warn(Core, rule.DisplayName, string.Format(ResUI.MsgUnifiedRuleSkipped, rule.DisplayName, ResUI.MsgUnifiedNoSupportedCondition, CoreCapabilities.CoreDisplayName(Core)));
                return [];
            }
        }
        if (selectors.Count == 0)
        {
            return constraints.Count switch
            {
                // Only trivial constraints such as port 0-65535 or network tcp,udp: the rule matches everything.
                0 => rule.HasConstraints ? [$"MATCH,{target}"] : [],
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

    /// <summary>
    /// The AND-ed constraints, empty when they all match everything (port 0-65535, network tcp,udp),
    /// or null when they cannot be expressed (e.g. only network icmp): dropping them would broaden the rule.
    /// </summary>
    private static List<string>? BuildConstraints(UnifiedRule rule)
    {
        var constraints = new List<string>();
        if (rule.Ports.Count > 0 && !rule.Ports.Any(p => p.IsAny))
        {
            constraints.Add(OrOf(rule.Ports.Select(p => $"DST-PORT,{p}")));
        }
        var networks = rule.Networks.Where(n => n is "tcp" or "udp").Distinct().ToList();
        if (rule.Networks.Count > 0 && networks.Count == 0)
        {
            return null;
        }
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

    private static bool HasBalancedParentheses(string text)
    {
        var depth = 0;
        foreach (var c in text)
        {
            depth += c switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth < 0)
            {
                return false;
            }
        }
        return depth == 0;
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
