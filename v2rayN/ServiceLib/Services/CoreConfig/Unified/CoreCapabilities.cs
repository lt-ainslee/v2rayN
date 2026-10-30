namespace ServiceLib.Services.CoreConfig;

public enum EFidelity
{
    Native,

    /// <summary>Supported with slightly different semantics; reported as information.</summary>
    Approximate,

    /// <summary>Cannot be expressed; the validator degrades the plan (only ever narrowing a rule).</summary>
    Unsupported,
}

public enum ECapability
{
    DomainFull,
    DomainSuffix,
    DomainKeyword,
    DomainRegex,
    DomainGeoSite,
    DomainGeoSiteAttribute,
    DomainDotless,
    DomainPlain,
    DomainExternal,
    DomainRuleSet,

    IpCidr,
    IpGeoIP,
    IpPrivate,
    IpNegated,
    IpExternal,
    IpRuleSet,

    Port,
    Network,
    ProcessName,
    ProcessPath,
    ProcessSelf,
    Protocol,
    InboundTag,
    SourceIp,
    SourcePort,

    GroupSelect,
    GroupUrlTest,
    GroupFallback,
    GroupLoadBalance,
    GroupChain,

    DnsServeStale,
    DnsParallelQuery,
    DnsExpectedIps,
    DnsFakeIp,
    DnsHosts,
    DnsSystemHosts,
    DnsBlockAAAA,
    DnsBlockBindingQuery,
    DnsPolicyBlock,
    DnsPolicyKeyword,
    DnsPolicyRegex,
    DnsPolicyServers,
}

/// <summary>
/// What each core can express. Keep this table in sync with the translators;
/// the validator and the UI badges are driven by it.
/// </summary>
public static class CoreCapabilities
{
    private static readonly Dictionary<ECapability, EFidelity> _xray = new()
    {
        [ECapability.DomainRuleSet] = EFidelity.Unsupported,
        [ECapability.IpRuleSet] = EFidelity.Unsupported,
        [ECapability.GroupSelect] = EFidelity.Approximate,
        [ECapability.GroupFallback] = EFidelity.Approximate,
        [ECapability.DnsPolicyBlock] = EFidelity.Unsupported,
    };

    private static readonly Dictionary<ECapability, EFidelity> _singbox = new()
    {
        [ECapability.DomainDotless] = EFidelity.Approximate,
        [ECapability.DomainExternal] = EFidelity.Unsupported,
        [ECapability.IpExternal] = EFidelity.Unsupported,
        [ECapability.ProcessSelf] = EFidelity.Approximate,
        [ECapability.GroupFallback] = EFidelity.Approximate,
        [ECapability.GroupLoadBalance] = EFidelity.Approximate,
    };

    private static readonly Dictionary<ECapability, EFidelity> _mihomo = new()
    {
        [ECapability.DomainGeoSiteAttribute] = EFidelity.Unsupported,
        [ECapability.DomainExternal] = EFidelity.Unsupported,
        [ECapability.IpExternal] = EFidelity.Unsupported,
        [ECapability.ProcessSelf] = EFidelity.Unsupported,
        [ECapability.Protocol] = EFidelity.Unsupported,
        [ECapability.InboundTag] = EFidelity.Unsupported,
        [ECapability.DnsServeStale] = EFidelity.Unsupported,
        [ECapability.DnsParallelQuery] = EFidelity.Unsupported,
        [ECapability.DnsExpectedIps] = EFidelity.Unsupported,
        [ECapability.DnsBlockAAAA] = EFidelity.Approximate,
        [ECapability.DnsBlockBindingQuery] = EFidelity.Unsupported,
        [ECapability.DnsPolicyBlock] = EFidelity.Unsupported,
        [ECapability.DnsPolicyKeyword] = EFidelity.Unsupported,
        [ECapability.DnsPolicyRegex] = EFidelity.Unsupported,
    };

    public static EFidelity Get(ECoreType core, ECapability capability)
    {
        var table = core switch
        {
            ECoreType.Xray => _xray,
            ECoreType.sing_box => _singbox,
            ECoreType.mihomo => _mihomo,
            _ => null,
        };
        return table?.GetValueOrDefault(capability, EFidelity.Native) ?? EFidelity.Unsupported;
    }

    public static IReadOnlyList<ECoreType> UnifiedCores { get; } = [ECoreType.Xray, ECoreType.sing_box, ECoreType.mihomo];

    public static ECapability ToCapability(DomainMatch match) => match.Kind switch
    {
        EDomainMatchKind.Full => ECapability.DomainFull,
        EDomainMatchKind.Suffix => ECapability.DomainSuffix,
        EDomainMatchKind.Keyword => ECapability.DomainKeyword,
        EDomainMatchKind.Regex => ECapability.DomainRegex,
        EDomainMatchKind.GeoSite => match.GeoSiteAttribute != null ? ECapability.DomainGeoSiteAttribute : ECapability.DomainGeoSite,
        EDomainMatchKind.Dotless => ECapability.DomainDotless,
        EDomainMatchKind.Plain => ECapability.DomainPlain,
        EDomainMatchKind.External => ECapability.DomainExternal,
        _ => ECapability.DomainRuleSet,
    };

