namespace ServiceLib.Services.CoreConfig;

/// <param name="Mark">✓ fully supported, ≈ approximated, ! partly dropped, ✗ skipped entirely</param>
public sealed record RuleCoreStatus(ECoreType Core, string Mark, IReadOnlyList<string> Messages);

/// <summary>
/// How one routing rule ends up in each core, computed with the same validator and translators
/// used to generate the configurations, so the editor shows exactly what will happen.
/// </summary>
public static class RuleCompatibility
{
    public const string Full = "✓";
    public const string Approximate = "≈";
    public const string Partial = "!";
    public const string Skipped = "✗";

    /// <param name="availableRuleSets">Rule set names each core can resolve.</param>
    public static List<RuleCoreStatus> Evaluate(RulesItem rule, IReadOnlyDictionary<ECoreType, HashSet<string>> availableRuleSets)
    {
        var item = JsonUtils.DeepCopy(rule) ?? rule;
        item.Enabled = true;
        // Only the routing side is judged here; DNS policies are reported in the preview.
        item.RuleType = ERuleType.Routing;
        var plan = UnifiedPlanBuilder.Build(null, new RoutingItem { RuleSet = JsonUtils.Serialize(new List<RulesItem> { item }) }, Global.AsIs);

        var result = new List<RuleCoreStatus>();
        foreach (var core in CoreCapabilities.UnifiedCores)
        {
            var diagnostics = new UnifiedDiagnostics();
            var validated = UnifiedPlanValidator.Validate(plan, core, diagnostics, availableRuleSets.GetValueOrDefault(core) ?? []);
            var skipped = plan.Rules.Count > 0 && validated.Rules.Count == 0;
            if (!skipped && core == ECoreType.mihomo && validated.Rules.Count > 0)
            {
                // Some limits only show while translating (e.g. a comma inside a regex).
                var translated = new MihomoTranslator(validated, new MihomoExtension(), false, diagnostics)
                    .TranslateRule(validated.Rules[0], "PROXY");
                skipped = translated.Count == 0 && RuleInputValidator.HasCondition(item);
            }
            var mark = skipped
                ? Skipped
                : diagnostics.Items.Any(d => d.Level == EDiagnosticLevel.Warning)
                    ? Partial
                    : diagnostics.Items.Count > 0 ? Approximate : Full;
            result.Add(new RuleCoreStatus(core, mark, diagnostics.Items.Select(d => d.Message).Distinct().ToList()));
        }
        return result;
    }
}
