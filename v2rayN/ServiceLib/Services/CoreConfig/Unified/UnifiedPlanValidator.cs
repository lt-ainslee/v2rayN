namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Checks a plan against a core's capabilities and degrades what the core cannot express.
/// Degradation only ever narrows a rule: an unsupported selector value is removed (the rule then matches less),
/// a rule whose every selector or any constraint is unsupported is dropped entirely, because removing
/// a constraint would make the rule match more traffic than the user asked for.
/// </summary>
public static class UnifiedPlanValidator
{
    /// <param name="availableRuleSets">
    /// Rule set names the core can resolve; null means those declared in the plan with a source for the core.
    /// </param>
    public static UnifiedPlan Validate(UnifiedPlan plan, ECoreType core, UnifiedDiagnostics diagnostics, IEnumerable<string>? availableRuleSets = null)
    {
        var coreName = CoreCapabilities.CoreDisplayName(core);
        var ruleSetNames = availableRuleSets?.ToHashSet() ?? core switch
        {
            ECoreType.sing_box => plan.RuleSets.Where(t => t.SingboxSource.IsNotEmpty()).Select(t => t.Name).ToHashSet(),
            ECoreType.mihomo => plan.RuleSets.Where(t => t.MihomoSource.IsNotEmpty()).Select(t => t.Name).ToHashSet(),
            _ => [],
        };

        var rules = new List<UnifiedRule>();
        foreach (var rule in plan.Rules)
        {
            if (ValidateRule(rule, core, coreName, ruleSetNames, diagnostics) is { } validRule)
            {
                rules.Add(validRule);
            }
        }

        return new UnifiedPlan
        {
            Rules = rules,
            Dns = ValidateDns(plan.Dns, core, coreName, ruleSetNames, diagnostics),
            Groups = ValidateGroups(plan.Groups, core, coreName, diagnostics),
            RuleSets = plan.RuleSets,
            DomainStrategy = plan.DomainStrategy,
        };
    }

    private static UnifiedRule? ValidateRule(UnifiedRule rule, ECoreType core, string coreName, HashSet<string> ruleSetNames, UnifiedDiagnostics diagnostics)
    {
        var name = rule.DisplayName;

        // Constraints are AND-ed: any unsupported constraint drops the whole rule.
        var unsupportedConstraint = GetUnsupportedConstraint(rule, core);
        if (unsupportedConstraint != null)
        {
            diagnostics.Warn(core, name, string.Format(ResUI.MsgUnifiedRuleSkipped, name, unsupportedConstraint, coreName));
            return null;
        }

        // Selectors are OR-ed: unsupported values are removed individually.
        var domains = FilterValues(rule.Domains, d => Check(core, CoreCapabilities.ToCapability(d), d.Kind == EDomainMatchKind.RuleSet && !ruleSetNames.Contains(d.Value)), d => d.Raw, name, core, coreName, diagnostics);
        var ips = FilterValues(rule.Ips, ip => Check(core, ip.Negated ? ECapability.IpNegated : CoreCapabilities.ToCapability(ip), ip.Kind == EIpMatchKind.RuleSet && !ruleSetNames.Contains(ip.Value), CoreCapabilities.ToCapability(ip)), ip => ip.Raw, name, core, coreName, diagnostics);
        var processes = FilterValues(rule.Processes, p => Check(core, CoreCapabilities.ToCapability(p)), p => p.Raw, name, core, coreName, diagnostics);

        if (rule.HasSelectors && domains.Count == 0 && ips.Count == 0 && processes.Count == 0)
        {
            diagnostics.Warn(core, name, string.Format(ResUI.MsgUnifiedRuleSkipped, name, ResUI.MsgUnifiedNoSupportedCondition, coreName));
            return null;
        }

        return new UnifiedRule
        {
            Id = rule.Id,
            Remarks = rule.Remarks,
            Scope = rule.Scope,
            Target = rule.Target,
            Domains = domains,
            Ips = ips,
            Processes = processes,
            Ports = rule.Ports,
            Networks = rule.Networks,
            Protocols = rule.Protocols,
            InboundTags = rule.InboundTags,
            SourceIps = rule.SourceIps,
            SourcePorts = rule.SourcePorts,
            CoreExt = rule.CoreExt,
            Source = rule.Source,
        };
    }

