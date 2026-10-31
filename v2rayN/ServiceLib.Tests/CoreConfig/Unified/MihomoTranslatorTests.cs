namespace ServiceLib.Tests.CoreConfig.Unified;

public class MihomoTranslatorTests
{
    private const string SourceYaml = """
        dns:
          enable: true
          listen: 0.0.0.0:1053
          nameserver:
            - 8.8.8.8
        proxies:
          - name: HK-01
            type: ss
            server: hk.example.com
            port: 443
            cipher: aes-128-gcm
            password: pass
          - name: JP-01
            type: ss
            server: jp.example.com
            port: 443
            cipher: aes-128-gcm
            password: pass
        proxy-groups:
          - name: Auto
            type: url-test
            proxies: [HK-01, JP-01]
          - name: Proxy
            type: select
            proxies: [Auto, HK-01, JP-01]
        rules:
          - DOMAIN-SUFFIX,original.example.com,Proxy
          - MATCH,DIRECT
        """;

    private static Dictionary<string, object> Load() => YamlUtils.FromYaml<Dictionary<string, object>>(SourceYaml);

    private static Dictionary<string, object> RoundTrip(Dictionary<string, object> content) =>
        YamlUtils.FromYaml<Dictionary<string, object>>(YamlUtils.ToYaml(content));

    private static (Dictionary<string, object> Result, UnifiedDiagnostics Diagnostics) Translate(
        List<RulesItem> rules,
        MihomoExtension ext,
        SimpleDNSItem? dns = null,
        List<UnifiedGroup>? groups = null,
        List<RuleSetItem>? ruleSets = null,
        string domainStrategy = "AsIs")
    {
        var plan = UnifiedPlanBuilder.Build(
            dns ?? new SimpleDNSItem
            {
                DirectDNS = "223.5.5.5,https://dns.alidns.com/dns-query",
                RemoteDNS = "https://1.1.1.1/dns-query",
                BootstrapDNS = "223.5.5.5",
            },
            new RoutingItem { RuleSet = JsonUtils.Serialize(rules) },
            domainStrategy,
            ruleSets,
            groups);
        var validated = UnifiedPlanValidator.Validate(plan, ECoreType.mihomo, new UnifiedDiagnostics());
        var diagnostics = new UnifiedDiagnostics();
        var content = Load();
        new MihomoTranslator(validated, ext, false, diagnostics).Apply(content);
        return (RoundTrip(content), diagnostics);
    }

    private static List<string> Rules(Dictionary<string, object> content) =>
        ((List<object>)content["rules"]).Select(t => t.ToString()!).ToList();

    [Test]
    public async Task Apply_Off_ShouldKeepFile()
    {
        var content = Load();
        var plan = UnifiedPlanBuilder.Build(new SimpleDNSItem(), new RoutingItem { RuleSet = "[]" }, Global.AsIs);
        new MihomoTranslator(plan, new MihomoExtension(), false, new UnifiedDiagnostics()).Apply(content);

        var dns = (Dictionary<object, object>)content["dns"];
        await ((List<object>)dns["nameserver"]).Single().Should().BeEqualTo("8.8.8.8");
    }

    [Test]
    public async Task Apply_DnsOnly_ShouldTranslateSharedDnsAndKeepRules()
    {
        var rules = new List<RulesItem>
        {
            new() { Id = "cn", RuleType = ERuleType.ALL, OutboundTag = Global.DirectTag, Domain = ["geosite:cn", "keyword:corp"] },
            new() { Id = "gfw", RuleType = ERuleType.ALL, OutboundTag = Global.ProxyTag, Domain = ["domain:google.com"] },
        };
        var dnsItem = new SimpleDNSItem
        {
            DirectDNS = "223.5.5.5",
            RemoteDNS = "https://1.1.1.1/dns-query",
            BootstrapDNS = "119.29.29.29",
            FakeIP = true,
            FakeIPRange = "198.18.0.0/15",
            Hosts = "full:nas.lan 192.168.1.10\ndomain:home.arpa 192.168.1.1",
            DnsPolicy = "full:corp.example.com = 10.0.0.53",
        };

        var (result, _) = Translate(rules, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsOnly, RespectRules = true }, dnsItem);

