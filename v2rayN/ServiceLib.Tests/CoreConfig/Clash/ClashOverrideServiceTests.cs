namespace ServiceLib.Tests.CoreConfig.Clash;

public class ClashOverrideServiceTests
{
    private const string SourceYaml = """
        mixed-port: 7890
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
          - name: Proxy
            type: select
            proxies:
              - HK-01
              - JP-01
        rules:
          - DOMAIN-SUFFIX,example.com,Proxy
          - MATCH,DIRECT
        """;

    private static Dictionary<string, object> Load() => YamlUtils.FromYaml<Dictionary<string, object>>(SourceYaml);

    private static Dictionary<string, object> RoundTrip(Dictionary<string, object> content) =>
        YamlUtils.FromYaml<Dictionary<string, object>>(YamlUtils.ToYaml(content));

    [Test]
    public async Task Apply_DisabledOverrides_ShouldKeepContent()
    {
        var content = Load();
        ClashOverrideService.Apply(content, new ClashOverrideItem { Nameserver = "1.1.1.1", Rules = "MATCH,Proxy" }, false);

        var dns = (Dictionary<object, object>)content["dns"];
        await ((List<object>)dns["nameserver"]).Should().BeEquivalentTo(new List<object> { "8.8.8.8" });
        await ((List<object>)content["rules"]).Count.Should().BeEqualTo(2);
    }

    [Test]
    public async Task Apply_DnsOverride_ShouldReplaceDnsAndKeepListen()
    {
        var content = Load();
        var item = new ClashOverrideItem
        {
            EnableDnsOverride = true,
            EnhancedMode = "fake-ip",
            FakeIPRange = "198.18.0.1/16",
            FakeIPFilter = "+.lan\n# comment\n*.local",
            DefaultNameserver = "223.5.5.5, 119.29.29.29",
            Nameserver = "https://dns.alidns.com/dns-query",
            Fallback = "https://1.1.1.1/dns-query",
            FallbackFilterGeoIP = true,
            FallbackFilterGeoIPCode = "CN",
            RespectRules = true,
            NameserverPolicy = "geosite:cn = 223.5.5.5\n+.google.com=https://8.8.8.8/dns-query, tls://1.1.1.1",
        };

        ClashOverrideService.Apply(content, item, true);
        var dns = (Dictionary<object, object>)RoundTrip(content)["dns"];

        await dns["listen"].Should().BeEqualTo("0.0.0.0:1053");
        await dns["enhanced-mode"].Should().BeEqualTo("fake-ip");
        await dns["fake-ip-range"].Should().BeEqualTo("198.18.0.1/16");
        await dns["ipv6"].Should().BeEqualTo("true");
        await dns["respect-rules"].Should().BeEqualTo("true");
        await ((List<object>)dns["fake-ip-filter"]).Should().BeEquivalentTo(new List<object> { "+.lan", "*.local" });
        await ((List<object>)dns["default-nameserver"]).Should().BeEquivalentTo(new List<object> { "223.5.5.5", "119.29.29.29" });
        // respect-rules requires proxy-server-nameserver, so it falls back to nameserver
        await ((List<object>)dns["proxy-server-nameserver"]).Should().BeEquivalentTo(new List<object> { "https://dns.alidns.com/dns-query" });
        var filter = (Dictionary<object, object>)dns["fallback-filter"];
        await filter["geoip-code"].Should().BeEqualTo("CN");
        var policy = (Dictionary<object, object>)dns["nameserver-policy"];
        await policy["geosite:cn"].Should().BeEqualTo("223.5.5.5");
        await ((List<object>)policy["+.google.com"]).Count.Should().BeEqualTo(2);
    }

    [Test]
    public async Task Apply_DnsOverrideWithoutNameserver_ShouldKeepOriginalDns()
    {
        var content = Load();
        ClashOverrideService.Apply(content, new ClashOverrideItem { EnableDnsOverride = true }, false);

        var dns = (Dictionary<object, object>)content["dns"];
        await ((List<object>)dns["nameserver"]).Should().BeEquivalentTo(new List<object> { "8.8.8.8" });
    }

