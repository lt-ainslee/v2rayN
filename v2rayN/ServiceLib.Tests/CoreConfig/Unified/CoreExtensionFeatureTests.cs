namespace ServiceLib.Tests.CoreConfig.Unified;

/// <summary>
/// The per-core options added from the core documentation, the Clash rule checks and the Mixin merge.
/// </summary>
// These tests replace the global AppManager config, so they must not run concurrently.
[NotInParallel(CoreConfigTestFactory.AppManagerConfigKey)]
public class CoreExtensionFeatureTests
{
    #region Clash rule syntax

    [Test]
    [Arguments("DOMAIN-SUFFIX,google.com,Proxy")]
    [Arguments("domain-wildcard,*.example.com,{target}")]
    [Arguments("IP-CIDR,10.0.0.0/8,DIRECT,no-resolve")]
    [Arguments("MATCH,Proxy")]
    [Arguments("RULE-SET,my-set,Proxy")]
    [Arguments("AND,((NETWORK,UDP),(DST-PORT,443)),REJECT")]
    [Arguments("NOT,((DOMAIN,example.com)),DIRECT")]
    [Arguments("OR,((AND,((NETWORK,UDP),(DST-PORT,443))),(DOMAIN,x.com)),REJECT")]
    public async Task IsValidRuleLine_ShouldAcceptMihomoRules(string line)
    {
        await ClashSyntax.IsValidRuleLine(line).Should().BeTrue();
    }

    [Test]
    [Arguments("DOMAIN-SUFFIX,google.com")]
    [Arguments("FOO,bar,Proxy")]
    [Arguments("MATCH,Proxy,extra")]
    [Arguments("AND,(NETWORK,UDP),REJECT")]
    [Arguments("AND,((NETWORK,UDP),(BOGUS,1)),REJECT")]
    [Arguments("AND,((MATCH,x)),REJECT")]
    public async Task IsValidRuleLine_ShouldRejectMalformedRules(string line)
    {
        await ClashSyntax.IsValidRuleLine(line).Should().BeFalse();
    }

    [Test]
    public async Task GetRuleSetReferences_ShouldFindNestedReferences()
    {
        var names = ClashSyntax.GetRuleSetReferences("AND,((RULE-SET,ads),(NETWORK,TCP)),REJECT").ToList();
        await names.Single().Should().BeEqualTo("ads");
        await ClashSyntax.GetRuleSetReferences("RULE-SET,streaming,Proxy").Single().Should().BeEqualTo("streaming");
        await ClashSyntax.GetRuleSetReferences("DOMAIN,rule-set.example.com,Proxy").Any().Should().BeFalse();
    }

    #endregion Clash rule syntax

    #region mihomo

    private const string SourceYaml = """
        proxies:
          - name: HK-01
            type: ss
            server: hk.example.com
            port: 443
            cipher: aes-128-gcm
            password: pass
        proxy-groups:
          - name: Proxy
            type: select
            proxies: [HK-01]
        rule-providers:
          file-set:
            type: http
            behavior: domain
            url: https://example.com/file-set.yaml
        rules:
          - MATCH,DIRECT
        """;

    private static (Dictionary<string, object> Content, UnifiedDiagnostics Diagnostics) Translate(MihomoExtension ext, List<RulesItem>? rules = null)
    {
        var ruleSets = new List<RuleSetItem>
        {
            new() { Name = "declared", Behavior = "domain", MihomoSource = "https://example.com/declared.mrs" },
            new() { Name = "singbox-only", Behavior = "domain", SingboxSource = "https://example.com/s.srs" },
        };
        var plan = UnifiedPlanBuilder.Build(new SimpleDNSItem { DirectDNS = "223.5.5.5", RemoteDNS = "1.1.1.1" },
            new RoutingItem { RuleSet = JsonUtils.Serialize(rules ?? []) }, Global.AsIs, ruleSets);
        var content = YamlUtils.FromYaml<Dictionary<string, object>>(SourceYaml);
        var validated = UnifiedPlanValidator.Validate(plan, ECoreType.mihomo, new UnifiedDiagnostics(),
            CoreConfigClashService.GetAvailableRuleSetNames(new Config { RuleSetItems = ruleSets }, content));
        var diagnostics = new UnifiedDiagnostics();
        new MihomoTranslator(validated, ext, false, diagnostics).Apply(content);
        return (YamlUtils.FromYaml<Dictionary<string, object>>(YamlUtils.ToYaml(content)), diagnostics);
    }