        var dns = (Dictionary<object, object>)result["dns"];
        await dns["listen"].Should().BeEqualTo("0.0.0.0:1053");
        await dns["enhanced-mode"].Should().BeEqualTo("fake-ip");
        await dns["respect-rules"].Should().BeEqualTo("true");
        await ((List<object>)dns["nameserver"]).Single().Should().BeEqualTo("https://1.1.1.1/dns-query");
        await ((List<object>)dns["default-nameserver"]).Single().Should().BeEqualTo("119.29.29.29");
        await ((List<object>)dns["fake-ip-filter"]).Should().Contain("+.lan");
        var policy = (Dictionary<object, object>)dns["nameserver-policy"];
        await policy["corp.example.com"].Should().BeEqualTo("10.0.0.53#DIRECT");
        await policy["geosite:cn"].Should().BeEqualTo("223.5.5.5");
        await policy["+.google.com"].Should().BeEqualTo("https://1.1.1.1/dns-query");
        await policy.ContainsKey("keyword:corp").Should().BeFalse();
        var hosts = (Dictionary<object, object>)result["hosts"];
        await hosts["nas.lan"].Should().BeEqualTo("192.168.1.10");
        await hosts["+.home.arpa"].Should().BeEqualTo("192.168.1.1");
        await Rules(result).Count.Should().BeEqualTo(2);
    }

    [Test]
    public async Task Apply_DnsAndRules_ShouldTranslateRulesBeforeOriginal()
    {
        var rules = new List<RulesItem>
        {
            new() { Id = "ads", RuleType = ERuleType.Routing, OutboundTag = Global.BlockTag, Domain = ["geosite:category-ads-all"] },
            new() { Id = "lan", RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, Ip = ["geoip:private", "!geoip:cn"] },
            new() { Id = "ssh", RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, Domain = ["full:git.example.com"], Port = "22,2222", Network = "tcp" },
            new() { Id = "jp", RuleType = ERuleType.Routing, OutboundTag = "JP-01", Domain = ["dotless:intra"] },
            new() { Id = "missing", RuleType = ERuleType.Routing, OutboundTag = "NoSuchNode", Process = ["curl", "/usr/bin/wget"] },
            new() { Id = "bt", RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, Protocol = ["bittorrent"] },
            new() { Id = "src", RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, SourceIp = ["192.168.1.0/24"], SourcePort = "5000" },
        };

        var (result, diagnostics) = Translate(rules, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsAndRules, ExtraRules = "DOMAIN,extra.example.com,Auto" });

        var translated = string.Join("\n", Rules(result));
        await translated.Should().BeEqualTo(string.Join("\n", new List<string>
        {
            "DOMAIN,extra.example.com,Auto",
            "GEOSITE,category-ads-all,REJECT",
            "OR,((IP-CIDR,0.0.0.0/8,no-resolve),(IP-CIDR,10.0.0.0/8,no-resolve),(IP-CIDR,100.64.0.0/10,no-resolve),(IP-CIDR,127.0.0.0/8,no-resolve),(IP-CIDR,169.254.0.0/16,no-resolve),(IP-CIDR,172.16.0.0/12,no-resolve),(IP-CIDR,192.168.0.0/16,no-resolve),(IP-CIDR,224.0.0.0/4,no-resolve),(IP-CIDR,240.0.0.0/4,no-resolve),(IP-CIDR6,::1/128,no-resolve),(IP-CIDR6,fc00::/7,no-resolve),(IP-CIDR6,fe80::/10,no-resolve)),DIRECT",
            "NOT,((GEOIP,CN,no-resolve)),DIRECT",
            "AND,((DOMAIN,git.example.com),(OR,((DST-PORT,22),(DST-PORT,2222))),(NETWORK,TCP)),DIRECT",
            "DOMAIN-REGEX,^[^.]*intra[^.]*$,JP-01",
            "PROCESS-NAME,curl,Proxy",
            "PROCESS-PATH,/usr/bin/wget,Proxy",
            "AND,((SRC-IP-CIDR,192.168.1.0/24),(SRC-PORT,5000)),DIRECT",
            "DOMAIN-SUFFIX,original.example.com,Proxy",
            "MATCH,DIRECT",
        }));
        await diagnostics.Items.Any(d => d.Message.Contains("NoSuchNode")).Should().BeTrue();
    }

    [Test]
    public async Task Apply_ReplaceRules_ShouldEndWithMatchToProxyGroup()
    {
        var rules = new List<RulesItem> { new() { Id = "cn", RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, Ip = ["geoip:cn"] } };

        var (result, _) = Translate(rules, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsAndReplaceRules, ProxyGroupName = "Auto" }, domainStrategy: Global.IPIfNonMatch);

        await string.Join("\n", Rules(result)).Should().BeEqualTo("GEOIP,CN,DIRECT\nMATCH,Auto");
    }

    [Test]
    public async Task Apply_PolicyGroupsAndRuleSets_ShouldBeAdded()
    {
        var rules = new List<RulesItem>
        {
            new() { Id = "stream", RuleType = ERuleType.Routing, OutboundTag = "Streaming", Domain = ["ruleset:netflix", "ruleset:no-mihomo-source"] },
        };
        var groups = new List<UnifiedGroup>
        {
            new() { Name = "Streaming", Kind = EGroupKind.Fallback, Members = ["JP-01", "Unknown", "HK-01"] },
        };
        var ruleSets = new List<RuleSetItem>
        {
            new() { Name = "netflix", Behavior = "domain", MihomoSource = "https://example.com/netflix.mrs" },
            new() { Name = "no-mihomo-source", Behavior = "domain", SingboxSource = "https://example.com/x.srs" },
        };
        var ext = new MihomoExtension
        {
            UnifiedMode = EMihomoUnifiedMode.DnsAndRules,
            ExtraProxyGroups = "Proxy = select, Streaming, HK-01, Ghost",
        };

        var (result, _) = Translate(rules, ext, groups: groups, ruleSets: ruleSets);

        var groupList = ((List<object>)result["proxy-groups"]).Cast<Dictionary<object, object>>().ToList();
        var streaming = groupList.Single(g => g["name"].Equals("Streaming"));
        await streaming["type"].Should().BeEqualTo("fallback");
        await ((List<object>)streaming["proxies"]).Should().BeEquivalentTo(new List<object> { "JP-01", "HK-01" });
        var proxy = groupList.Single(g => g["name"].Equals("Proxy"));
        await ((List<object>)proxy["proxies"]).Should().BeEquivalentTo(new List<object> { "Streaming", "HK-01" });

        await Rules(result)[0].Should().BeEqualTo("RULE-SET,netflix,Streaming");
        var providers = (Dictionary<object, object>)result["rule-providers"];
        await providers.Count.Should().BeEqualTo(1);
        var netflix = (Dictionary<object, object>)providers["netflix"];
        await netflix["format"].Should().BeEqualTo("mrs");
        await netflix["type"].Should().BeEqualTo("http");
    }

    [Test]
    public async Task TranslateRule_CoreExtension_ShouldReplaceTranslation()
    {
        var rules = new List<RulesItem>
        {
            new()
            {
                Id = "x", RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, Domain = ["full:a.com"],
                CoreExt = new RuleCoreExtension { MihomoRules = "DOMAIN-WILDCARD,*.a.com,{target}" },
            },
        };

        var (result, _) = Translate(rules, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsAndReplaceRules });

        await Rules(result)[0].Should().BeEqualTo("DOMAIN-WILDCARD,*.a.com,DIRECT");
    }

    [Test]
    public async Task ClashSyntax_ParseProxyGroups_ShouldHandleOptionsAndQuotes()
    {
        var invalid = new List<string>();
        var groups = ClashSyntax.ParseProxyGroups("""
            Auto = url-test, HK-01, JP-01, interval=600, tolerance=50
            HK = select, include-all=true, filter="(?i)hk|港,香港"
            Bad = unknown-type, a
            Empty = select
            """, invalid);

        await groups.Count.Should().BeEqualTo(2);
        await groups[0]["interval"].Should().BeEqualTo(600);
        await groups[0]["url"].Should().BeEqualTo("https://www.gstatic.com/generate_204");
        await groups[1]["filter"].Should().BeEqualTo("(?i)hk|港,香港");
        await invalid.Count.Should().BeEqualTo(2);
    }

    [Test]
    public async Task ClashSyntax_GetRulePolicy_ShouldHandleRuleShapes()
    {
        await ClashSyntax.GetRulePolicy("MATCH,Proxy").Should().BeEqualTo("Proxy");
        await ClashSyntax.GetRulePolicy("IP-CIDR,10.0.0.0/8,DIRECT,no-resolve").Should().BeEqualTo("DIRECT");
        await ClashSyntax.GetRulePolicy("OR,((DOMAIN,a.com),(DOMAIN,b.com)),Proxy").Should().BeEqualTo("Proxy");
        await ClashSyntax.GetRulePolicy("SUB-RULE,(NETWORK,tcp),sub").Should().BeNull();
    }

    [Test]
    public async Task RuleSetTextFormat_ShouldRoundTrip()
    {
        var invalid = new List<string>();
        var items = RuleSetTextFormat.Parse("""
            ads = domain, singbox=https://x/ads.srs, mihomo=https://x/ads.mrs, interval=3600
            bad = nope, singbox=x
            nosrc = domain, interval=1
            ads = domain, singbox=https://x/duplicate.srs
            """, invalid);

        await items.Single().UpdateInterval.Should().BeEqualTo(3600);
        await invalid.Count.Should().BeEqualTo(3);
        var withComma = RuleSetTextFormat.Parse("q = classical, mihomo=https://x/list?a=1,2&b=3, interval=60", null).Single();
        await withComma.MihomoSource.Should().BeEqualTo("https://x/list?a=1,2&b=3");
        await RuleSetTextFormat.ToText(items).Should().BeEqualTo("ads = domain, singbox=https://x/ads.srs, mihomo=https://x/ads.mrs, interval=3600");
    }

    [Test]
    public async Task Apply_DnsOnly_ShouldAddProvidersForRuleSetsUsedByDns()
    {
        var rules = new List<RulesItem> { new() { Id = "cn", RuleType = ERuleType.DNS, OutboundTag = Global.DirectTag, Domain = ["ruleset:cn-sites", "ruleset:undeclared"] } };
        var ruleSets = new List<RuleSetItem> { new() { Name = "cn-sites", Behavior = "domain", MihomoSource = "https://example.com/cn.mrs" } };

        var (result, _) = Translate(rules, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsOnly }, ruleSets: ruleSets);

        var policy = (Dictionary<object, object>)((Dictionary<object, object>)result["dns"])["nameserver-policy"];
        await policy.ContainsKey("rule-set:cn-sites").Should().BeTrue();
        await policy.ContainsKey("rule-set:undeclared").Should().BeFalse();
        var providers = (Dictionary<object, object>)result["rule-providers"];
        await providers.ContainsKey("cn-sites").Should().BeTrue();
    }

    [Test]
    public async Task Apply_ShouldMergeHostsWithTheFile()
    {
        var content = Load();
        content["hosts"] = new Dictionary<object, object> { ["router.lan"] = "192.168.1.1" };
        var plan = UnifiedPlanBuilder.Build(new SimpleDNSItem { Hosts = "full:nas.lan 192.168.1.10" }, new RoutingItem { RuleSet = "[]" }, Global.AsIs);

        new MihomoTranslator(plan, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsOnly }, false, new UnifiedDiagnostics()).Apply(content);

        var hosts = (Dictionary<object, object>)content["hosts"];
        await hosts["router.lan"].Should().BeEqualTo("192.168.1.1");
        await hosts["nas.lan"].Should().BeEqualTo("192.168.1.10");
    }

    [Test]
    [Arguments("dhcp://auto", "system")]
    [Arguments("localhost", "system")]
    [Arguments("223.5.5.5", "223.5.5.5")]
    [Arguments("https+local://dns.alidns.com/dns-query", "https://dns.alidns.com/dns-query#DIRECT")]
    [Arguments("h3://dns.google/dns-query", "https://dns.google/dns-query#h3=true")]
    [Arguments("tcp://8.8.8.8", "tcp://8.8.8.8")]
    public async Task NormalizeServer_ShouldMapAddressForms(string input, string expected)
    {
        await MihomoTranslator.NormalizeServer(input).Should().BeEqualTo(expected);
    }

    [Test]
    public async Task Apply_MissingProxyGroup_ShouldWarnAndFallBack()
    {
        var rules = new List<RulesItem> { new() { Id = "g", RuleType = ERuleType.Routing, OutboundTag = Global.ProxyTag, Domain = ["full:a.com"] } };

        var (result, diagnostics) = Translate(rules, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsAndRules, ProxyGroupName = "Nope" });

        await Rules(result)[0].Should().BeEqualTo("DOMAIN,a.com,Proxy");
        await diagnostics.Items.Any(d => d.Message.Contains("Nope")).Should().BeTrue();
    }

    [Test]
    public async Task TranslateRule_UnbalancedRegexInsideLogicalRule_ShouldBeDropped()
    {
        var rules = new List<RulesItem>
        {
            new() { Id = "r", RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, Domain = ["regexp:^a\\(b$", "full:ok.com"], Port = "443" },
            new() { Id = "plain", RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, Domain = ["regexp:^a\\(b$"] },
        };

        var (result, _) = Translate(rules, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsAndReplaceRules });

        var translated = Rules(result);
        await translated[0].Should().BeEqualTo("AND,((DOMAIN,ok.com),(DST-PORT,443)),DIRECT");
        await translated[1].Should().BeEqualTo("DOMAIN-REGEX,^a\\(b$,DIRECT");
    }

    [Test]
    public async Task Apply_CatchAllRule_ShouldBecomeMatchAndEndTheRules()
    {
        // The "blacklist" preset ends with port 0-65535 -> direct: unmatched traffic must go direct, not to the proxy.
        var rules = new List<RulesItem>
        {
            new() { Id = "gfw", RuleType = ERuleType.Routing, OutboundTag = Global.ProxyTag, Domain = ["geosite:gfw"] },
            new() { Id = "rest", RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, Port = "0-65535" },
            new() { Id = "unreachable", RuleType = ERuleType.Routing, OutboundTag = Global.BlockTag, Domain = ["full:x.com"] },
        };

        var (replaced, _) = Translate(rules, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsAndReplaceRules });
        var (prepended, diagnostics) = Translate(rules, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsAndRules });

        await string.Join("\n", Rules(replaced)).Should().BeEqualTo("GEOSITE,gfw,Proxy\nMATCH,DIRECT");
        await string.Join("\n", Rules(prepended)).Should().BeEqualTo("GEOSITE,gfw,Proxy\nMATCH,DIRECT");
        await diagnostics.Items.Any(d => d.Subject == "rest").Should().BeTrue();
    }

    [Test]
    [Arguments("h3://10.0.0.53/dns-query", "https://10.0.0.53/dns-query#DIRECT&h3=true")]
    [Arguments("https+local://dns.example/dns-query#skip-cert-verify=true", "https://dns.example/dns-query#DIRECT&skip-cert-verify=true")]
    [Arguments("https://dns.example/dns-query#Proxy", "https://dns.example/dns-query#Proxy")]
    [Arguments("10.0.0.53", "10.0.0.53#DIRECT")]
    [Arguments("dhcp://auto", "system")]
    public async Task NormalizeServer_ForceDirect_ShouldMergeParameters(string input, string expected)
    {
        await MihomoTranslator.NormalizeServer(input, forceDirect: true).Should().BeEqualTo(expected);
    }

    [Test]
    public async Task Apply_CustomFakeIpFilterRuleSet_ShouldGetAProvider()
    {
        var ruleSets = new List<RuleSetItem> { new() { Name = "lan", Behavior = "domain", MihomoSource = "https://example.com/lan.yaml" } };
        var dns = new SimpleDNSItem { DirectDNS = "223.5.5.5", RemoteDNS = "1.1.1.1", FakeIP = true };

        var (result, _) = Translate([], new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsOnly, FakeIpFilter = "rule-set:lan\n+.home" }, dns, ruleSets: ruleSets);

        await ((Dictionary<object, object>)result["rule-providers"]).ContainsKey("lan").Should().BeTrue();
    }

    [Test]
    public async Task ParseClashContent_ShouldKeepExplicitStringTags()
    {
        var content = CoreConfigClashService.ParseClashContent("""
            proxies:
              - name: a
                type: ss
                password: !<str> 123456
            """)!;

        var yaml = CoreConfigClashService.RestoreStrTags(YamlUtils.ToYaml(content));

        await yaml.Should().Contain("!!str 123456");
    }

    [Test]
    public async Task Apply_Hosts_ShouldKeepFileEntriesOverCommonHostsAndLeaveUseSystemHosts()
    {
        var content = Load();
        content["hosts"] = new Dictionary<object, object> { ["dns.google"] = "10.0.0.53" };
        content["use-system-hosts"] = true;
        var noHosts = UnifiedPlanBuilder.Build(new SimpleDNSItem(), new RoutingItem { RuleSet = "[]" }, Global.AsIs);
        new MihomoTranslator(noHosts, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsOnly }, false, new UnifiedDiagnostics()).Apply(content);
        await content["use-system-hosts"].Should().BeEqualTo(true);

        var common = UnifiedPlanBuilder.Build(new SimpleDNSItem { AddCommonHosts = true }, new RoutingItem { RuleSet = "[]" }, Global.AsIs);
        new MihomoTranslator(common, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsOnly }, false, new UnifiedDiagnostics()).Apply(content);

        var hosts = (Dictionary<object, object>)content["hosts"];
        await hosts["dns.google"].Should().BeEqualTo("10.0.0.53");
        await hosts.ContainsKey("dns.alidns.com").Should().BeTrue();
    }

    [Test]
    public async Task TranslateRule_InexpressibleNetworkOnly_ShouldBeDroppedNotMatchAll()
    {
        var rules = new List<RulesItem>
        {
            new() { Id = "icmp", RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, Network = "icmp" },
            new() { Id = "next", RuleType = ERuleType.Routing, OutboundTag = Global.BlockTag, Domain = ["full:ads.com"] },
        };

        var (result, diagnostics) = Translate(rules, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsAndRules });

        await string.Join("\n", Rules(result)).Should().BeEqualTo("DOMAIN,ads.com,REJECT\nDOMAIN-SUFFIX,original.example.com,Proxy\nMATCH,DIRECT");
        await diagnostics.Items.Any(d => d.Subject == "icmp").Should().BeTrue();
    }

    [Test]
    public async Task Validate_FileRuleProviders_ShouldBeAvailableForDnsPolicies()
    {
        var plan = UnifiedPlanBuilder.Build(new SimpleDNSItem { DnsPolicy = "ruleset:netflix = 192.168.1.1" }, new RoutingItem { RuleSet = "[]" }, Global.AsIs);
        var content = Load();
        content["rule-providers"] = new Dictionary<object, object> { ["netflix"] = new Dictionary<object, object> { ["type"] = "http" } };

        var validated = UnifiedPlanValidator.Validate(plan, ECoreType.mihomo, new UnifiedDiagnostics(),
            CoreConfigClashService.GetAvailableRuleSetNames(new Config(), content));
        new MihomoTranslator(validated, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsOnly }, false, new UnifiedDiagnostics()).Apply(content);

        var policy = (Dictionary<object, object>)((Dictionary<object, object>)content["dns"])["nameserver-policy"];
        await policy["rule-set:netflix"].Should().BeEqualTo("192.168.1.1#DIRECT");
    }

    [Test]
    public async Task Apply_ExplicitRemotePolicy_ShouldNotGetFakeIpWhitelisted()
    {
        var rules = new List<RulesItem> { new() { Id = "gfw", RuleType = ERuleType.ALL, OutboundTag = Global.ProxyTag, Domain = ["geosite:gfw"] } };
        var dns = new SimpleDNSItem { DirectDNS = "223.5.5.5", RemoteDNS = "1.1.1.1", FakeIP = true, GlobalFakeIp = false, DnsPolicy = "full:real.example.com = remote" };

        var (result, _) = Translate(rules, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsOnly }, dns);

        var filter = (List<object>)((Dictionary<object, object>)result["dns"])["fake-ip-filter"];
        await filter.Should().BeEquivalentTo(new List<object> { "geosite:gfw" });
    }

    [Test]
    public async Task Apply_SystemHostsOff_ShouldNotDisableTheFileDefault()
    {
        var content = Load();
        var plan = UnifiedPlanBuilder.Build(new SimpleDNSItem { AddCommonHosts = true, UseSystemHosts = false }, new RoutingItem { RuleSet = "[]" }, Global.AsIs);

        new MihomoTranslator(plan, new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsOnly }, false, new UnifiedDiagnostics()).Apply(content);

        await content.ContainsKey("use-system-hosts").Should().BeFalse();
    }
}