    [Test]
    public async Task Apply_PolicyOverride_ShouldMergeGroupsProvidersAndRules()
    {
        var content = Load();
        var item = new ClashOverrideItem
        {
            EnablePolicyOverride = true,
            ProxyGroups = """
                Proxy = select, Auto, HK-01, JP-01, Missing
                Auto = url-test, HK-01, JP-01, interval=600, tolerance=50
                HK = select, include-all=true, filter="(?i)hk|港,香港"
                """,
            RuleProviders = """
                ads = domain, https://example.com/ads.mrs, interval=3600
                local = classical, ./rules/local.yaml
                """,
            Rules = """
                RULE-SET,ads,REJECT
                DOMAIN-SUFFIX,google.com,Auto
                AND,((DOMAIN,a.com),(NETWORK,UDP)),HK
                DOMAIN,unknown.com,NotExist
                """,
        };

        ClashOverrideService.Apply(content, item, false);
        var result = RoundTrip(content);

        var groups = ((List<object>)result["proxy-groups"]).Cast<Dictionary<object, object>>().ToList();
        await groups.Count.Should().BeEqualTo(3);
        // existing group is replaced in place, unknown member removed
        await groups[0]["name"].Should().BeEqualTo("Proxy");
        await ((List<object>)groups[0]["proxies"]).Should().BeEquivalentTo(new List<object> { "Auto", "HK-01", "JP-01" });
        await groups[1]["interval"].Should().BeEqualTo("600");
        await groups[1]["url"].Should().BeEqualTo("https://www.gstatic.com/generate_204");
        await groups[2]["filter"].Should().BeEqualTo("(?i)hk|港,香港");
        await groups[2].ContainsKey("proxies").Should().BeFalse();

        var providers = (Dictionary<object, object>)result["rule-providers"];
        var ads = (Dictionary<object, object>)providers["ads"];
        await ads["type"].Should().BeEqualTo("http");
        await ads["format"].Should().BeEqualTo("mrs");
        await ads["path"].Should().BeEqualTo("./ruleset/ads.mrs");
        await ads["interval"].Should().BeEqualTo("3600");
        var local = (Dictionary<object, object>)providers["local"];
        await local["type"].Should().BeEqualTo("file");

        var rules = ((List<object>)result["rules"]).Select(t => t.ToString()).ToList();
        await rules.Should().BeEquivalentTo(new List<string?>
        {
            "RULE-SET,ads,REJECT",
            "DOMAIN-SUFFIX,google.com,Auto",
            "AND,((DOMAIN,a.com),(NETWORK,UDP)),HK",
            "DOMAIN-SUFFIX,example.com,Proxy",
            "MATCH,DIRECT",
        });
    }

    [Test]
    public async Task Apply_ReplaceRules_ShouldDropOriginalRules()
    {
        var content = Load();
        var item = new ClashOverrideItem
        {
            EnablePolicyOverride = true,
            ReplaceRules = true,
            Rules = "GEOIP,CN,DIRECT\nMATCH,Proxy",
        };

        ClashOverrideService.Apply(content, item, false);

        var rules = ((List<object>)content["rules"]).Select(t => t.ToString()).ToList();
        await rules.Should().BeEquivalentTo(new List<string?> { "GEOIP,CN,DIRECT", "MATCH,Proxy" });
    }

    [Test]
    public async Task Validate_ShouldReportInvalidLines()
    {
        var item = new ClashOverrideItem
        {
            EnableDnsOverride = true,
            NameserverPolicy = "geosite:cn",
            EnablePolicyOverride = true,
            ProxyGroups = "Bad = unknown-type, a\nEmpty = select\nGood = select, DIRECT",
            RuleProviders = "p = notabehavior, https://x",
            Rules = "MATCH",
        };

        var result = ClashOverrideService.Validate(item);

        await result.IsValid.Should().BeFalse();
        await result.NameserverMissing.Should().BeTrue();
        await result.NameserverPolicy.Count.Should().BeEqualTo(1);
        await result.ProxyGroups.Count.Should().BeEqualTo(2);
        await result.RuleProviders.Count.Should().BeEqualTo(1);
        await result.Rules.Count.Should().BeEqualTo(1);
    }

    [Test]
    public async Task GetRulePolicy_ShouldHandleRuleShapes()
    {
        await ClashOverrideService.GetRulePolicy("MATCH,Proxy").Should().BeEqualTo("Proxy");
        await ClashOverrideService.GetRulePolicy("IP-CIDR,10.0.0.0/8,DIRECT,no-resolve").Should().BeEqualTo("DIRECT");
        await ClashOverrideService.GetRulePolicy("OR,((DOMAIN,a.com),(DOMAIN,b.com)),Proxy").Should().BeEqualTo("Proxy");
        await ClashOverrideService.GetRulePolicy("SUB-RULE,(NETWORK,tcp),sub").Should().BeNull();
    }
}
