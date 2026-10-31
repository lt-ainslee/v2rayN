namespace ServiceLib.ViewModels;

/// <summary>
/// One structured condition of a rule (domain, IP or process entry) with its per-core support.
/// </summary>
public partial class RuleConditionRowViewModel : ReactiveObject
{
    public static IReadOnlyList<string> KindNames { get; } = [.. RuleConditionKinds.All.Select(RuleConditionKinds.DisplayName)];

    /// <summary>Instance access to <see cref="KindNames" /> for item templates.</summary>
    public IReadOnlyList<string> KindOptions => KindNames;

    [Reactive] public partial string KindName { get; set; }
    [Reactive] public partial string Value { get; set; }
    [Reactive] public partial bool Negated { get; set; }
    [Reactive] public partial bool CanNegate { get; set; }
    [Reactive] public partial bool HasValue { get; set; }
    [Reactive] public partial string XrayMark { get; set; }
    [Reactive] public partial string SingboxMark { get; set; }
    [Reactive] public partial string MihomoMark { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> RemoveCmd { get; }

    public ERuleConditionKind Kind => RuleConditionKinds.All[Math.Max(0, KindNames.ToList().IndexOf(KindName))];

    public RuleConditionRowViewModel(ERuleConditionKind kind, string value, bool negated,
        Action<RuleConditionRowViewModel> remove, Action changed)
    {
        KindName = RuleConditionKinds.DisplayName(kind);
        Value = value;
        Negated = negated;
        XrayMark = SingboxMark = MihomoMark = string.Empty;
        RemoveCmd = ReactiveCommand.Create(() => remove(this));

        this.WhenAnyValue(x => x.KindName, x => x.Value, x => x.Negated)
            .Skip(1)
            .Subscribe(_ =>
            {
                Update();
                changed();
            });
        Update();
    }

    private void Update()
    {
        var kind = Kind;
        CanNegate = RuleConditionKinds.FieldOf(kind) == ERuleConditionField.Ip;
        HasValue = RuleConditionKinds.HasValue(kind);
        var negated = Negated && CanNegate;
        XrayMark = MarkFor(ECoreType.Xray, kind, negated);
        SingboxMark = MarkFor(ECoreType.sing_box, kind, negated);
        MihomoMark = MarkFor(ECoreType.mihomo, kind, negated);
    }

    private static string MarkFor(ECoreType core, ERuleConditionKind kind, bool negated)
    {
        var fidelity = CoreCapabilities.Get(core, RuleConditionKinds.Capability(kind, negated));
        if (negated)
        {
            // A negated value also needs its inner kind.
            var inner = CoreCapabilities.Get(core, RuleConditionKinds.Capability(kind, false));
            fidelity = inner > fidelity ? inner : fidelity;
        }
        return RuleConditionKinds.Mark(fidelity);
    }

    /// <summary>The stored entry, or null when the row has no value yet.</summary>
    public string? ToRaw()
    {
        var kind = Kind;
        if (RuleConditionKinds.HasValue(kind) && Value.IsNullOrEmpty())
        {
            return null;
        }
        return RuleConditionKinds.ToRaw(kind, Value, Negated && CanNegate);
    }
}

/// <summary>
/// A check box option such as a protocol or an inbound tag.
/// </summary>
public partial class RuleCheckOption : ReactiveObject
{
    public string Name { get; }

    [Reactive] public partial bool IsChecked { get; set; }

    public RuleCheckOption(string name, bool isChecked, Action changed)
    {
        Name = name;
        IsChecked = isChecked;
        this.WhenAnyValue(x => x.IsChecked).Skip(1).Subscribe(_ => changed());
    }
}

/// <summary>
/// A rule in the list: a summary plus how each core handles it.
/// </summary>
public partial class RuleListItemViewModel : ReactiveObject
{
    public RulesItem Rule { get; }

    [Reactive] public partial string Title { get; set; }
    [Reactive] public partial string Summary { get; set; }
    [Reactive] public partial string Target { get; set; }
    [Reactive] public partial bool Enabled { get; set; }
    [Reactive] public partial string XrayMark { get; set; }
    [Reactive] public partial string SingboxMark { get; set; }
    [Reactive] public partial string MihomoMark { get; set; }

