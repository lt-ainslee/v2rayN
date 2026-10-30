namespace ServiceLib.Models.Unified;

// Core-agnostic intermediate representation (IR) of the user's DNS, routing and policy settings.
// Every core translator (Xray, sing-box, mihomo) consumes the same IR, so a setting configured once
// applies to all cores; per-core capabilities decide how unsupported parts are degraded.

#region Rule conditions

public enum EDomainMatchKind
{
    /// <summary>full:example.com</summary>
    Full,

    /// <summary>domain:example.com</summary>
    Suffix,

    /// <summary>keyword:example</summary>
    Keyword,

    /// <summary>regexp:^ex.*$</summary>
    Regex,

    /// <summary>geosite:cn, geosite:google@ads</summary>
    GeoSite,

    /// <summary>dotless:intranet - a domain without dots containing the value</summary>
    Dotless,

    /// <summary>Bare value without prefix: substring match in the routing system, full match in hosts.</summary>
    Plain,

    /// <summary>ext:file.dat:tag - Xray external geosite file</summary>
    External,

    /// <summary>ruleset:name - a remote or local rule set declared in the rule set list</summary>
    RuleSet,
}

public sealed record DomainMatch(EDomainMatchKind Kind, string Value, string Raw)
{
    /// <summary>geosite attribute filter, e.g. "ads" for geosite:google@ads</summary>
    public string? GeoSiteAttribute => Kind == EDomainMatchKind.GeoSite && Value.Contains('@') ? Value[(Value.IndexOf('@') + 1)..] : null;
}

public enum EIpMatchKind
{
    Cidr,
    GeoIP,

    /// <summary>geoip:private</summary>
    Private,

    /// <summary>ext:file.dat:tag / ext-ip:file.dat:tag</summary>
    External,

    /// <summary>ruleset:name (IP rule set)</summary>
    RuleSet,
}

public sealed record IpMatch(EIpMatchKind Kind, string Value, bool Negated, string Raw);

public sealed record PortRange(int From, int To)
{
    public bool IsSingle => From == To;

    public bool IsAny => From <= 0 && To >= 65535;

    public override string ToString() => IsSingle ? From.ToString() : $"{From}-{To}";
}

public enum EProcessMatchKind
{
    Name,
    Path,

    /// <summary>self/ or xray/ - the running core itself</summary>
    Self,
}

public sealed record ProcessMatch(EProcessMatchKind Kind, string Value, string Raw);

public enum ERuleTargetKind
{
    Proxy,
    Direct,
    Block,

    /// <summary>A node or policy group referenced by its remarks.</summary>
    Named,
}

public sealed record RuleTarget(ERuleTargetKind Kind, string Name)
{
    public static RuleTarget From(string? outboundTag) => outboundTag switch
    {
        null or "" or Global.ProxyTag => new(ERuleTargetKind.Proxy, Global.ProxyTag),
        Global.DirectTag => new(ERuleTargetKind.Direct, Global.DirectTag),
        Global.BlockTag => new(ERuleTargetKind.Block, Global.BlockTag),
        _ => new(ERuleTargetKind.Named, outboundTag),
    };
}

#endregion Rule conditions

/// <summary>
/// One routing rule. Semantics follow v2rayN's existing rule model:
/// the destination selectors (domains, IPs, processes) are OR-ed with each other,
/// every non-empty constraint (ports, networks, protocols, inbound tags, sources) is AND-ed on top.
/// </summary>
public sealed class UnifiedRule
{
    public required string Id { get; init; }
    public string? Remarks { get; init; }
    public ERuleType Scope { get; init; }
    public required RuleTarget Target { get; set; }

    public List<DomainMatch> Domains { get; init; } = [];
    public List<IpMatch> Ips { get; init; } = [];
    public List<ProcessMatch> Processes { get; init; } = [];

    public List<PortRange> Ports { get; init; } = [];
    public List<string> Networks { get; init; } = [];
    public List<string> Protocols { get; init; } = [];
    public List<string> InboundTags { get; init; } = [];
    public List<IpMatch> SourceIps { get; init; } = [];
    public List<PortRange> SourcePorts { get; init; } = [];

