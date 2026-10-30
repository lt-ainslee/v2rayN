namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Maps IR rule conditions onto sing-box rule fields.
/// </summary>
public static class SingboxRuleTranslator
{
    /// <summary>
    /// Returns false when the condition cannot be expressed in sing-box.
    /// </summary>
    public static bool ApplyDomain(DomainMatch match, Rule4Sbox rule)
    {
        switch (match.Kind)
        {
            case EDomainMatchKind.GeoSite:
                rule.geosite ??= [];
                rule.geosite.Add(match.Value);
                return true;

            case EDomainMatchKind.Regex:
                rule.domain_regex ??= [];
                rule.domain_regex.Add(match.Value);
                return true;

            case EDomainMatchKind.Suffix:
                rule.domain_suffix ??= [];
                rule.domain_suffix.Add(match.Value);
                return true;

            case EDomainMatchKind.Full:
                rule.domain ??= [];
                rule.domain.Add(match.Value);
                return true;

            case EDomainMatchKind.Keyword:
            case EDomainMatchKind.Dotless:
            case EDomainMatchKind.Plain:
                rule.domain_keyword ??= [];
                rule.domain_keyword.Add(match.Value);
                return true;

            case EDomainMatchKind.RuleSet:
                rule.rule_set ??= [];
                rule.rule_set.Add(match.Value);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Applies the IP condition ignoring negation (callers build the inverted rule).
    /// Returns false when the condition cannot be expressed in sing-box.
    /// </summary>
    public static bool ApplyIp(IpMatch match, Rule4Sbox rule)
    {
        switch (match.Kind)
        {
            case EIpMatchKind.Private:
                rule.ip_is_private = true;
                return true;

            case EIpMatchKind.GeoIP:
                rule.geoip ??= [];
                rule.geoip.Add(match.Value);
                return true;

            case EIpMatchKind.Cidr:
                rule.ip_cidr ??= [];
                rule.ip_cidr.Add(match.Value);
                return true;

            case EIpMatchKind.RuleSet:
                rule.rule_set ??= [];
                rule.rule_set.Add(match.Value);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Applies source IP / source port constraints.
    /// </summary>
    public static void ApplySource(UnifiedRule unified, Rule4Sbox rule)
    {
        foreach (var ip in unified.SourceIps.Where(t => !t.Negated))
        {
            if (ip.Kind == EIpMatchKind.Private)
            {
                rule.source_ip_is_private = true;
            }
            else if (ip.Kind == EIpMatchKind.Cidr)
            {
                rule.source_ip_cidr ??= [];
                rule.source_ip_cidr.Add(ip.Value);
            }
        }
        var singlePorts = unified.SourcePorts.Where(t => t.IsSingle).ToList();
        var rangePorts = unified.SourcePorts.Where(t => !t.IsSingle).ToList();
        if (singlePorts.Count == 1 && rangePorts.Count == 0)
        {
            rule.source_port = singlePorts[0].From;
        }
        else if (unified.SourcePorts.Count > 0)
        {
            rule.source_port_range = [.. unified.SourcePorts.Select(t => $"{t.From}:{t.To}")];
        }
    }

    /// <summary>
    /// Merges the object-level extension (a JSON object) into the generated rule.
    /// </summary>
    public static Rule4Sbox ApplyExtension(Rule4Sbox rule, RuleCoreExtension? ext)
    {
        return ext?.SingboxJson.IsNullOrEmpty() == false
            ? UnifiedJsonMerge.Merge(rule, ext.SingboxJson) ?? rule
            : rule;
    }
}