    private static string? GetUnsupportedConstraint(UnifiedRule rule, ECoreType core)
    {
        if (rule.Protocols.Count > 0 && CoreCapabilities.Get(core, ECapability.Protocol) == EFidelity.Unsupported)
        {
            return $"protocol: {string.Join(",", rule.Protocols)}";
        }
        if (rule.InboundTags.Count > 0 && CoreCapabilities.Get(core, ECapability.InboundTag) == EFidelity.Unsupported)
        {
            return $"inboundTag: {string.Join(",", rule.InboundTags)}";
        }
        if (rule.SourceIps.Count > 0)
        {
            if (CoreCapabilities.Get(core, ECapability.SourceIp) == EFidelity.Unsupported
                || rule.SourceIps.Any(ip => ip.Kind is not (EIpMatchKind.Cidr or EIpMatchKind.Private) || ip.Negated))
            {
                return $"sourceIp: {string.Join(",", rule.SourceIps.Select(t => t.Raw))}";
            }
        }
        if (rule.SourcePorts.Count > 0 && CoreCapabilities.Get(core, ECapability.SourcePort) == EFidelity.Unsupported)
        {
            return $"sourcePort: {string.Join(",", rule.SourcePorts)}";
        }
        return null;
    }

    private enum ECheck
    {
        Keep,
        KeepApproximate,
        Drop,
        DropMissingRuleSet,
    }

    private static ECheck Check(ECoreType core, ECapability capability, bool missingRuleSet = false, ECapability? valueCapability = null)
    {
        var fidelity = CoreCapabilities.Get(core, capability);
        if (valueCapability is { } inner && fidelity != EFidelity.Unsupported)
        {
            // A negated value also needs its inner kind to be supported.
            var innerFidelity = CoreCapabilities.Get(core, inner);
            fidelity = innerFidelity > fidelity ? innerFidelity : fidelity;
        }
        return fidelity switch
        {
            EFidelity.Unsupported => ECheck.Drop,
            _ when missingRuleSet => ECheck.DropMissingRuleSet,
            EFidelity.Approximate => ECheck.KeepApproximate,
            _ => ECheck.Keep,
        };
    }

    private static List<T> FilterValues<T>(List<T> values, Func<T, ECheck> check, Func<T, string> display,
        string ruleName, ECoreType core, string coreName, UnifiedDiagnostics diagnostics)
    {
        var result = new List<T>();
        foreach (var value in values)
        {
            switch (check(value))
            {
                case ECheck.Keep:
                    result.Add(value);
                    break;

                case ECheck.KeepApproximate:
                    result.Add(value);
                    diagnostics.Info(core, ruleName, string.Format(ResUI.MsgUnifiedApproximated, ruleName, display(value), coreName));
                    break;

                case ECheck.DropMissingRuleSet:
                    diagnostics.Warn(core, ruleName, string.Format(ResUI.MsgUnifiedRuleSetMissing, ruleName, display(value), coreName));
                    break;

                default:
                    diagnostics.Warn(core, ruleName, string.Format(ResUI.MsgUnifiedValueIgnored, ruleName, display(value), coreName));
                    break;
            }
        }
        return result;
    }

