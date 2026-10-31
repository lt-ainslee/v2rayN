namespace ServiceLib.Tests.CoreConfig.Unified;

public class UnifiedPlanTests
{
    private static UnifiedPlan Build(List<RulesItem> rules, SimpleDNSItem? dns = null, List<RuleSetItem>? ruleSets = null) =>
        UnifiedPlanBuilder.Build(dns ?? new SimpleDNSItem(), new RoutingItem { RuleSet = JsonUtils.Serialize(rules) }, Global.AsIs, ruleSets);

    [Test]
    [Arguments("geosite:cn", EDomainMatchKind.GeoSite, "cn")]
    [Arguments("domain:example.com", EDomainMatchKind.Suffix, "example.com")]
    [Arguments("full:example.com", EDomainMatchKind.Full, "example.com")]
    [Arguments("keyword:exa", EDomainMatchKind.Keyword, "exa")]
    [Arguments("regexp:^a<COMMA>b$", EDomainMatchKind.Regex, "^a,b$")]
    [Arguments("dotless:intra", EDomainMatchKind.Dotless, "intra")]
    [Arguments("ext:my.dat:tag", EDomainMatchKind.External, "my.dat:tag")]
    [Arguments("ruleset:ads", EDomainMatchKind.RuleSet, "ads")]
    [Arguments("twitter", EDomainMatchKind.Plain, "twitter")]
    public async Task ParseDomain_ShouldRecognizePrefixes(string raw, EDomainMatchKind kind, string value)
    {
        var match = UnifiedRuleParser.ParseDomain(raw.Replace("<COMMA>", Global.RoutingRuleComma));
        await match!.Kind.Should().BeEqualTo(kind);
        await match.Value.Should().BeEqualTo(value);
    }

    [Test]
    public async Task ParseIp_ShouldHandleNegationAndKinds()
    {
        var neg = UnifiedRuleParser.ParseIp("!geoip:cn")!;
        await neg.Negated.Should().BeTrue();
        await neg.Kind.Should().BeEqualTo(EIpMatchKind.GeoIP);
        await UnifiedRuleParser.ParseIp("geoip:private")!.Kind.Should().BeEqualTo(EIpMatchKind.Private);
        await UnifiedRuleParser.ParseIp("10.0.0.0/8")!.Kind.Should().BeEqualTo(EIpMatchKind.Cidr);
        await UnifiedRuleParser.ParseDomain("#comment").Should().BeNull();
    }

    [Test]
    public async Task ParsePorts_ShouldParseSinglesAndRanges()
    {
        var ports = UnifiedRuleParser.ParsePorts("80, 443,1000-2000,bad,70000,2000-1000,-1");
        await ports.Count.Should().BeEqualTo(3);
        await ports[2].Should().BeEqualTo(new PortRange(1000, 2000));
    }

    [Test]
    public async Task Build_ShouldDeriveDnsPoliciesAndFinalDirect()
    {
        var plan = Build(
        [
            new() { Id = "a", RuleType = ERuleType.ALL, OutboundTag = Global.DirectTag, Domain = ["geosite:cn"] },
            new() { Id = "b", RuleType = ERuleType.Routing, OutboundTag = Global.ProxyTag, Domain = ["geosite:google"] },
            new() { Id = "c", RuleType = ERuleType.DNS, OutboundTag = Global.BlockTag, Domain = ["geosite:ads"] },
            new() { Id = "off", Enabled = false, OutboundTag = Global.DirectTag, Domain = ["full:x.com"] },
            new() { Id = "final", OutboundTag = Global.DirectTag, Port = "0-65535" },
        ], new SimpleDNSItem { DnsPolicy = "full:corp.example.com = 10.0.0.53\ngeosite:apple-cn = direct" });

        await plan.Rules.Count.Should().BeEqualTo(4);
        await plan.Dns.FinalDirect.Should().BeTrue();
        var policies = plan.Dns.Policies;
        await policies.Count.Should().BeEqualTo(4);
        await policies[0].Servers!.Single().Should().BeEqualTo("10.0.0.53");
        await policies[1].Target.Should().BeEqualTo(EDnsPolicyTarget.Direct);
        await policies[2].Target.Should().BeEqualTo(EDnsPolicyTarget.Direct);
        await policies[3].Target.Should().BeEqualTo(EDnsPolicyTarget.Block);
    }

