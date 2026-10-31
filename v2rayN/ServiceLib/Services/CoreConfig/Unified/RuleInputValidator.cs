namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Input checks for a routing rule, shared by the rule editors. Each method returns the offending
/// entries (empty when valid) so the editor can tell the user exactly what is wrong.
/// </summary>
public static class RuleInputValidator
{
    public static List<string> InvalidSourcePorts(string? sourcePort) =>
        (Utils.String2List(sourcePort) ?? []).Where(p => UnifiedRuleParser.ParsePorts(p).Count == 0).ToList();

    /// <summary>
    /// Source conditions are matched against the client address: only CIDRs and geoip:private are meaningful.
    /// </summary>
    public static List<string> InvalidSourceIps(IEnumerable<string>? sourceIps) =>
        (sourceIps ?? []).Where(t => !IsValidSourceIp(t)).ToList();

    public static bool IsValidSourceIp(string value) => UnifiedRuleParser.ParseIp(value) switch
    {
        { Negated: true } => false,
        { Kind: EIpMatchKind.Private } => true,
        { Kind: EIpMatchKind.Cidr } match => UnifiedRuleParser.IsValidCidr(match.Value),
        _ => false,
    };

    /// <summary>
    /// IP entries without a known prefix must be IP addresses or CIDRs, or the core refuses the configuration.
    /// </summary>
    public static List<string> InvalidIps(IEnumerable<string>? ips) =>
        (ips ?? []).Where(t => UnifiedRuleParser.ParseIp(t) is { Kind: EIpMatchKind.Cidr } m && !UnifiedRuleParser.IsValidCidr(m.Value)).ToList();

    public static List<string> InvalidMihomoRules(string? rules) =>
        UnifiedRuleParser.SplitLines(rules).Where(r => ClashSyntax.SplitTopLevel(r).Count < 2).ToList();

    /// <summary>
    /// Returns a user-facing message for the first problem of the rule, or null when it is valid.
    /// </summary>
    public static string? Validate(RulesItem item)
    {
        if (InvalidIps(item.Ip) is { Count: > 0 } invalidIps)
        {
            return string.Format(ResUI.TbClashOverrideInvalidLines, ResUI.TbRoutingRuleIP, string.Join("; ", invalidIps));
        }
        if (InvalidSourcePorts(item.SourcePort) is { Count: > 0 } ports)
        {
            return string.Format(ResUI.TbClashOverrideInvalidLines, ResUI.TbRuleSourcePort, string.Join("; ", ports));
        }
        if (InvalidSourceIps(item.SourceIp) is { Count: > 0 } ips)
        {
            return string.Format(ResUI.TbClashOverrideInvalidLines, ResUI.TbRuleSourceIp, string.Join("; ", ips));
        }
        if (InvalidMihomoRules(item.CoreExt?.MihomoRules) is { Count: > 0 } rules)
        {
            return string.Format(ResUI.TbClashOverrideInvalidLines, "mihomo", string.Join("; ", rules));
        }
        foreach (var (name, json) in new[] { ("Xray", item.CoreExt?.XrayJson), ("sing-box", item.CoreExt?.SingboxJson) })
        {
            if (!UnifiedJsonMerge.IsValidObject(json))
            {
                return string.Format(ResUI.TbRuleCoreExtInvalidJson, name);
            }
        }
        if (!HasCondition(item))
        {
            return string.Format(ResUI.RoutingRuleDetailRequiredTips, "Network/Port/Protocol/Domain/IP/Process");
        }
        return null;
    }

    public static bool HasCondition(RulesItem item) =>
        item.Domain?.Count > 0
        || item.Ip?.Count > 0
        || item.Protocol?.Count > 0
        || item.Process?.Count > 0
        || item.Port.IsNotEmpty()
        || item.Network.IsNotEmpty()
        || item.InboundTag?.Count > 0
        || item.SourceIp?.Count > 0
        || item.SourcePort.IsNotEmpty();
}