    public IReadOnlyList<RuleCoreStatus> Statuses { get; private set; } = [];

    public RuleListItemViewModel(RulesItem rule, Action<RuleListItemViewModel>? enabledChanged = null)
    {
        Rule = rule;
        Title = Summary = Target = XrayMark = SingboxMark = MihomoMark = string.Empty;
        Enabled = rule.Enabled;
        this.WhenAnyValue(x => x.Enabled).Skip(1).Subscribe(enabled =>
        {
            if (Rule.Enabled == enabled)
            {
                return;
            }
            Rule.Enabled = enabled;
            enabledChanged?.Invoke(this);
        });
    }

    public void Refresh(IReadOnlyDictionary<ECoreType, HashSet<string>> availableRuleSets)
    {
        Enabled = Rule.Enabled;
        var conditions = new List<string>([.. Rule.Domain ?? [], .. Rule.Ip ?? [], .. Rule.Process ?? [], .. Rule.SourceIp ?? []]);
        if (Rule.Port.IsNotEmpty())
        {
            conditions.Add($"port:{Rule.Port}");
        }
        if (Rule.Network.IsNotEmpty())
        {
            conditions.Add($"network:{Rule.Network}");
        }
        conditions.AddRange((Rule.Protocol ?? []).Select(p => $"protocol:{p}"));
        conditions.AddRange((Rule.InboundTag ?? []).Select(p => $"inbound:{p}"));
        Summary = conditions.Count <= 3 ? string.Join(", ", conditions) : $"{string.Join(", ", conditions.Take(3))} … (+{conditions.Count - 3})";
        Title = Rule.Remarks.IsNullOrEmpty() ? (conditions.FirstOrDefault() ?? ResUI.TbRuleCenterNewRule) : Rule.Remarks;
        Target = $"{(Rule.RuleType is null or ERuleType.ALL ? string.Empty : $"[{Rule.RuleType}] ")}→ {Rule.OutboundTag ?? Global.ProxyTag}";

        Statuses = RuleCompatibility.Evaluate(Rule, availableRuleSets);
        XrayMark = Statuses.First(s => s.Core == ECoreType.Xray).Mark;
        SingboxMark = Statuses.First(s => s.Core == ECoreType.sing_box).Mark;
        MihomoMark = Statuses.First(s => s.Core == ECoreType.mihomo).Mark;
    }
}

/// <summary>
/// One declared rule set (referenced as ruleset:name).
/// </summary>
public partial class RuleSetRowViewModel : ReactiveObject
{
    public static IReadOnlyList<string> BehaviorOptionsList { get; } = Global.ClashRuleProviderBehaviors;

    public IReadOnlyList<string> BehaviorOptions => BehaviorOptionsList;

    [Reactive] public partial string Name { get; set; }
    [Reactive] public partial string Behavior { get; set; }
    [Reactive] public partial string SingboxSource { get; set; }
    [Reactive] public partial string MihomoSource { get; set; }
    [Reactive] public partial string Interval { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> RemoveCmd { get; }

    public RuleSetRowViewModel(RuleSetItem item, Action<RuleSetRowViewModel> remove, Action changed)
    {
        Name = item.Name;
        Behavior = Global.ClashRuleProviderBehaviors.Contains(item.Behavior) ? item.Behavior : Global.ClashRuleProviderBehaviors.First();
        SingboxSource = item.SingboxSource ?? string.Empty;
        MihomoSource = item.MihomoSource ?? string.Empty;
        Interval = item.UpdateInterval.ToString();
        RemoveCmd = ReactiveCommand.Create(() => remove(this));
        this.WhenAnyValue(x => x.Name, x => x.SingboxSource, x => x.MihomoSource)
            .Skip(1)
            .Subscribe(_ => changed());
    }

    public RuleSetItem ToItem() => new()
    {
        Name = Name.Trim(),
        Behavior = Behavior,
        SingboxSource = SingboxSource.Trim().NullIfEmpty(),
        MihomoSource = MihomoSource.Trim().NullIfEmpty(),
        UpdateInterval = Interval.ToInt(86400),
    };
}

public sealed record PolicyGroupListItem(ProfileItem Profile, string Remarks, string TypeName, string Members);
