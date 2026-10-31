namespace ServiceLib.Tests.CoreConfig.Unified;

/// <summary>
/// End-to-end checks that the shared settings (rules, explicit DNS policy, rule sets, extensions)
/// reach the generated Xray and sing-box configurations.
/// </summary>
// These tests replace the global AppManager config, so they must not run concurrently.
[NotInParallel(CoreConfigTestFactory.AppManagerConfigKey)]
public class UnifiedGenerationTests
{
    private static List<RulesItem> Rules() =>
    [
        new()
        {
            Id = "src", Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag,
            Domain = ["domain:lan.example.com"], SourceIp = ["192.168.1.0/24"], SourcePort = "5000-6000",
            CoreExt = new RuleCoreExtension
            {
                XrayJson = """{"attrs":{":method":"GET"}}""",
                SingboxJson = """{"rule_set_ip_cidr_match_source":true,"invert":false}""",
            },
        },
        new()
        {
            Id = "ads", Enabled = true, RuleType = ERuleType.ALL, OutboundTag = Global.BlockTag,
            Domain = ["ruleset:ads", "full:ads.example.com"],
        },
        new()
        {
            Id = "only-ruleset", Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag,
            Domain = ["ruleset:ads"], Port = "443",
        },
        new()
        {
            Id = "undeclared", Enabled = true, RuleType = ERuleType.ALL, OutboundTag = Global.DirectTag,
            Domain = ["ruleset:undeclared"],
        },
        new()
        {
            Id = "source-only", Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag,
            SourceIp = ["192.168.1.50/32"],
        },
        new()
        {
            Id = "routing-only-set", Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.ProxyTag,
            Ip = ["ruleset:streaming-ip"],
        },
    ];

    private static CoreConfigContext CreateContext(ECoreType coreType)
    {
        var config = CoreConfigTestFactory.CreateConfig(coreType);
        config.SimpleDNSItem.DirectDNS = "223.5.5.5";
        config.SimpleDNSItem.RemoteDNS = "https://1.1.1.1/dns-query";
        config.SimpleDNSItem.DnsPolicy = "full:corp.example.com, domain:intra.example.com = 10.0.0.53\ngeosite:apple-cn = direct";
        config.RuleSetItems =
        [
            new RuleSetItem { Name = "ads", Behavior = "domain", SingboxSource = "https://example.com/ads.srs", UpdateInterval = 3600 },
            new RuleSetItem { Name = "streaming-ip", Behavior = "ipcidr", SingboxSource = "https://example.com/streaming-ip.srs" },
        ];
        config.CoreExtensionItem = new CoreExtensionItem
        {
            Xray = new XrayExtension { DnsQueryStrategy = "UseIPv6", DnsDisableCache = true },
            Singbox = new SingboxExtension { DnsClientSubnet = "1.2.3.0/24", DnsIndependentCache = true },
        };
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var node = CoreConfigTestFactory.CreateSocksNode(coreType, "n-main", "main");
        return CoreConfigTestFactory.CreateContext(config, node, coreType) with
        {
            RoutingItem = new RoutingItem
            {
                Id = "r-unified",
                Remarks = "unified",
                RuleSet = JsonUtils.Serialize(Rules()),
                DomainStrategy = string.Empty,
                DomainStrategy4Singbox = string.Empty,
            },
        };
    }