    private static UnifiedDns ValidateDns(UnifiedDns dns, ECoreType core, string coreName, HashSet<string> ruleSetNames, UnifiedDiagnostics diagnostics)
    {
        void CheckFeature(bool enabled, ECapability capability, string label)
        {
            if (!enabled)
            {
                return;
            }
            switch (CoreCapabilities.Get(core, capability))
            {
                case EFidelity.Unsupported:
                    diagnostics.Info(core, "DNS", string.Format(ResUI.MsgUnifiedDnsIgnored, label, coreName));
                    break;

                case EFidelity.Approximate:
                    diagnostics.Info(core, "DNS", string.Format(ResUI.MsgUnifiedApproximated, "DNS", label, coreName));
                    break;
            }
        }

        CheckFeature(dns.ServeStale, ECapability.DnsServeStale, "serve-stale");
        CheckFeature(dns.ParallelQuery, ECapability.DnsParallelQuery, "parallel-query");
        CheckFeature(dns.DirectExpectedIps.Count > 0, ECapability.DnsExpectedIps, "expected-ips");
        CheckFeature(dns.BlockAAAA, ECapability.DnsBlockAAAA, "block-AAAA");
        CheckFeature(dns.BlockBindingQuery, ECapability.DnsBlockBindingQuery, "block-SVCB/HTTPS");

        var policies = new List<DnsPolicy>();
        foreach (var policy in dns.Policies)
        {
            if (policy.Target == EDnsPolicyTarget.Block && CoreCapabilities.Get(core, ECapability.DnsPolicyBlock) == EFidelity.Unsupported)
            {
                // A block derived from a routing rule still blocks the connection, so only explicit policies are reported.
                if (policy.IsExplicit)
                {
                    diagnostics.Info(core, "DNS", string.Format(ResUI.MsgUnifiedDnsIgnored, $"block: {string.Join(",", policy.Domains.Select(d => d.Raw))}", coreName));
                }
                continue;
            }
            var domains = policy.Domains.Where(d =>
            {
                var capability = d.Kind switch
                {
                    EDomainMatchKind.Keyword or EDomainMatchKind.Plain or EDomainMatchKind.Dotless => ECapability.DnsPolicyKeyword,
                    EDomainMatchKind.Regex => ECapability.DnsPolicyRegex,
                    _ => CoreCapabilities.ToCapability(d),
                };
                var missingRuleSet = d.Kind == EDomainMatchKind.RuleSet && !ruleSetNames.Contains(d.Value);
                if (CoreCapabilities.Get(core, capability) != EFidelity.Unsupported && !missingRuleSet)
                {
                    return true;
                }
                // For policies derived from routing rules the connection is still routed by the rule; only the
                // choice of DNS server falls back to the default, so only explicit policies are reported.
                if (policy.IsExplicit)
                {
                    diagnostics.Info(core, "DNS", string.Format(ResUI.MsgUnifiedDnsIgnored, d.Raw, coreName));
                }
                return false;
            }).ToList();
            if (domains.Count > 0)
            {
                policies.Add(policy with { Domains = domains });
            }
        }

        return new UnifiedDns
        {
            DirectServers = dns.DirectServers,
            RemoteServers = dns.RemoteServers,
            BootstrapServers = dns.BootstrapServers,
            FakeIp = dns.FakeIp,
            GlobalFakeIp = dns.GlobalFakeIp,
            FakeIpRange = dns.FakeIpRange,
            Hosts = dns.Hosts,
            UseSystemHosts = dns.UseSystemHosts,
            AddCommonHosts = dns.AddCommonHosts,
            BlockAAAA = dns.BlockAAAA,
            BlockBindingQuery = dns.BlockBindingQuery,
            ServeStale = dns.ServeStale,
            ParallelQuery = dns.ParallelQuery,
            DirectExpectedIps = dns.DirectExpectedIps,
            Policies = policies,
            FinalDirect = dns.FinalDirect,
        };
    }

    private static List<UnifiedGroup> ValidateGroups(List<UnifiedGroup> groups, ECoreType core, string coreName, UnifiedDiagnostics diagnostics)
    {
        foreach (var group in groups)
        {
            if (CoreCapabilities.Get(core, CoreCapabilities.ToCapability(group.Kind)) == EFidelity.Approximate)
            {
                diagnostics.Info(core, group.Name, string.Format(ResUI.MsgUnifiedApproximated, group.Name, group.Kind, coreName));
            }
        }
        return groups;
    }
}