    public static ECapability ToCapability(IpMatch match) => match.Kind switch
    {
        EIpMatchKind.Cidr => ECapability.IpCidr,
        EIpMatchKind.GeoIP => ECapability.IpGeoIP,
        EIpMatchKind.Private => ECapability.IpPrivate,
        EIpMatchKind.External => ECapability.IpExternal,
        _ => ECapability.IpRuleSet,
    };

    public static ECapability ToCapability(ProcessMatch match) => match.Kind switch
    {
        EProcessMatchKind.Name => ECapability.ProcessName,
        EProcessMatchKind.Path => ECapability.ProcessPath,
        _ => ECapability.ProcessSelf,
    };

    public static ECapability ToCapability(EGroupKind kind) => kind switch
    {
        EGroupKind.Select => ECapability.GroupSelect,
        EGroupKind.UrlTest => ECapability.GroupUrlTest,
        EGroupKind.Fallback => ECapability.GroupFallback,
        EGroupKind.LoadBalance => ECapability.GroupLoadBalance,
        _ => ECapability.GroupChain,
    };

    public static string CoreDisplayName(ECoreType core) => core switch
    {
        ECoreType.sing_box => "sing-box",
        _ => core.ToString(),
    };
}

public sealed record CoreCapabilityRow(string Name, string Xray, string Singbox, string Mihomo);

public static class CoreCapabilityMatrix
{
    /// <summary>
    /// One row per capability: ✓ native, ≈ approximate, ✗ unsupported.
    /// </summary>
    public static IReadOnlyList<CoreCapabilityRow> Rows()
    {
        return Enum.GetValues<ECapability>()
            .Select(c => new CoreCapabilityRow(
                DisplayName(c),
                Mark(ECoreType.Xray, c),
                Mark(ECoreType.sing_box, c),
                Mark(ECoreType.mihomo, c)))
            .ToList();
    }

    private static string Mark(ECoreType core, ECapability capability) => CoreCapabilities.Get(core, capability) switch
    {
        EFidelity.Native => "✓",
        EFidelity.Approximate => "≈",
        _ => "✗",
    };

    /// <summary>
    /// Names in the rule / settings syntax the user writes, so the table reads like the rule editor.
    /// </summary>
    public static string DisplayName(ECapability capability) => capability switch
    {
        ECapability.DomainFull => "full:",
        ECapability.DomainSuffix => "domain:",
        ECapability.DomainKeyword => "keyword:",
        ECapability.DomainRegex => "regexp:",
        ECapability.DomainGeoSite => "geosite:",
        ECapability.DomainGeoSiteAttribute => "geosite:name@attr",
        ECapability.DomainDotless => "dotless:",
        ECapability.DomainPlain => "domain (no prefix)",
        ECapability.DomainExternal => "ext: / ext-domain:",
        ECapability.DomainRuleSet => "ruleset: (domain)",
        ECapability.IpCidr => "IP / CIDR",
        ECapability.IpGeoIP => "geoip:",
        ECapability.IpPrivate => "geoip:private",
        ECapability.IpNegated => "!IP (negated)",
        ECapability.IpExternal => "ext: / ext-ip:",
        ECapability.IpRuleSet => "ruleset: (IP)",
        ECapability.Port => "port",
        ECapability.Network => "network",
        ECapability.ProcessName => "process name",
        ECapability.ProcessPath => "process path",
        ECapability.ProcessSelf => "self/ xray/",
        ECapability.Protocol => "protocol",
        ECapability.InboundTag => "inboundTag",
        ECapability.SourceIp => "source IP",
        ECapability.SourcePort => "source port",
        ECapability.GroupSelect => "group: select",
        ECapability.GroupUrlTest => "group: lowest latency",
        ECapability.GroupFallback => "group: fallback",
        ECapability.GroupLoadBalance => "group: load balance",
        ECapability.GroupChain => "group: proxy chain",
        ECapability.DnsServeStale => "DNS: serve stale",
        ECapability.DnsParallelQuery => "DNS: parallel query",
        ECapability.DnsExpectedIps => "DNS: expected IPs",
        ECapability.DnsFakeIp => "DNS: FakeIP",
        ECapability.DnsHosts => "DNS: hosts",
        ECapability.DnsSystemHosts => "DNS: system hosts",
        ECapability.DnsBlockAAAA => "DNS: block AAAA",
        ECapability.DnsBlockBindingQuery => "DNS: block SVCB/HTTPS",
        ECapability.DnsPolicyBlock => "DNS policy: block",
        ECapability.DnsPolicyKeyword => "DNS policy: keyword / plain",
        ECapability.DnsPolicyRegex => "DNS policy: regexp",
        ECapability.DnsPolicyServers => "DNS policy: custom servers",
        _ => capability.ToString(),
    };
}