    [Test]
    public async Task Xray_ShouldApplySourceExtensionsAndDnsPolicy()
    {
        var result = new CoreConfigV2rayService(CreateContext(ECoreType.Xray)).GenerateClientConfigContent();
        await result.Success.Should().BeTrue().Because($"ret msg: {result.Msg}");
        var json = JsonNode.Parse(result.Data!.ToString()!)!;
        var rules = json["routing"]!["rules"]!.AsArray();

        var srcRule = rules.Single(r => r!["domain"]?.AsArray().Any(d => d!.ToString() == "domain:lan.example.com") == true)!;
        await srcRule["source"]![0]!.ToString().Should().BeEqualTo("192.168.1.0/24");
        await srcRule["sourcePort"]!.ToString().Should().BeEqualTo("5000-6000");
        await srcRule["attrs"]![":method"]!.ToString().Should().BeEqualTo("GET");

        // Xray has no rule sets: the value is removed, and the rule whose only selector was a rule set is dropped.
        var adsRule = rules.Single(r => r!["domain"]?.AsArray().Any(d => d!.ToString() == "full:ads.example.com") == true)!;
        await adsRule["domain"]!.AsArray().Count.Should().BeEqualTo(1);
        await rules.Any(r => r!["port"]?.ToString() == "443" && r["outboundTag"]?.ToString() == Global.DirectTag).Should().BeFalse();

        await rules.Any(r => r!["source"]?[0]?.ToString() == "192.168.1.50/32" && r["domain"] == null && r["ip"] == null).Should().BeTrue();

        var dns = json["dns"]!;
        await dns["queryStrategy"]!.ToString().Should().BeEqualTo("UseIPv6");
        await dns["disableCache"]!.GetValue<bool>().Should().BeTrue();
        var corpServer = dns["servers"]!.AsArray().OfType<JsonObject>().Single(s => s["address"]?.ToString() == "10.0.0.53");
        await corpServer["domains"]!.AsArray().Select(d => d!.ToString()).Should().Contain("domain:intra.example.com");
        await corpServer["tag"]!.ToString().Should().StartWith(Global.DirectDnsTag);
    }

    [Test]
    public async Task Singbox_ShouldApplySourceExtensionsRuleSetsAndDnsPolicy()
    {
        var result = new CoreConfigSingboxService(CreateContext(ECoreType.sing_box)).GenerateClientConfigContent();
        await result.Success.Should().BeTrue().Because($"ret msg: {result.Msg}");
        var json = JsonNode.Parse(result.Data!.ToString()!)!;
        var rules = json["route"]!["rules"]!.AsArray().OfType<JsonObject>().ToList();

        var srcRule = rules.Single(r => r["domain_suffix"]?.AsArray().Any(d => d!.ToString() == "lan.example.com") == true);
        await srcRule["source_ip_cidr"]![0]!.ToString().Should().BeEqualTo("192.168.1.0/24");
        await srcRule["source_port_range"]![0]!.ToString().Should().BeEqualTo("5000:6000");
        await srcRule["rule_set_ip_cidr_match_source"]!.GetValue<bool>().Should().BeTrue();

        var adsRule = rules.Single(r => r["domain"]?.AsArray().Any(d => d!.ToString() == "ads.example.com") == true);
        await adsRule["rule_set"]![0]!.ToString().Should().BeEqualTo("ads");
        await rules.Any(r => r["rule_set"]?.AsArray().Any(t => t!.ToString() == "ads") == true && r["port"] != null).Should().BeTrue();

        await rules.Any(r => r["source_ip_cidr"]?[0]?.ToString() == "192.168.1.50/32").Should().BeTrue();
        // A rule set used only by routing rules still needs its definition.
        await json["route"]!["rule_set"]!.AsArray().Any(r => r!["tag"]!.ToString() == "streaming-ip").Should().BeTrue();
        // An undeclared rule set must not turn into a (non-existent) geosite download.
        await json["route"]!["rule_set"]!.AsArray().Any(r => r!["tag"]!.ToString() == "undeclared").Should().BeFalse();
        var ruleSet = json["route"]!["rule_set"]!.AsArray().OfType<JsonObject>().Single(r => r["tag"]!.ToString() == "ads");
        await ruleSet["url"]!.ToString().Should().BeEqualTo("https://example.com/ads.srs");
        await ruleSet["update_interval"]!.ToString().Should().BeEqualTo("60m");

        var dns = json["dns"]!;
        await dns["client_subnet"]!.ToString().Should().BeEqualTo("1.2.3.0/24");
        await dns["independent_cache"]!.GetValue<bool>().Should().BeTrue();
        var policyServer = dns["servers"]!.AsArray().OfType<JsonObject>().Single(s => s["tag"]?.ToString() == "policy-0-1");
        await policyServer["server"]!.ToString().Should().BeEqualTo("10.0.0.53");
        var dnsRules = dns["rules"]!.AsArray().OfType<JsonObject>().ToList();
        await dnsRules.Any(r => r["server"]?.ToString() == "policy-0-1"
            && r["domain"]?.AsArray().Any(d => d!.ToString() == "corp.example.com") == true).Should().BeTrue();
        await dnsRules.Any(r => r["action"]?.ToString() == "predefined" && r["rule_set"]?.AsArray().Any(t => t!.ToString() == "ads") == true).Should().BeTrue();
    }
}
