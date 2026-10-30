namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Builds the core-agnostic <see cref="UnifiedPlan" /> from the shared settings
/// (SimpleDNSItem, the active RoutingItem, policy groups and rule sets).
/// </summary>
public static class UnifiedPlanBuilder
{
    public static UnifiedPlan Build(
        SimpleDNSItem? simpleDns,
        RoutingItem? routing,
        string? defaultDomainStrategy,
        IEnumerable<RuleSetItem>? ruleSets = null,
        IEnumerable<UnifiedGroup>? groups = null)
    {
        simpleDns ??= new SimpleDNSItem();
        var allRules = JsonUtils.Deserialize<List<RulesItem>>(routing?.RuleSet) ?? [];
        var rules = allRules.Where(t => t.Enabled).Select(UnifiedRuleParser.ParseRule).ToList();

        var domainStrategy = routing?.DomainStrategy.IsNotEmpty() == true ? routing.DomainStrategy : defaultDomainStrategy;

        return new UnifiedPlan
        {
            Rules = rules,
            Dns = BuildDns(simpleDns, rules, allRules),
            Groups = groups?.ToList() ?? [],
            RuleSets = ruleSets?.Where(t => t.Name.IsNotEmpty()).ToList() ?? [],
            DomainStrategy = domainStrategy.IsNullOrEmpty() ? Global.AsIs : domainStrategy,
        };
    }

    private static UnifiedDns BuildDns(SimpleDNSItem simpleDns, List<UnifiedRule> rules, List<RulesItem> allRules)
    {
        var policies = new List<DnsPolicy>();
        var lines = UnifiedRuleParser.SplitLines(simpleDns.DnsPolicy);
        for (var i = 0; i < lines.Count; i++)
        {
            if (UnifiedRuleParser.ParseDnsPolicyLine(lines[i], i) is { } policy)
            {
                policies.Add(policy);
            }
        }
        foreach (var rule in rules.Where(r => r.Scope != ERuleType.Routing && r.Domains.Count > 0))
        {
            var target = rule.Target.Kind switch
            {
                ERuleTargetKind.Direct => EDnsPolicyTarget.Direct,
                ERuleTargetKind.Block => EDnsPolicyTarget.Block,
                _ => EDnsPolicyTarget.Remote,
            };
            policies.Add(new DnsPolicy(rule.Domains, target, rule.Id));
        }

        return new UnifiedDns
        {
            DirectServers = SplitServers(simpleDns.DirectDNS ?? Global.DomainDirectDNSAddress.First()),
            RemoteServers = SplitServers(simpleDns.RemoteDNS ?? Global.DomainRemoteDNSAddress.First()),
            BootstrapServers = SplitServers(simpleDns.BootstrapDNS ?? Global.DomainPureIPDNSAddress.First()),
            FakeIp = simpleDns.FakeIP == true,
            GlobalFakeIp = simpleDns.GlobalFakeIp != false,
            FakeIpRange = simpleDns.FakeIPRange.IsNullOrEmpty() ? Global.FakeIPRanges.First() : simpleDns.FakeIPRange,
            Hosts = Utils.ParseHostsToDictionary(simpleDns.Hosts),
            UseSystemHosts = simpleDns.UseSystemHosts == true,
            AddCommonHosts = simpleDns.AddCommonHosts == true,
            BlockAAAA = simpleDns.BlockAAAAQuery == true,
            BlockBindingQuery = simpleDns.BlockBindingQuery == true,
            ServeStale = simpleDns.ServeStale == true,
            ParallelQuery = simpleDns.ParallelQuery == true,
            DirectExpectedIps = (Utils.String2List(simpleDns.DirectExpectedIPs) ?? [])
                .Select(UnifiedRuleParser.ParseIp).OfType<IpMatch>().ToList(),
            Policies = policies,
            FinalDirect = IsFinalDirect(allRules),
        };
    }

    /// <summary>
    /// The last effective routing rule being a catch-all direct rule means unmatched queries should use the
    /// direct DNS. Disabled and DNS-only rules do not count, and any restriction (source, protocol, inbound)
    /// means the rule does not catch everything.
    /// </summary>
    public static bool IsFinalDirect(List<RulesItem>? rules)
    {
        if (rules?.LastOrDefault(r => r.Enabled && r.RuleType != ERuleType.DNS) is not { OutboundTag: Global.DirectTag } lastRule)
        {
            return false;
        }
        if (lastRule.SourceIp?.Count > 0 || lastRule.SourcePort.IsNotEmpty()
            || lastRule.Protocol?.Count > 0 || lastRule.InboundTag?.Count > 0)
        {
            return false;
        }
        var noDomain = lastRule.Domain == null || lastRule.Domain.Count == 0;
        var noProcess = lastRule.Process == null || lastRule.Process.Count == 0;
        var isAnyIp = lastRule.Ip == null || lastRule.Ip.Count == 0 || lastRule.Ip.Contains("0.0.0.0/0");
        var isAnyPort = string.IsNullOrEmpty(lastRule.Port) || lastRule.Port == "0-65535";
        var isAnyNetwork = string.IsNullOrEmpty(lastRule.Network) || lastRule.Network == "tcp,udp";
        return noDomain && noProcess && isAnyIp && isAnyPort && isAnyNetwork;
    }

    public static List<string> SplitServers(string? text)
    {
        return (text ?? string.Empty)
            .Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    /// <summary>
    /// Builds groups from policy group / proxy chain profiles referenced by routing rules.
    /// </summary>
    public static async Task<List<UnifiedGroup>> BuildGroupsAsync(IEnumerable<ProfileItem> profiles)
    {
        var groups = new List<UnifiedGroup>();
        foreach (var profile in profiles.Where(p => p.ConfigType.IsGroupType()).DistinctBy(p => p.IndexId))
        {
            var (children, extra) = await GroupProfileManager.GetChildProfileItems(profile);
            var multipleLoad = extra?.MultipleLoad ?? EMultipleLoad.LeastPing;
            groups.Add(new UnifiedGroup
            {
                Name = profile.Remarks,
                Kind = profile.ConfigType == EConfigType.ProxyChain ? EGroupKind.Chain : ToGroupKind(multipleLoad),
                MultipleLoad = multipleLoad,
                Members = children.Select(c => c.Remarks).Where(r => r.IsNotEmpty()).ToList(),
                Filter = extra?.Filter,
            });
        }
        return groups;
    }

    public static EGroupKind ToGroupKind(EMultipleLoad multipleLoad) => multipleLoad switch
    {
        EMultipleLoad.Fallback => EGroupKind.Fallback,
        EMultipleLoad.Random or EMultipleLoad.RoundRobin => EGroupKind.LoadBalance,
        _ => EGroupKind.UrlTest,
    };
}
