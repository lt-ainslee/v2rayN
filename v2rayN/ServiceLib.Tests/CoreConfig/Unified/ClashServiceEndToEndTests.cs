namespace ServiceLib.Tests.CoreConfig.Unified;

/// <summary>
/// Runs the whole Clash path: configuration file on disk -> CoreConfigClashService -> generated YAML.
/// </summary>
// These tests replace the global AppManager config, so they must not run concurrently.
[NotInParallel(CoreConfigTestFactory.AppManagerConfigKey)]
public class ClashServiceEndToEndTests
{
    private const string SourceYaml = """
        mixed-port: 7890
        secret: should-be-removed
        dns:
          enable: true
          nameserver: [8.8.8.8]
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
            proxies: [HK-01, DIRECT]
        rules:
          - MATCH,Proxy
        """;

    private static async Task<Dictionary<string, object>> Generate(EMihomoUnifiedMode mode)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"v2rayn-clash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "source.yaml");
            var output = Path.Combine(dir, "output.yaml");
            await File.WriteAllTextAsync(source, SourceYaml);

            var config = CoreConfigTestFactory.CreateConfig(ECoreType.mihomo);
            config.SimpleDNSItem.DirectDNS = "223.5.5.5";
            config.SimpleDNSItem.RemoteDNS = "https://1.1.1.1/dns-query";
            config.CoreExtensionItem = new CoreExtensionItem { Mihomo = new MihomoExtension { UnifiedMode = mode } };
            CoreConfigTestFactory.BindAppManagerConfig(config);

            var node = new ProfileItem
            {
                IndexId = "clash-node",
                ConfigType = EConfigType.Custom,
                CoreType = ECoreType.mihomo,
                Remarks = "clash",
                Address = source,
            };
            var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.mihomo) with
            {
                RoutingItem = new RoutingItem
                {
                    Id = "r",
                    RuleSet = JsonUtils.Serialize(new List<RulesItem>
                    {
                        new() { Id = "cn", Enabled = true, RuleType = ERuleType.ALL, OutboundTag = Global.DirectTag, Domain = ["geosite:cn"] },
                    }),
                },
            };

            var result = await new CoreConfigClashService(config, false, context).GenerateClientCustomConfig(node, output);
            await result.Success.Should().BeTrue().Because(result.Msg);
            return YamlUtils.FromYaml<Dictionary<string, object>>(await File.ReadAllTextAsync(output));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public async Task Generate_Off_ShouldKeepDnsAndRules()
    {
        var result = await Generate(EMihomoUnifiedMode.Off);

        await ((List<object>)((Dictionary<object, object>)result["dns"])["nameserver"]).Single().Should().BeEqualTo("8.8.8.8");
        await ((List<object>)result["rules"]).Single().Should().BeEqualTo("MATCH,Proxy");
        await result.ContainsKey("secret").Should().BeFalse();
    }

    [Test]
    public async Task Generate_DnsAndRules_ShouldApplySharedSettings()
    {
        var result = await Generate(EMihomoUnifiedMode.DnsAndRules);

        var dns = (Dictionary<object, object>)result["dns"];
        await ((List<object>)dns["nameserver"]).Single().Should().BeEqualTo("https://1.1.1.1/dns-query");
        await ((Dictionary<object, object>)dns["nameserver-policy"])["geosite:cn"].Should().BeEqualTo("223.5.5.5");
        var rules = ((List<object>)result["rules"]).Select(t => t.ToString()).ToList();
        await rules.Should().BeEquivalentTo(new List<string?> { "GEOSITE,cn,DIRECT", "MATCH,Proxy" });
    }
}