    [Test]
    public async Task Validate_UnsupportedSelectorValue_ShouldOnlyRemoveThatValue()
    {
        var plan = Build([new() { Id = "r", RuleType = ERuleType.Routing, OutboundTag = Global.ProxyTag, Domain = ["geosite:google@ads", "domain:a.com"] }]);
        var diagnostics = new UnifiedDiagnostics();

        var result = UnifiedPlanValidator.Validate(plan, ECoreType.mihomo, diagnostics);

        await result.Rules.Single().Domains.Single().Raw.Should().BeEqualTo("domain:a.com");
        await diagnostics.Items.Single().Level.Should().BeEqualTo(EDiagnosticLevel.Warning);
    }

    [Test]
    public async Task Validate_AllSelectorsUnsupported_ShouldDropRuleInsteadOfBroadening()
    {
        // Without the domain the rule would degrade to "port 443 -> direct", matching far more traffic.
        var plan = Build([new() { Id = "r", OutboundTag = Global.DirectTag, Domain = ["ext:my.dat:tag"], Port = "443" }]);

        var result = UnifiedPlanValidator.Validate(plan, ECoreType.sing_box, new UnifiedDiagnostics());

        await result.Rules.Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Validate_UnsupportedConstraint_ShouldDropRule()
    {
        var plan = Build([new() { Id = "bt", OutboundTag = Global.DirectTag, Protocol = ["bittorrent"] }]);
        var diagnostics = new UnifiedDiagnostics();

        var mihomo = UnifiedPlanValidator.Validate(plan, ECoreType.mihomo, diagnostics);
        var singbox = UnifiedPlanValidator.Validate(plan, ECoreType.sing_box, new UnifiedDiagnostics());

        await mihomo.Rules.Count.Should().BeEqualTo(0);
        await diagnostics.Items.Single().Message.Should().Contain("protocol");
        await singbox.Rules.Count.Should().BeEqualTo(1);
    }

    [Test]
    public async Task Validate_RuleSetWithoutSourceForCore_ShouldBeRemoved()
    {
        var ruleSets = new List<RuleSetItem> { new() { Name = "ads", Behavior = "domain", SingboxSource = "https://x/ads.srs" } };
        var plan = Build([new() { Id = "r", OutboundTag = Global.BlockTag, Domain = ["ruleset:ads", "full:b.com"] }], ruleSets: ruleSets);

        var singbox = UnifiedPlanValidator.Validate(plan, ECoreType.sing_box, new UnifiedDiagnostics());
        var mihomo = UnifiedPlanValidator.Validate(plan, ECoreType.mihomo, new UnifiedDiagnostics());
        var xray = UnifiedPlanValidator.Validate(plan, ECoreType.Xray, new UnifiedDiagnostics());

        await singbox.Rules.Single().Domains.Count.Should().BeEqualTo(2);
        await mihomo.Rules.Single().Domains.Count.Should().BeEqualTo(1);
        await xray.Rules.Single().Domains.Count.Should().BeEqualTo(1);
    }

    [Test]
    public async Task Validate_Approximation_ShouldKeepValueAndReportInfo()
    {
        var plan = Build([new() { Id = "r", OutboundTag = Global.ProxyTag, Domain = ["dotless:intra"] }]);
        var diagnostics = new UnifiedDiagnostics();

        var result = UnifiedPlanValidator.Validate(plan, ECoreType.sing_box, diagnostics);

        await result.Rules.Single().Domains.Count.Should().BeEqualTo(1);
        await diagnostics.Items.Single().Level.Should().BeEqualTo(EDiagnosticLevel.Info);
    }

    [Test]
    public async Task Validate_DnsPolicies_ShouldDropUnsupportedForMihomo()
    {
        var plan = Build(
        [
            new() { Id = "a", RuleType = ERuleType.DNS, OutboundTag = Global.BlockTag, Domain = ["geosite:ads"] },
            new() { Id = "b", RuleType = ERuleType.DNS, OutboundTag = Global.DirectTag, Domain = ["keyword:corp", "geosite:cn"] },
        ]);

        var result = UnifiedPlanValidator.Validate(plan, ECoreType.mihomo, new UnifiedDiagnostics());

        await result.Dns.Policies.Count.Should().BeEqualTo(1);
        await result.Dns.Policies[0].Domains.Single().Raw.Should().BeEqualTo("geosite:cn");
    }

    [Test]
    public async Task Validate_DnsDiagnostics_ShouldOnlyReportExplicitPolicies()
    {
        var plan = Build(
            [new() { Id = "r", RuleType = ERuleType.ALL, OutboundTag = Global.BlockTag, Domain = ["keyword:ads"] }],
            new SimpleDNSItem { DnsPolicy = "keyword:corp = direct\ngeosite:ads = block" });
        var diagnostics = new UnifiedDiagnostics();

        UnifiedPlanValidator.Validate(plan, ECoreType.mihomo, diagnostics);

        var dnsMessages = diagnostics.Items.Where(d => d.Subject == "DNS").Select(d => d.Message).ToList();
        await dnsMessages.Count.Should().BeEqualTo(2);
        await dnsMessages.Any(m => m.Contains("keyword:corp")).Should().BeTrue();
        await dnsMessages.Any(m => m.Contains("geosite:ads")).Should().BeTrue();
    }

    [Test]
    public async Task JsonMerge_ShouldDeepMergeAndKeepUnmodeledKeys()
    {
        var rule = new Rule4Sbox { outbound = "proxy", domain = ["a.com"] };

        var merged = UnifiedJsonMerge.Merge(rule, """{"invert":true,"rule_set_ip_cidr_match_source":true,"custom_key":1}""")!;
        var json = JsonUtils.Serialize(merged, false);

        await merged.invert.Should().BeEqualTo(true);
        await merged.outbound.Should().BeEqualTo("proxy");
        await json.Should().Contain("\"custom_key\":1");
    }

    [Test]
    public async Task PreviewCompose_ShouldListDistinctDiagnosticsBeforeBody()
    {
        var diagnostics = new UnifiedDiagnostics();
        diagnostics.Warn(ECoreType.mihomo, "r", "dropped");
        diagnostics.Warn(ECoreType.mihomo, "r", "dropped");
        diagnostics.Info(ECoreType.mihomo, "DNS", "approx");

        var text = UnifiedPreviewService.Compose(diagnostics, "body");

        await text.ReplaceLineEndings("\n").Should().BeEqualTo("! dropped\n~ approx\n\nbody");
        await UnifiedPreviewService.Compose(new UnifiedDiagnostics(), "x").Should().StartWith(ResUI.MsgUnifiedPreviewNoIssues);
    }

    [Test]
    [Arguments("192.168.1.0/24", true)]
    [Arguments("10.0.0.1", true)]
    [Arguments("fd00::/8", true)]
    [Arguments("192.168.1.0/33", false)]
    [Arguments("fd00::/129", false)]
    [Arguments("foo", false)]
    [Arguments("lan.example.com", false)]
    [Arguments("192.168.1", false)]
    [Arguments("10", false)]
    [Arguments("0x7f.0.0.1", false)]
    [Arguments("fe80::1%eth0", false)]
    public async Task IsValidCidr_ShouldCheckAddressAndPrefix(string value, bool expected)
    {
        await UnifiedRuleParser.IsValidCidr(value).Should().BeEqualTo(expected);
    }

    [Test]
    public async Task Validate_AvailableRuleSets_ShouldIncludeSetsFromOtherSources()
    {
        // e.g. a tag from the routing profile's custom sing-box rule set file or a local srss/*.srs file
        var plan = Build([new() { Id = "r", RuleType = ERuleType.Routing, OutboundTag = Global.BlockTag, Domain = ["ruleset:myads"] }]);

        var result = UnifiedPlanValidator.Validate(plan, ECoreType.sing_box, new UnifiedDiagnostics(), ["myads"]);

        await result.Rules.Single().Domains.Single().Raw.Should().BeEqualTo("ruleset:myads");
    }

    [Test]
    public async Task IsFinalDirect_ShouldIgnoreDisabledAndRestrictedRules()
    {
        RulesItem catchAll = new() { OutboundTag = Global.DirectTag, Port = "0-65535" };
        await UnifiedPlanBuilder.IsFinalDirect([catchAll]).Should().BeTrue();
        await UnifiedPlanBuilder.IsFinalDirect([catchAll, new() { Enabled = false, OutboundTag = Global.ProxyTag, Port = "0-65535" }]).Should().BeTrue();
        await UnifiedPlanBuilder.IsFinalDirect([new() { OutboundTag = Global.DirectTag, SourceIp = ["192.168.1.50/32"] }]).Should().BeFalse();
        await UnifiedPlanBuilder.IsFinalDirect([new() { OutboundTag = Global.DirectTag, Protocol = ["bittorrent"] }]).Should().BeFalse();
    }
}
