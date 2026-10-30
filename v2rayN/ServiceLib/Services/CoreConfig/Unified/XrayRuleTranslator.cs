namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Xray consumes the native v2ray rule syntax directly; this adds the IR parts that the
/// generic rule copy does not carry (source conditions and the per-rule extension).
/// </summary>
public static class XrayRuleTranslator
{
    /// <summary>
    /// Applies source conditions and the per-rule extension to the rules generated from one IR rule,
    /// i.e. rules[start..].
    /// </summary>
    public static void ApplyToGenerated(UnifiedRule unified, List<RulesItem4Ray> rules, int start)
    {
        for (var i = start; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (unified.SourceIps.Count > 0)
            {
                rule.source = [.. unified.SourceIps.Select(ip => ip.Kind == EIpMatchKind.Private ? $"{Global.GeoIPPrefix}private" : ip.Value)];
            }
            if (unified.SourcePorts.Count > 0)
            {
                rule.sourcePort = string.Join(",", unified.SourcePorts);
            }
            if (unified.CoreExt?.XrayJson.IsNullOrEmpty() == false)
            {
                rules[i] = UnifiedJsonMerge.Merge(rule, unified.CoreExt.XrayJson) ?? rule;
            }
        }
    }
}
