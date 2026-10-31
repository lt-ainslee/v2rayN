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
    /// Applies source conditions and the per-rule extension to the rules generated from one IR rule,
    /// i.e. rules[start..].
    /// </summary>
    public static void ApplyToGenerated(UnifiedRule unified, List<Rule4Sbox> rules, int start)
    {
        for (var i = start; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (unified.SourceIps.Count > 0 || unified.SourcePorts.Count > 0)
            {
                if (rule.type == "logical")
                {
                    // A logical rule only carries mode/rules/action, so AND the source on top of it.
                    var source = new Rule4Sbox();
                    ApplySource(unified, source);
                    var inner = JsonUtils.DeepCopy(rule) ?? rule;
                    inner.outbound = null;
                    inner.action = null;
                    rule = new Rule4Sbox
                    {
                        type = "logical",
                        mode = "and",
                        outbound = rule.outbound,
                        action = rule.action,
                        rules = [inner, source],
                    };
                }
                else
                {
                    ApplySource(unified, rule);
                }
            }
            if (unified.CoreExt?.SingboxJson.IsNullOrEmpty() == false)
            {
                rule = UnifiedJsonMerge.Merge(rule, unified.CoreExt.SingboxJson) ?? rule;
            }
            rules[i] = rule;
        }
    }
}
