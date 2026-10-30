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
