namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Parses v2rayN's rule syntax (the v2ray routing syntax used by RulesItem) into the core-agnostic IR.
/// This is the single place that knows the meaning of prefixes such as domain:, full:, geosite:, geoip:.
/// </summary>
public static class UnifiedRuleParser
{
    public const string RuleSetPrefix = "ruleset:";

    /// <summary>
    /// Returns null for comments and empty entries.
    /// </summary>
    public static DomainMatch? ParseDomain(string? raw)
    {
        if (raw.IsNullOrEmpty() || raw.StartsWith('#'))
        {
            return null;
        }
        if (raw.StartsWith("ext:") || raw.StartsWith("ext-domain:"))
        {
            return new(EDomainMatchKind.External, raw[(raw.IndexOf(':') + 1)..], raw);
        }
        if (raw.StartsWith(Global.GeoSitePrefix))
        {
            return new(EDomainMatchKind.GeoSite, raw[Global.GeoSitePrefix.Length..], raw);
        }
        if (raw.StartsWith(RuleSetPrefix))
        {
            return new(EDomainMatchKind.RuleSet, raw[RuleSetPrefix.Length..], raw);
        }
        if (raw.StartsWith("regexp:"))
        {
            return new(EDomainMatchKind.Regex, raw.Replace(Global.RoutingRuleComma, ",")[7..], raw);
        }
        if (raw.StartsWith("domain:"))
        {
            return new(EDomainMatchKind.Suffix, raw[7..], raw);
        }
        if (raw.StartsWith("full:"))
        {
            return new(EDomainMatchKind.Full, raw[5..], raw);
        }
        if (raw.StartsWith("keyword:"))
        {
            return new(EDomainMatchKind.Keyword, raw[8..], raw);
        }
        if (raw.StartsWith("dotless:"))
        {
            return new(EDomainMatchKind.Dotless, raw[8..], raw);
        }
        return new(EDomainMatchKind.Plain, raw, raw);
    }

    public static IpMatch? ParseIp(string? raw)
    {
        if (raw.IsNullOrEmpty() || raw.StartsWith('#'))
        {
            return null;
        }
        var negated = raw.StartsWith('!');
        var value = negated ? raw[1..].Trim() : raw.Trim();
        if (value.StartsWith("ext:") || value.StartsWith("ext-ip:"))
        {
            return new(EIpMatchKind.External, value[(value.IndexOf(':') + 1)..], negated, raw);
        }
        if (value.Equals($"{Global.GeoIPPrefix}private"))
        {
            return new(EIpMatchKind.Private, "private", negated, raw);
        }
        if (value.StartsWith(Global.GeoIPPrefix))
        {
            return new(EIpMatchKind.GeoIP, value[Global.GeoIPPrefix.Length..], negated, raw);
        }
        if (value.StartsWith(RuleSetPrefix))
        {
            return new(EIpMatchKind.RuleSet, value[RuleSetPrefix.Length..], negated, raw);
        }
        return new(EIpMatchKind.Cidr, value, negated, raw);
    }

