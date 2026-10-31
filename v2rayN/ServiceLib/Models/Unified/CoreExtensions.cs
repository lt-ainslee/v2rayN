namespace ServiceLib.Models.Unified;

// Per-core extensions layered on top of the shared settings.
// Priority (low -> high): shared settings < field level (CoreExtensionItem) < object level (RuleCoreExtension)
// < document level (full config template / custom DNS JSON / Clash Mixin.yaml).

/// <summary>
/// Object-level extension attached to a single routing rule.
/// </summary>
[Serializable]
public class RuleCoreExtension
{
    /// <summary>JSON object merged into every Xray rule generated from this rule.</summary>
    public string? XrayJson { get; set; }

    /// <summary>JSON object merged into every sing-box rule generated from this rule.</summary>
    public string? SingboxJson { get; set; }

    /// <summary>Clash rule lines that replace the translated mihomo rules of this rule; use {target} for the policy.</summary>
    public string? MihomoRules { get; set; }

    public bool IsEmpty() => XrayJson.IsNullOrEmpty() && SingboxJson.IsNullOrEmpty() && MihomoRules.IsNullOrEmpty();
}

/// <summary>
/// Field-level extensions, one block per core.
/// </summary>
[Serializable]
public class CoreExtensionItem
{
    public XrayExtension Xray { get; set; } = new();
    public SingboxExtension Singbox { get; set; } = new();
    public MihomoExtension Mihomo { get; set; } = new();
}

[Serializable]
public class XrayExtension
{
    /// <summary>dns.queryStrategy: UseIP / UseIPv4 / UseIPv6</summary>
    public string? DnsQueryStrategy { get; set; }

    /// <summary>dns.disableCache</summary>
    public bool? DnsDisableCache { get; set; }

    /// <summary>dns.disableFallback: never query the other servers when the matched one fails.</summary>
    public bool? DnsDisableFallback { get; set; }

    /// <summary>dns.disableFallbackIfMatch: no fallback when a server's domain list matched.</summary>
    public bool? DnsDisableFallbackIfMatch { get; set; }

    /// <summary>dns.clientIp: EDNS Client Subnet address, e.g. 1.2.3.4</summary>
    public string? DnsClientIp { get; set; }

    /// <summary>dns.serveExpiredTTL (seconds); only used with serve stale. 0: never expire.</summary>
    public int? DnsServeExpiredTtl { get; set; }
}

[Serializable]
public class SingboxExtension
{
    /// <summary>dns.client_subnet, e.g. 1.2.3.0/24</summary>
    public string? DnsClientSubnet { get; set; }

    /// <summary>dns.independent_cache (deprecated in sing-box 1.14)</summary>
    public bool? DnsIndependentCache { get; set; }

    /// <summary>dns.disable_expire; conflicts with optimistic (serve stale), which wins.</summary>
    public bool? DnsDisableExpire { get; set; }

    /// <summary>dns.cache_capacity; values below 1024 are ignored by sing-box.</summary>
    public int? DnsCacheCapacity { get; set; }

    /// <summary>dns.reverse_mapping: keep IP -> domain mappings so routing sees domains for IP connections.</summary>
    public bool? DnsReverseMapping { get; set; }

    /// <summary>route.find_process: look processes up for logs and the connection list.</summary>
    public bool? RouteFindProcess { get; set; }

    /// <summary>route.default_mark (Linux only).</summary>
    public int? RouteDefaultMark { get; set; }

    /// <summary>experimental.cache_file.store_rdrc: remember rejected DNS responses; needs the cache file.</summary>
    public bool? CacheFileStoreRdrc { get; set; }
}

/// <summary>
/// How the shared DNS / routing settings are applied to a Clash (mihomo) configuration file.
/// </summary>
public enum EMihomoUnifiedMode
{
    /// <summary>Keep the DNS and rules of the configuration file.</summary>
    Off = 0,

    /// <summary>Replace the dns section with the shared DNS settings.</summary>
    DnsOnly = 1,

    /// <summary>Replace the dns section and insert the shared routing rules before the original rules.</summary>
    DnsAndRules = 2,

    /// <summary>Replace the dns section and the rules entirely with the shared settings.</summary>
    DnsAndReplaceRules = 3,
}

[Serializable]
public class MihomoExtension
{
    public EMihomoUnifiedMode UnifiedMode { get; set; } = EMihomoUnifiedMode.Off;

    /// <summary>Policy used for rules targeting "proxy". Empty: the first select group of the file.</summary>
    public string? ProxyGroupName { get; set; }

    public bool RespectRules { get; set; } = true;

    /// <summary>fake-ip-filter entries, one per line. Empty: the built-in filter list.</summary>
    public string? FakeIpFilter { get; set; }

    public bool PreferH3 { get; set; }

    /// <summary>Extra proxy groups: name = type, member1, member2, key=value</summary>
    public string? ExtraProxyGroups { get; set; }

    /// <summary>Extra Clash rules inserted before the translated rules.</summary>
    public string? ExtraRules { get; set; }

    /// <summary>dns.cache-algorithm: lru / arc. Empty: mihomo default (lru).</summary>
    public string? DnsCacheAlgorithm { get; set; }

    // General options: applied whatever the unified mode; null keeps the configuration file's value.

    /// <summary>unified-delay</summary>
    public bool? UnifiedDelay { get; set; }

    /// <summary>tcp-concurrent</summary>
    public bool? TcpConcurrent { get; set; }

    /// <summary>find-process-mode: always / strict / off</summary>
    public string? FindProcessMode { get; set; }

    /// <summary>keep-alive-interval (seconds)</summary>
    public int? KeepAliveInterval { get; set; }

    /// <summary>sniffer.enable; when enabling and the file has no sniffer, HTTP / TLS / QUIC are sniffed on common ports.</summary>
    public bool? SnifferEnabled { get; set; }
}

/// <summary>
/// A rule set that rules can reference with ruleset:name in their domain or IP list.
/// </summary>
[Serializable]
public class RuleSetItem
{
    public string Name { get; set; } = string.Empty;

    /// <summary>domain / ipcidr / classical</summary>
    public string Behavior { get; set; } = "domain";

    /// <summary>sing-box source (.srs / .json), URL or local path.</summary>
    public string? SingboxSource { get; set; }

    /// <summary>mihomo source (.mrs / .yaml / .txt), URL or local path.</summary>
    public string? MihomoSource { get; set; }

    public int UpdateInterval { get; set; } = 86400;
}
