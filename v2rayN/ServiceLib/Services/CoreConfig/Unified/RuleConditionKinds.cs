namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// The condition types offered by the structured rule editor. Each maps to one prefix of the stored
/// rule syntax, so the editor reads and writes the same RulesItem lists as the text editor.
/// </summary>
public enum ERuleConditionKind
{
    DomainSuffix,
    DomainFull,
    DomainKeyword,
    DomainRegex,
    GeoSite,
    Dotless,
    DomainPlain,
    DomainExternal,
    DomainRuleSet,
    IpCidr,
    GeoIp,
    IpPrivate,
    IpExternal,
    IpRuleSet,
    ProcessName,
    ProcessPath,
}

public enum ERuleConditionField
{
    Domain,
    Ip,
    Process,
}

public static class RuleConditionKinds
{
    public static IReadOnlyList<ERuleConditionKind> All { get; } = Enum.GetValues<ERuleConditionKind>();

    public static ERuleConditionField FieldOf(ERuleConditionKind kind) => kind switch
    {
        >= ERuleConditionKind.IpCidr and <= ERuleConditionKind.IpRuleSet => ERuleConditionField.Ip,
        ERuleConditionKind.ProcessName or ERuleConditionKind.ProcessPath => ERuleConditionField.Process,
        _ => ERuleConditionField.Domain,
    };

    public static bool HasValue(ERuleConditionKind kind) => kind != ERuleConditionKind.IpPrivate;

    public static string DisplayName(ERuleConditionKind kind) => kind switch
    {
        ERuleConditionKind.DomainSuffix => $"{ResUI.TbCondDomainSuffix} (domain:)",
        ERuleConditionKind.DomainFull => $"{ResUI.TbCondDomainFull} (full:)",
        ERuleConditionKind.DomainKeyword => $"{ResUI.TbCondDomainKeyword} (keyword:)",
        ERuleConditionKind.DomainRegex => $"{ResUI.TbCondDomainRegex} (regexp:)",
        ERuleConditionKind.GeoSite => "GeoSite (geosite:)",
        ERuleConditionKind.Dotless => $"{ResUI.TbCondDotless} (dotless:)",
        ERuleConditionKind.DomainPlain => ResUI.TbCondDomainPlain,
        ERuleConditionKind.DomainExternal => $"{ResUI.TbCondExternal} (ext:)",
        ERuleConditionKind.DomainRuleSet => $"{ResUI.TbCondRuleSet} (ruleset:)",
        ERuleConditionKind.IpCidr => "IP / CIDR",
        ERuleConditionKind.GeoIp => "GeoIP (geoip:)",
        ERuleConditionKind.IpPrivate => $"{ResUI.TbCondIpPrivate} (geoip:private)",
        ERuleConditionKind.IpExternal => $"{ResUI.TbCondIpExternal} (ext-ip:)",
        ERuleConditionKind.IpRuleSet => $"{ResUI.TbCondIpRuleSet} (ruleset:)",
        ERuleConditionKind.ProcessName => ResUI.TbCondProcessName,
        _ => ResUI.TbCondProcessPath,
    };

    /// <summary>
    /// Builds the stored entry, e.g. "domain:google.com", "!geoip:cn", "regexp:a<COMMA>b".
    /// </summary>
    public static string ToRaw(ERuleConditionKind kind, string? value, bool negated)
    {
        value = (value ?? string.Empty).Trim();
        var raw = kind switch
        {
            ERuleConditionKind.DomainSuffix => $"domain:{value}",
            ERuleConditionKind.DomainFull => $"full:{value}",
            ERuleConditionKind.DomainKeyword => $"keyword:{value}",
            // Lists are edited as comma separated text elsewhere, so a literal comma is stored escaped.
            ERuleConditionKind.DomainRegex => $"regexp:{value.Replace(",", Global.RoutingRuleComma)}",
            ERuleConditionKind.GeoSite => $"{Global.GeoSitePrefix}{value}",
            ERuleConditionKind.Dotless => $"dotless:{value}",
            // External files keep the prefix they were stored with (ext: / ext-domain: / ext-ip:).
            ERuleConditionKind.DomainExternal => IsExternalRaw(value) ? value : $"ext:{value}",
            ERuleConditionKind.DomainRuleSet or ERuleConditionKind.IpRuleSet => $"{UnifiedRuleParser.RuleSetPrefix}{value}",
            ERuleConditionKind.GeoIp => $"{Global.GeoIPPrefix}{value}",
            ERuleConditionKind.IpPrivate => $"{Global.GeoIPPrefix}private",
            ERuleConditionKind.IpExternal => IsExternalRaw(value) ? value : $"ext-ip:{value}",
            _ => value,
        };
        return negated && FieldOf(kind) == ERuleConditionField.Ip ? $"!{raw}" : raw;
    }

    private static bool IsExternalRaw(string value) =>
        value.StartsWith("ext:") || value.StartsWith("ext-domain:") || value.StartsWith("ext-ip:");