    /// <summary>
    /// Parses "80,443,1000-2000". Invalid entries (not numbers, outside 0-65535, reversed ranges) are skipped.
    /// </summary>
    public static List<PortRange> ParsePorts(string? raw)
    {
        var result = new List<PortRange>();
        if (raw.IsNullOrEmpty())
        {
            return result;
        }
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-', StringSplitOptions.TrimEntries);
            if (range.Length == 1 && int.TryParse(range[0], out var port) && IsPort(port))
            {
                result.Add(new(port, port));
            }
            else if (range.Length == 2 && int.TryParse(range[0], out var from) && int.TryParse(range[1], out var to)
                && IsPort(from) && IsPort(to) && from <= to)
            {
                result.Add(new(from, to));
            }
        }
        return result;
    }

    /// <summary>
    /// True for an IP address or a CIDR with a prefix length valid for its address family.
    /// </summary>
    public static bool IsValidCidr(string? value)
    {
        if (value.IsNullOrEmpty())
        {
            return false;
        }
        var parts = value.Split('/');
        if (parts.Length > 2 || parts[0].Contains('%') || !IPAddress.TryParse(parts[0], out var address))
        {
            return false;
        }
        // IPAddress.TryParse also accepts shorthand IPv4 such as "10", "1.2.3" or "0x7f.1", which cores reject.
        if (address.AddressFamily == AddressFamily.InterNetwork
            && parts[0].Split('.') is var octets
            && (octets.Length != 4 || octets.Any(o => o.Length == 0 || !o.All(char.IsAsciiDigit))))
        {
            return false;
        }
        var maxPrefix = address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        return parts.Length == 1 || (int.TryParse(parts[1], out var prefix) && prefix >= 0 && prefix <= maxPrefix);
    }

    private static bool IsPort(int port) => port is >= 0 and <= 65535;

    public static ProcessMatch? ParseProcess(string? raw)
    {
        if (raw.IsNullOrEmpty())
        {
            return null;
        }
        if (raw is "self/" or "xray/")
        {
            return new(EProcessMatchKind.Self, raw, raw);
        }
        if (raw.Contains('/') || raw.Contains('\\'))
        {
            return new(EProcessMatchKind.Path, raw, raw);
        }
        return new(EProcessMatchKind.Name, raw, raw);
    }

    public static UnifiedRule ParseRule(RulesItem item)
    {
        return new UnifiedRule
        {
            Id = item.Id ?? string.Empty,
            Remarks = item.Remarks,
            Scope = item.RuleType ?? ERuleType.ALL,
            Target = RuleTarget.From(item.OutboundTag),
            Domains = [.. (item.Domain ?? []).Select(ParseDomain).OfType<DomainMatch>()],
            Ips = [.. (item.Ip ?? []).Select(ParseIp).OfType<IpMatch>()],
            Processes = [.. (item.Process ?? []).Select(ParseProcess).OfType<ProcessMatch>()],
            Ports = ParsePorts(item.Port),
            Networks = Utils.String2List(item.Network)?.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToList() ?? [],
            Protocols = [.. item.Protocol ?? []],
            InboundTags = [.. item.InboundTag ?? []],
            SourceIps = [.. (item.SourceIp ?? []).Select(ParseIp).OfType<IpMatch>()],
            SourcePorts = ParsePorts(item.SourcePort),
            CoreExt = item.CoreExt,
            Source = item,
        };
    }

    /// <summary>
    /// Parses one explicit DNS policy line: "match[, match] = direct | remote | block | server1, server2".
    /// </summary>
    public static DnsPolicy? ParseDnsPolicyLine(string line, int index)
    {
        var idx = line.IndexOf('=');
        if (idx <= 0)
        {
            return null;
        }
        var domains = line[..idx].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseDomain).OfType<DomainMatch>().ToList();
        var targets = line[(idx + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (domains.Count == 0 || targets.Count == 0)
        {
            return null;
        }
        var sourceId = $"{DnsPolicy.ExplicitIdPrefix}{index}";
        return targets[0].ToLowerInvariant() switch
        {
            "direct" when targets.Count == 1 => new DnsPolicy(domains, EDnsPolicyTarget.Direct, sourceId),
            "remote" or "proxy" when targets.Count == 1 => new DnsPolicy(domains, EDnsPolicyTarget.Remote, sourceId),
            "block" when targets.Count == 1 => new DnsPolicy(domains, EDnsPolicyTarget.Block, sourceId),
            _ => new DnsPolicy(domains, EDnsPolicyTarget.Remote, sourceId, targets),
        };
    }

    public static List<string> SplitLines(string? text)
    {
        if (text.IsNullOrEmpty())
        {
            return [];
        }
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0 && !t.StartsWith('#'))
            .ToList();
    }
}