    private static List<string> Rules(Dictionary<string, object> content) =>
        ((List<object>)content["rules"]).Select(t => t.ToString()!).ToList();

    [Test]
    public async Task ExtraRules_ShouldAddRuleProvidersAndSkipUnloadableLines()
    {
        var (content, diagnostics) = Translate(new MihomoExtension
        {
            UnifiedMode = EMihomoUnifiedMode.DnsAndRules,
            ExtraRules = string.Join("\n",
                "RULE-SET,declared,Proxy",
                "RULE-SET,file-set,DIRECT",
                "RULE-SET,singbox-only,Proxy",
                "RULE-SET,unknown,Proxy",
                "DOMAIN,missing-policy.example.com,Nowhere",
                "BOGUS,line,Proxy"),
        });

        var rules = Rules(content);
        await rules.Should().Contain("RULE-SET,declared,Proxy");
        await rules.Should().Contain("RULE-SET,file-set,DIRECT");
        await rules.Any(r => r.Contains("singbox-only") || r.Contains("unknown") || r.Contains("Nowhere") || r.StartsWith("BOGUS")).Should().BeFalse();
        var providers = (Dictionary<object, object>)content["rule-providers"];
        await providers.ContainsKey("declared").Should().BeTrue();
        await providers.ContainsKey("file-set").Should().BeTrue();
        await diagnostics.Items.Count.Should().BeEqualTo(4);
    }

    [Test]
    public async Task PerRuleMihomoRules_ShouldTrackRuleSetsAndDropMissingPolicies()
    {
        var (content, _) = Translate(new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsAndReplaceRules },
        [
            new()
            {
                Id = "r1", Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag,
                Domain = ["domain:example.com"],
                CoreExt = new RuleCoreExtension { MihomoRules = "AND,((RULE-SET,declared),(NETWORK,TCP)),{target}\nDOMAIN,x.example.com,Nowhere" },
            },
        ]);

