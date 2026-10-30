using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ServiceLib.Tests.CoreConfig.Unified;

/// <summary>
/// Snapshot tests that pin the generated Xray / sing-box configuration for representative
/// DNS and routing settings, so refactors of the shared translation layer cannot change output.
/// Set UPDATE_SNAPSHOTS=1 to regenerate the snapshot files after an intended change.
/// </summary>
// These tests replace the global AppManager config, so they must not run concurrently.
[NotInParallel(CoreConfigTestFactory.AppManagerConfigKey)]
public partial class GoldenOutputTests
{
    public static IEnumerable<Func<(string Name, string DomainStrategy, bool FakeIp, bool ParallelQuery)>> Scenarios()
    {
        yield return () => ("asis", Global.AsIs, false, false);
        yield return () => ("ipifnonmatch-fakeip", Global.IPIfNonMatch, true, false);
        yield return () => ("ipondemand-parallel", Global.IPOnDemand, false, true);
    }

    [Test]
    [MethodDataSource(nameof(Scenarios))]
    public async Task Xray_Output_ShouldMatchSnapshot(string name, string domainStrategy, bool fakeIp, bool parallelQuery)
    {
        var context = CreateContext(ECoreType.Xray, domainStrategy, fakeIp, parallelQuery);
        var result = new CoreConfigV2rayService(context).GenerateClientConfigContent();
        await result.Success.Should().BeTrue().Because($"ret msg: {result.Msg}");
        await AssertSnapshot($"xray-{name}", result.Data!.ToString()!);
    }

    [Test]
    [MethodDataSource(nameof(Scenarios))]
    public async Task Singbox_Output_ShouldMatchSnapshot(string name, string domainStrategy, bool fakeIp, bool parallelQuery)
    {
        var context = CreateContext(ECoreType.sing_box, domainStrategy, fakeIp, parallelQuery);
        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();
        await result.Success.Should().BeTrue().Because($"ret msg: {result.Msg}");
        await AssertSnapshot($"singbox-{name}", result.Data!.ToString()!);
    }

    internal static List<RulesItem> SampleRules() =>
    [
        new()
        {
            Id = "rule-ads", Enabled = true, RuleType = ERuleType.ALL, OutboundTag = Global.BlockTag,
            Domain = ["geosite:category-ads-all", "ext:custom.dat:ads"],
        },
        new()
        {
            Id = "rule-private", Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag,
            Ip = ["geoip:private"], Domain = ["full:localhost"],
        },
        new()
        {
            Id = "rule-google", Enabled = true, RuleType = ERuleType.ALL, OutboundTag = Global.ProxyTag,
            Domain = ["geosite:google", "domain:youtube.com", "keyword:gstatic", "regexp:^api\\d+\\.example\\.org$", "twitter", "dotless:intranet", "#comment"],
        },
        new()
        {
            Id = "rule-ports", Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag,
            Port = "123,5000-6000", Network = "udp",
        },
        new()
        {
            Id = "rule-process", Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag,
            Process = ["curl", "/usr/bin/wget", "self/"],
        },
        new()
        {
            Id = "rule-proto", Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.BlockTag,
            Protocol = ["bittorrent"],
        },
        new()
        {
            Id = "rule-inbound", Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.ProxyTag,
            InboundTag = ["socks"], Domain = ["domain:inbound.example.com"], Port = "443",
        },
        new()
        {
            Id = "rule-neg-ip", Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.ProxyTag,
            Ip = ["!geoip:cn", "1.1.1.1/32"],
        },
        new()
        {
            Id = "rule-disabled", Enabled = false, RuleType = ERuleType.Routing, OutboundTag = Global.BlockTag,
            Domain = ["full:disabled.example.com"],
        },
        new()
        {
            Id = "rule-dns-only", Enabled = true, RuleType = ERuleType.DNS, OutboundTag = Global.DirectTag,
            Domain = ["domain:dns-only.example.com"],
        },
        new()
        {
            Id = "rule-cn", Enabled = true, RuleType = ERuleType.ALL, OutboundTag = Global.DirectTag,
            Domain = ["geosite:cn"], Ip = ["geoip:cn"],
        },
        new()
        {
            Id = "rule-final", Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.ProxyTag,
            Port = "0-65535",
        },
    ];

    private static CoreConfigContext CreateContext(ECoreType coreType, string domainStrategy, bool fakeIp, bool parallelQuery)
    {
        var config = CoreConfigTestFactory.CreateConfig(coreType);
        config.RoutingBasicItem.DomainStrategy = domainStrategy;
        config.SimpleDNSItem.DirectDNS = "223.5.5.5,https://dns.alidns.com/dns-query";
        config.SimpleDNSItem.RemoteDNS = "https://1.1.1.1/dns-query";
        config.SimpleDNSItem.FakeIP = fakeIp;
        config.SimpleDNSItem.GlobalFakeIp = true;
        config.SimpleDNSItem.FakeIPRange = Global.FakeIPRanges.First();
        config.SimpleDNSItem.ParallelQuery = parallelQuery;
        config.SimpleDNSItem.BlockBindingQuery = true;
        config.SimpleDNSItem.AddCommonHosts = true;
        config.SimpleDNSItem.Hosts = "full:hosts.example.com 10.0.0.1\ndomain:alias.example.com target.example.com";
        config.SimpleDNSItem.DirectExpectedIPs = "geoip:cn";
        CoreConfigTestFactory.BindAppManagerConfig(config);

        var node = CoreConfigTestFactory.CreateSocksNode(coreType, "n-main", "main");
        return CoreConfigTestFactory.CreateContext(config, node, coreType) with
        {
            RoutingItem = new RoutingItem
            {
                Id = "r-golden",
                Remarks = "golden",
                RuleSet = JsonUtils.Serialize(SampleRules()),
                DomainStrategy = string.Empty,
                DomainStrategy4Singbox = string.Empty,
            },
        };
    }

    private static async Task AssertSnapshot(string name, string actual, [CallerFilePath] string sourceFile = "")
    {
        actual = FinalGuidRegex().Replace(actual.ReplaceLineEndings("\n"), "final-GUID");
        var path = Path.Combine(Path.GetDirectoryName(sourceFile)!, "Snapshots", $"{name}.json");
        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, actual);
        }
        var expected = (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n");
        if (actual != expected)
        {
            await File.WriteAllTextAsync(Path.ChangeExtension(path, ".actual"), actual);
        }
        await actual.Should().BeEqualTo(expected).Because($"snapshot {name} changed; rerun with UPDATE_SNAPSHOTS=1 if intended");
    }

    [GeneratedRegex("final-[0-9a-fA-F]{32}")]
    private static partial Regex FinalGuidRegex();
}