    /// <summary>
    /// Reads a stored entry of the given list; null for comments and empty entries.
    /// </summary>
    public static (ERuleConditionKind Kind, string Value, bool Negated)? FromRaw(ERuleConditionField field, string raw)
    {
        switch (field)
        {
            case ERuleConditionField.Domain when UnifiedRuleParser.ParseDomain(raw) is { } d:
                var kind = d.Kind switch
                {
                    EDomainMatchKind.Full => ERuleConditionKind.DomainFull,
                    EDomainMatchKind.Suffix => ERuleConditionKind.DomainSuffix,
                    EDomainMatchKind.Keyword => ERuleConditionKind.DomainKeyword,
                    EDomainMatchKind.Regex => ERuleConditionKind.DomainRegex,
                    EDomainMatchKind.GeoSite => ERuleConditionKind.GeoSite,
                    EDomainMatchKind.Dotless => ERuleConditionKind.Dotless,
                    EDomainMatchKind.External => ERuleConditionKind.DomainExternal,
                    EDomainMatchKind.RuleSet => ERuleConditionKind.DomainRuleSet,
                    _ => ERuleConditionKind.DomainPlain,
                };
                // External files are shown with their prefix so it is written back unchanged.
                return (kind, kind == ERuleConditionKind.DomainExternal ? d.Raw : d.Value, false);

            case ERuleConditionField.Ip when UnifiedRuleParser.ParseIp(raw) is { } ip:
                var ipKind = ip.Kind switch
                {
                    EIpMatchKind.GeoIP => ERuleConditionKind.GeoIp,
                    EIpMatchKind.Private => ERuleConditionKind.IpPrivate,
                    EIpMatchKind.External => ERuleConditionKind.IpExternal,
                    EIpMatchKind.RuleSet => ERuleConditionKind.IpRuleSet,
                    _ => ERuleConditionKind.IpCidr,
                };
                var ipValue = ipKind switch
                {
                    ERuleConditionKind.IpPrivate => string.Empty,
                    ERuleConditionKind.IpExternal => raw.TrimStart('!').Trim(),
                    _ => ip.Value,
                };
                return (ipKind, ipValue, ip.Negated);

            case ERuleConditionField.Process when UnifiedRuleParser.ParseProcess(raw) is { } p:
                return (p.Kind == EProcessMatchKind.Path ? ERuleConditionKind.ProcessPath : ERuleConditionKind.ProcessName, p.Value, false);

            default:
                return null;
        }
    }

    /// <summary>
    /// Guesses the condition type of pasted text: prefixed entries keep their prefix, bare IPs/CIDRs are IP
    /// conditions, paths are process paths, names ending in .exe are process names, the rest are domain suffixes.
    /// </summary>
    public static (ERuleConditionKind Kind, string Value, bool Negated)? Guess(string text)
    {
        text = text.Trim();
        if (text.Length == 0 || text.StartsWith('#'))
        {
            return null;
        }
        var body = text.TrimStart('!');
        if (body.StartsWith(Global.GeoIPPrefix) || body.StartsWith("ext-ip:") || UnifiedRuleParser.IsValidCidr(body))
        {
            return FromRaw(ERuleConditionField.Ip, text);
        }
        // A recognized domain prefix wins, even if the value contains '/' (e.g. regexp:^a/b$).
        if (UnifiedRuleParser.ParseDomain(text) is { Kind: not EDomainMatchKind.Plain })
        {
            return FromRaw(ERuleConditionField.Domain, text);
        }
        // Only absolute paths count as process paths; anything else that looks like an address is an IP entry,
        // so a typo such as 10.0.0.0/33 is reported instead of silently becoming a process.
        if (text.StartsWith('/') || text.Contains('\\') || (text.Length > 2 && text[1] == ':' && char.IsLetter(text[0])))
        {
            return (ERuleConditionKind.ProcessPath, text, false);
        }
        if (text.Contains('/') && (char.IsAsciiDigit(body.FirstOrDefault()) || body.Contains(':')))
        {
            return (ERuleConditionKind.IpCidr, body, text.StartsWith('!'));
        }
        if (text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return (ERuleConditionKind.ProcessName, text, false);
        }
        return (ERuleConditionKind.DomainSuffix, text, false);
    }

    public static ECapability Capability(ERuleConditionKind kind, bool negated) => negated && FieldOf(kind) == ERuleConditionField.Ip
        ? ECapability.IpNegated
        : kind switch
        {
            ERuleConditionKind.DomainSuffix => ECapability.DomainSuffix,
            ERuleConditionKind.DomainFull => ECapability.DomainFull,
            ERuleConditionKind.DomainKeyword => ECapability.DomainKeyword,
            ERuleConditionKind.DomainRegex => ECapability.DomainRegex,
            ERuleConditionKind.GeoSite => ECapability.DomainGeoSite,
            ERuleConditionKind.Dotless => ECapability.DomainDotless,
            ERuleConditionKind.DomainPlain => ECapability.DomainPlain,
            ERuleConditionKind.DomainExternal => ECapability.DomainExternal,
            ERuleConditionKind.DomainRuleSet => ECapability.DomainRuleSet,
            ERuleConditionKind.IpCidr => ECapability.IpCidr,
            ERuleConditionKind.GeoIp => ECapability.IpGeoIP,
            ERuleConditionKind.IpPrivate => ECapability.IpPrivate,
            ERuleConditionKind.IpExternal => ECapability.IpExternal,
            ERuleConditionKind.IpRuleSet => ECapability.IpRuleSet,
            ERuleConditionKind.ProcessName => ECapability.ProcessName,
            _ => ECapability.ProcessPath,
        };

    public static string Mark(EFidelity fidelity) => fidelity switch
    {
        EFidelity.Native => "✓",
        EFidelity.Approximate => "≈",
        _ => "✗",
    };
}