        var rules = Rules(content);
        await rules[0].Should().BeEqualTo("AND,((RULE-SET,declared),(NETWORK,TCP)),DIRECT");
        await rules.Any(r => r.Contains("Nowhere")).Should().BeFalse();
        await ((Dictionary<object, object>)content["rule-providers"]).ContainsKey("declared").Should().BeTrue();
    }

    [Test]
    public async Task DnsCacheAlgorithm_ShouldBeWritten()
    {
        var (content, _) = Translate(new MihomoExtension { UnifiedMode = EMihomoUnifiedMode.DnsOnly, DnsCacheAlgorithm = "arc" });
        var dns = (Dictionary<object, object>)content["dns"];
        await dns["cache-algorithm"].ToString().Should().BeEqualTo("arc");
    }

    [Test]
    public async Task ApplyGeneralOptions_ShouldOnlyTouchSetOptions()
    {
        var content = new Dictionary<string, object>
        {
            ["unified-delay"] = false,
            ["sniffer"] = new Dictionary<object, object> { ["enable"] = false, ["parse-pure-ip"] = false },
        };
        CoreConfigClashService.ApplyGeneralOptions(content, new MihomoExtension
        {
            TcpConcurrent = true,
            FindProcessMode = "off",
            KeepAliveInterval = 15,
            SnifferEnabled = true,
        });

        await ((bool)content["unified-delay"]).Should().BeFalse();
        await ((bool)content["tcp-concurrent"]).Should().BeTrue();
        await content["find-process-mode"].ToString().Should().BeEqualTo("off");
        await ((int)content["keep-alive-interval"]).Should().BeEqualTo(15);
        var sniffer = (Dictionary<object, object>)content["sniffer"];
        await ((bool)sniffer["enable"]).Should().BeTrue();
        // The file's own sniffer settings are kept.
        await ((bool)sniffer["parse-pure-ip"]).Should().BeFalse();
    }

    [Test]
    public async Task ApplyGeneralOptions_ShouldCreateSnifferWhenMissing()
    {
        var content = new Dictionary<string, object>();
        CoreConfigClashService.ApplyGeneralOptions(content, new MihomoExtension { SnifferEnabled = true });
        var sniff = (Dictionary<object, object>)((Dictionary<object, object>)content["sniffer"])["sniff"];
        await sniff.ContainsKey("TLS").Should().BeTrue();

        var untouched = new Dictionary<string, object>();
        CoreConfigClashService.ApplyGeneralOptions(untouched, new MihomoExtension { SnifferEnabled = false });
        await untouched.ContainsKey("sniffer").Should().BeFalse();
    }

    [Test]
    public async Task MixinMerge_ShouldRemoveGroupsByNameAndSkipNonLists()
    {
        var content = new Dictionary<string, object>
        {
            ["proxy-groups"] = new List<object>
            {
                new Dictionary<object, object> { ["name"] = "Ads", ["type"] = "select" },
                new Dictionary<object, object> { ["name"] = "Proxy", ["type"] = "select" },
            },
            ["rules"] = new List<object> { "DOMAIN,a.com,Proxy", "MATCH,Proxy" },
            ["dns"] = new Dictionary<object, object>(),
        };

        CoreConfigClashService.ModifyContentMerge(content, "removed-proxy-groups", new List<object> { "Ads" });
        CoreConfigClashService.ModifyContentMerge(content, "prepend-rules", new List<object> { "DOMAIN,b.com,DIRECT", "DOMAIN,c.com,DIRECT" });
        CoreConfigClashService.ModifyContentMerge(content, "removed-rules", new List<object> { "DOMAIN,a.com" });
        CoreConfigClashService.ModifyContentMerge(content, "append-dns", new List<object> { "x" });
        CoreConfigClashService.ModifyContentMerge(content, "append-hosts-list", "single");
        CoreConfigClashService.ModifyContentMerge(content, "removed-missing", new List<object> { "x" });

        var groups = (List<object>)content["proxy-groups"];
        await groups.Count.Should().BeEqualTo(1);
        var rules = ((List<object>)content["rules"]).Select(t => t.ToString()).ToList();
        await string.Join("|", rules).Should().BeEqualTo("DOMAIN,b.com,DIRECT|DOMAIN,c.com,DIRECT|MATCH,Proxy");
        await (content["dns"] is Dictionary<object, object>).Should().BeTrue();
        await ((List<object>)content["hosts-list"]).Single().Should().BeEqualTo("single");
        await content.ContainsKey("missing").Should().BeFalse();
    }

    #endregion mihomo

    #region Validator

    [Test]
    public async Task Validator_ShouldAcceptDefaults()
    {
        await CoreExtensionValidator.Validate(new CoreExtensionItem()).Count.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Validator_ShouldReportEveryInvalidField()
    {
        var errors = CoreExtensionValidator.Validate(new CoreExtensionItem
        {
            Xray = new XrayExtension { DnsQueryStrategy = "UseIPv5", DnsClientIp = "not-an-ip", DnsServeExpiredTtl = -1 },
            Singbox = new SingboxExtension { DnsClientSubnet = "1.2.3.0/33", DnsCacheCapacity = 10, RouteDefaultMark = -1 },
            Mihomo = new MihomoExtension { DnsCacheAlgorithm = "fifo", FindProcessMode = "sometimes", KeepAliveInterval = -5, ExtraRules = "DOMAIN,a.com" },
        });
        await errors.Count.Should().BeEqualTo(10);
    }

    [Test]
    [Arguments("1.2.3.4", true)]
    [Arguments("1.2.3.0/24", true)]
    [Arguments("2001:db8::/56", true)]
    [Arguments("2001:db8::/129", false)]
    [Arguments("1.2.3.0/x", false)]
    public async Task IsIpOrCidr(string value, bool expected)
    {
        await CoreExtensionValidator.IsIpOrCidr(value).Should().BeEqualTo(expected);
    }

    #endregion Validator

    #region Xray / sing-box generation

    private static CoreConfigContext CreateContext(ECoreType coreType, bool serveStale)
    {
        var config = CoreConfigTestFactory.CreateConfig(coreType);
        config.SimpleDNSItem.DirectDNS = "223.5.5.5";
        config.SimpleDNSItem.RemoteDNS = "https://1.1.1.1/dns-query";
        config.SimpleDNSItem.ServeStale = serveStale;
        config.CoreBasicItem.EnableCacheFile4Sbox = true;
        config.CoreExtensionItem = new CoreExtensionItem
        {
            Xray = new XrayExtension
            {
                DnsQueryStrategy = "UseSystem",
                DnsDisableFallback = true,
                DnsDisableFallbackIfMatch = true,
                DnsClientIp = "1.2.3.4",
                DnsServeExpiredTtl = 3600,
            },
            Singbox = new SingboxExtension
            {
                DnsDisableExpire = true,
                DnsCacheCapacity = 4096,
                DnsReverseMapping = true,
                RouteFindProcess = true,
                CacheFileStoreRdrc = true,
            },
        };
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateSocksNode(coreType, "n-main", "main");
        return CoreConfigTestFactory.CreateContext(config, node, coreType);
    }

    [Test]
    public async Task Xray_ShouldApplyDnsExtensions()
    {
        var result = new CoreConfigV2rayService(CreateContext(ECoreType.Xray, serveStale: true)).GenerateClientConfigContent();
        await result.Success.Should().BeTrue().Because($"ret msg: {result.Msg}");
        var dns = JsonNode.Parse(result.Data!.ToString()!)!["dns"]!;
        await dns["queryStrategy"]!.ToString().Should().BeEqualTo("UseSystem");
        await dns["disableFallback"]!.GetValue<bool>().Should().BeTrue();
        await dns["disableFallbackIfMatch"]!.GetValue<bool>().Should().BeTrue();
        await dns["clientIp"]!.ToString().Should().BeEqualTo("1.2.3.4");
        await dns["serveExpiredTTL"]!.GetValue<int>().Should().BeEqualTo(3600);
    }

    [Test]
    public async Task Xray_ServeExpiredTtl_ShouldNeedServeStale()
    {
        var result = new CoreConfigV2rayService(CreateContext(ECoreType.Xray, serveStale: false)).GenerateClientConfigContent();
        await result.Success.Should().BeTrue().Because($"ret msg: {result.Msg}");
        var dns = JsonNode.Parse(result.Data!.ToString()!)!["dns"]!;
        await (dns["serveExpiredTTL"] == null).Should().BeTrue();
    }

    [Test]
    public async Task Singbox_ShouldApplyDnsRouteAndCacheExtensions()
    {
        var result = new CoreConfigSingboxService(CreateContext(ECoreType.sing_box, serveStale: false)).GenerateClientConfigContent();
        await result.Success.Should().BeTrue().Because($"ret msg: {result.Msg}");
        var json = JsonNode.Parse(result.Data!.ToString()!)!;
        await json["dns"]!["disable_expire"]!.GetValue<bool>().Should().BeTrue();
        await json["dns"]!["cache_capacity"]!.GetValue<int>().Should().BeEqualTo(4096);
        await json["dns"]!["reverse_mapping"]!.GetValue<bool>().Should().BeTrue();
        await json["route"]!["find_process"]!.GetValue<bool>().Should().BeTrue();
        await json["experimental"]!["cache_file"]!["store_rdrc"]!.GetValue<bool>().Should().BeTrue();
    }

    [Test]
    public async Task Singbox_DisableExpire_ShouldYieldToServeStale()
    {
        var result = new CoreConfigSingboxService(CreateContext(ECoreType.sing_box, serveStale: true)).GenerateClientConfigContent();
        await result.Success.Should().BeTrue().Because($"ret msg: {result.Msg}");
        var dns = JsonNode.Parse(result.Data!.ToString()!)!["dns"]!;
        await dns["optimistic"]!.GetValue<bool>().Should().BeTrue();
        await (dns["disable_expire"] == null).Should().BeTrue();
    }

    #endregion Xray / sing-box generation
}
