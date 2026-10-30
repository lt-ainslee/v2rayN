namespace ServiceLib.Services.CoreConfig;

public static class UnifiedPlanFactory
{
    /// <summary>
    /// Builds the plan from the shared settings in the context and validates it for the given core.
    /// </summary>
    public static UnifiedPlan ForContext(CoreConfigContext context, ECoreType core, UnifiedDiagnostics? diagnostics = null)
    {
        var plan = UnifiedPlanBuilder.Build(
            context.SimpleDnsItem,
            context.RoutingItem,
            context.AppConfig.RoutingBasicItem?.DomainStrategy,
            context.AppConfig.RuleSetItems);
        var availableRuleSets = core == ECoreType.sing_box
            ? SingboxRuleSetSources.GetAvailableNames(context.AppConfig, context.RoutingItem)
            : null;
        return UnifiedPlanValidator.Validate(plan, core, diagnostics ?? new UnifiedDiagnostics(), availableRuleSets);
    }

    /// <summary>
    /// The explicit DNS policies from the DNS settings, in priority order.
    /// </summary>
    public static List<DnsPolicy> ExplicitDnsPolicies(SimpleDNSItem? simpleDns)
    {
        var lines = UnifiedRuleParser.SplitLines(simpleDns?.DnsPolicy);
        return lines.Select(UnifiedRuleParser.ParseDnsPolicyLine).OfType<DnsPolicy>().ToList();
    }

    /// <summary>
    /// Rebuilds a RulesItem for generators that emit the native v2ray syntax, keeping only the validated values.
    /// </summary>
    public static RulesItem ToRulesItem(this UnifiedRule rule)
    {
        var source = rule.Source ?? new RulesItem { Id = rule.Id, OutboundTag = rule.Target.Name, RuleType = rule.Scope };
        var item = JsonUtils.DeepCopy(source) ?? source;
        item.Domain = source.Domain == null ? null : [.. rule.Domains.Select(d => d.Raw)];
        item.Ip = source.Ip == null ? null : [.. rule.Ips.Select(ip => ip.Raw)];
        item.Process = source.Process == null ? null : [.. rule.Processes.Select(p => p.Raw)];
        return item;
    }
}