    /// <summary>Per-core extension attached to this rule.</summary>
    public RuleCoreExtension? CoreExt { get; init; }

    /// <summary>The original rule, for translators that emit the native v2ray syntax unchanged.</summary>
    public RulesItem? Source { get; init; }

    public bool HasSelectors => Domains.Count > 0 || Ips.Count > 0 || Processes.Count > 0;

    public bool HasConstraints => Ports.Count > 0 || Networks.Count > 0 || Protocols.Count > 0
        || InboundTags.Count > 0 || SourceIps.Count > 0 || SourcePorts.Count > 0;

    public string DisplayName => Remarks.IsNullOrEmpty() ? Id : Remarks;
}

public enum EDnsPolicyTarget
{
    Direct,
    Remote,
    Block,
}

/// <summary>Domains that must be resolved by a given DNS group, derived from routing rules or set explicitly.</summary>
public sealed record DnsPolicy(List<DomainMatch> Domains, EDnsPolicyTarget Target, string SourceId, List<string>? Servers = null);

public sealed class UnifiedDns
{
    public List<string> DirectServers { get; init; } = [];
    public List<string> RemoteServers { get; init; } = [];
    public List<string> BootstrapServers { get; init; } = [];

    public bool FakeIp { get; init; }
    public bool GlobalFakeIp { get; init; } = true;
    public string FakeIpRange { get; init; } = Global.FakeIPRanges.First();

    public Dictionary<string, List<string>> Hosts { get; init; } = [];
    public bool UseSystemHosts { get; init; }
    public bool AddCommonHosts { get; init; }

    public bool BlockAAAA { get; init; }
    public bool BlockBindingQuery { get; init; }
    public bool ServeStale { get; init; }
    public bool ParallelQuery { get; init; }
    public List<IpMatch> DirectExpectedIps { get; init; } = [];

    /// <summary>DNS policies in priority order (explicit policies first, then those derived from rules).</summary>
    public List<DnsPolicy> Policies { get; init; } = [];

    /// <summary>True when unmatched queries should use the direct DNS (the last rule is a catch-all direct rule).</summary>
    public bool FinalDirect { get; init; }
}

public enum EGroupKind
{
    Select,
    UrlTest,
    Fallback,
    LoadBalance,
    Chain,
}

public sealed class UnifiedGroup
{
    public required string Name { get; init; }
    public required EGroupKind Kind { get; init; }
    public EMultipleLoad MultipleLoad { get; init; } = EMultipleLoad.LeastPing;
    public List<string> Members { get; init; } = [];
    public string? Filter { get; init; }
}

public sealed class UnifiedPlan
{
    public List<UnifiedRule> Rules { get; init; } = [];
    public UnifiedDns Dns { get; init; } = new();
    public List<UnifiedGroup> Groups { get; init; } = [];
    public List<RuleSetItem> RuleSets { get; init; } = [];

    /// <summary>AsIs / IPIfNonMatch / IPOnDemand</summary>
    public string DomainStrategy { get; init; } = Global.AsIs;
}

#region Diagnostics

public enum EDiagnosticLevel
{
    Info,
    Warning,
}

public sealed record UnifiedDiagnostic(ECoreType Core, EDiagnosticLevel Level, string Subject, string Message)
{
    public override string ToString() => $"[{Core}] {Subject}: {Message}";
}

public sealed class UnifiedDiagnostics
{
    private readonly List<UnifiedDiagnostic> _items = [];

    public IReadOnlyList<UnifiedDiagnostic> Items => _items;

    public void Warn(ECoreType core, string subject, string message) => _items.Add(new(core, EDiagnosticLevel.Warning, subject, message));

    public void Info(ECoreType core, string subject, string message) => _items.Add(new(core, EDiagnosticLevel.Info, subject, message));

    public void AddRange(UnifiedDiagnostics other) => _items.AddRange(other.Items);
}

#endregion Diagnostics
