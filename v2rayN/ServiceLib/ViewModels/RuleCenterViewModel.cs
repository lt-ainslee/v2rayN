namespace ServiceLib.ViewModels;

/// <summary>
/// Structured editor for the selected rule. Every change is written straight into the rule's working
/// copy; nothing is persisted until the rule center is saved.
/// </summary>
public partial class RuleEditorViewModel : ReactiveObject
{
    public static IReadOnlyList<string> RuleTypes { get; } = ["", nameof(ERuleType.ALL), nameof(ERuleType.Routing), nameof(ERuleType.DNS)];
    public static IReadOnlyList<string> Networks { get; } = Global.RuleNetworks;

    private readonly Action _changed;
    private RuleListItemViewModel? _current;
    private bool _loading;

    [Reactive] public partial bool HasRule { get; set; }
    [Reactive] public partial string Remarks { get; set; }
    [Reactive] public partial bool Enabled { get; set; }
    [Reactive] public partial string RuleType { get; set; }
    [Reactive] public partial string OutboundTag { get; set; }
    [Reactive] public partial string Port { get; set; }
    [Reactive] public partial string Network { get; set; }
    [Reactive] public partial string SourceIp { get; set; }
    [Reactive] public partial string SourcePort { get; set; }
    [Reactive] public partial string XrayJson { get; set; }
    [Reactive] public partial string SingboxJson { get; set; }
    [Reactive] public partial string MihomoRules { get; set; }
    [Reactive] public partial string BulkText { get; set; }
    [Reactive] public partial string CompatibilityText { get; set; }

    public ObservableCollection<RuleConditionRowViewModel> Conditions { get; } = [];
    public ObservableCollection<RuleCheckOption> Protocols { get; } = [];
    public ObservableCollection<RuleCheckOption> InboundTags { get; } = [];
    public BulkObservableCollection<string> OutboundOptions { get; } = [];

    public ReactiveCommand<RxVoid, RxVoid> AddConditionCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddBulkCmd { get; }

    public RuleEditorViewModel(Action changed)
    {
        _changed = changed;
        Remarks = RuleType = OutboundTag = Port = Network = SourceIp = SourcePort = string.Empty;
        XrayJson = SingboxJson = MihomoRules = BulkText = CompatibilityText = string.Empty;
        AddConditionCmd = ReactiveCommand.Create(() => AddCondition(ERuleConditionKind.DomainSuffix, string.Empty, false));
        AddBulkCmd = ReactiveCommand.Create(AddBulk);

        this.WhenAnyValue(x => x.Remarks, x => x.Enabled, x => x.RuleType, x => x.OutboundTag, x => x.Port, x => x.Network)
            .Subscribe(_ => WriteBack());
        this.WhenAnyValue(x => x.SourceIp, x => x.SourcePort, x => x.XrayJson, x => x.SingboxJson, x => x.MihomoRules)
            .Subscribe(_ => WriteBack());
    }

    public void Load(RuleListItemViewModel? item)
    {
        _loading = true;
        try
        {
            _current = item;
            HasRule = item != null;
            var rule = item?.Rule ?? new RulesItem();
            Remarks = rule.Remarks ?? string.Empty;
            Enabled = rule.Enabled;
            RuleType = rule.RuleType?.ToString() ?? string.Empty;
            OutboundTag = rule.OutboundTag ?? Global.ProxyTag;
            Port = rule.Port ?? string.Empty;
            Network = rule.Network ?? string.Empty;
            SourceIp = Utils.List2String(rule.SourceIp, true) ?? string.Empty;
            SourcePort = rule.SourcePort ?? string.Empty;
            XrayJson = rule.CoreExt?.XrayJson ?? string.Empty;
            SingboxJson = rule.CoreExt?.SingboxJson ?? string.Empty;
            MihomoRules = rule.CoreExt?.MihomoRules ?? string.Empty;
            BulkText = string.Empty;

            Conditions.Clear();
            foreach (var (field, values) in new[]
                     {
                         (ERuleConditionField.Domain, rule.Domain), (ERuleConditionField.Ip, rule.Ip),
                         (ERuleConditionField.Process, rule.Process),
                     })
            {
                foreach (var raw in values ?? [])
                {
                    if (RuleConditionKinds.FromRaw(field, raw) is { } c)
                    {
                        AddCondition(c.Kind, c.Value, c.Negated);
                    }
                }
            }

            Protocols.Clear();
            foreach (var name in Global.RuleProtocols)
            {
                Protocols.Add(new RuleCheckOption(name, rule.Protocol?.Contains(name) == true, WriteBack));
            }
            InboundTags.Clear();
            foreach (var name in Global.InboundTags)
            {
                InboundTags.Add(new RuleCheckOption(name, rule.InboundTag?.Contains(name) == true, WriteBack));
            }
        }
        finally
        {
            _loading = false;
        }
        UpdateCompatibilityText();
    }

    private void AddCondition(ERuleConditionKind kind, string value, bool negated)
    {
        Conditions.Add(new RuleConditionRowViewModel(kind, value, negated, row =>
        {
            Conditions.Remove(row);
            WriteBack();
        }, WriteBack));
        WriteBack();
    }

    /// <summary>
    /// Adds pasted entries (one per line or comma separated), guessing each condition type.
    /// </summary>
    private void AddBulk()
    {
        foreach (var text in (BulkText ?? string.Empty).Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (RuleConditionKinds.Guess(text) is { } c)
            {
                AddCondition(c.Kind, c.Value, c.Negated);
            }
        }
        BulkText = string.Empty;
    }

    private void WriteBack()
    {
        if (_loading || _current == null)
        {
            return;
        }
        var rule = _current.Rule;
        rule.Remarks = Remarks.NullIfEmpty();
        rule.Enabled = Enabled;
        rule.RuleType = RuleType.IsNullOrEmpty() ? null : Enum.Parse<ERuleType>(RuleType);
        rule.OutboundTag = OutboundTag.NullIfEmpty() ?? Global.ProxyTag;
        rule.Port = Port.NullIfEmpty();
        rule.Network = Network.NullIfEmpty();
        rule.SourceIp = Utils.String2List(Utils.Convert2Comma(SourceIp)) is { Count: > 0 } ips ? ips : null;
        rule.SourcePort = SourcePort.NullIfEmpty();
        var coreExt = new RuleCoreExtension
        {
            XrayJson = XrayJson.NullIfEmpty(),
            SingboxJson = SingboxJson.NullIfEmpty(),
            MihomoRules = MihomoRules.NullIfEmpty(),
        };
        rule.CoreExt = coreExt.IsEmpty() ? null : coreExt;

        List<string>? Collect(ERuleConditionField field)
        {
            var values = Conditions.Where(c => RuleConditionKinds.FieldOf(c.Kind) == field)
                .Select(c => c.ToRaw()).OfType<string>().ToList();
            return values.Count > 0 ? values : null;
        }
        rule.Domain = Collect(ERuleConditionField.Domain);
        rule.Ip = Collect(ERuleConditionField.Ip);
        rule.Process = Collect(ERuleConditionField.Process);
        rule.Protocol = Protocols.Where(p => p.IsChecked).Select(p => p.Name).ToList() is { Count: > 0 } protocols ? protocols : null;
        rule.InboundTag = InboundTags.Where(p => p.IsChecked).Select(p => p.Name).ToList() is { Count: > 0 } tags ? tags : null;

        _changed();
        UpdateCompatibilityText();
    }

    public void UpdateCompatibilityText()
    {
        if (_current == null)
        {
            CompatibilityText = string.Empty;
            return;
        }
        var lines = new List<string>();
        foreach (var status in _current.Statuses)
        {
            var name = CoreCapabilities.CoreDisplayName(status.Core);
            if (status.Messages.Count == 0)
            {
                lines.Add($"{status.Mark} {name}");
                continue;
            }
            lines.AddRange(status.Messages.Select(m => $"{status.Mark} {name}: {m}"));
        }
        CompatibilityText = string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// One place for everything the cores share: routing rules, DNS, policy groups, rule sets,
/// per-core extensions, and a preview of what each core receives.
/// </summary>
public partial class RuleCenterViewModel : MyReactiveObject, ICloseable
{
    public event EventHandler? RequestClose;

    private readonly Dictionary<string, List<RulesItem>> _editedRules = [];
    private Dictionary<ECoreType, HashSet<string>> _availableRuleSets = [];
    private string _savedActiveProfileId = string.Empty;

    public DNSSettingViewModel Dns { get; }
    public CoreExtensionSettingViewModel Extensions { get; }
    public RuleEditorViewModel Editor { get; }

    public BulkObservableCollection<RoutingItem> Profiles { get; } = [];
    [Reactive] public partial RoutingItem? SelectedProfile { get; set; }
    [Reactive] public partial string ActiveProfileId { get; set; }
    [Reactive] public partial bool IsSelectedProfileActive { get; set; }

    public ObservableCollection<RuleListItemViewModel> Rules { get; } = [];
    [Reactive] public partial RuleListItemViewModel? SelectedRule { get; set; }

    public ObservableCollection<RuleSetRowViewModel> RuleSets { get; } = [];

    public BulkObservableCollection<PolicyGroupListItem> PolicyGroups { get; } = [];
    [Reactive] public partial PolicyGroupListItem? SelectedPolicyGroup { get; set; }

    public static IReadOnlyList<string> PreviewCores => CoreExtensionSettingViewModel.PreviewCores;
    [Reactive] public partial string PreviewCore { get; set; }
    [Reactive] public partial string PreviewText { get; set; }
    public IReadOnlyList<CoreCapabilityRow> CapabilityRows { get; } = CoreCapabilityMatrix.Rows();

    public ReactiveCommand<RxVoid, RxVoid> SetActiveProfileCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddRuleCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> DuplicateRuleCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveRuleCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> MoveUpCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> MoveDownCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddRuleSetCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddPolicyGroupCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddProxyChainCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> EditPolicyGroupCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> PreviewCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCmd { get; }

    public RuleCenterViewModel()
    {
        _config = AppManager.Instance.Config;
        Dns = new DNSSettingViewModel();
        Extensions = new CoreExtensionSettingViewModel();
        Editor = new RuleEditorViewModel(OnRuleEdited);
        ActiveProfileId = string.Empty;
        PreviewCore = PreviewCores[0];
        PreviewText = string.Empty;

        var hasRule = this.WhenAnyValue(x => x.SelectedRule).Select(r => r != null);
        var hasGroup = this.WhenAnyValue(x => x.SelectedPolicyGroup).Select(g => g != null);
        SetActiveProfileCmd = ReactiveCommand.Create(() =>
        {
            ActiveProfileId = SelectedProfile?.Id ?? ActiveProfileId;
        });
        AddRuleCmd = ReactiveCommand.Create(() => InsertRule(new RulesItem
        {
            Id = Utils.GetGuid(false),
            OutboundTag = Global.ProxyTag,
            Enabled = true,
        }));
        DuplicateRuleCmd = ReactiveCommand.Create(() =>
        {
            if (SelectedRule is { } current && JsonUtils.DeepCopy(current.Rule) is { } copy)
            {
                copy.Id = Utils.GetGuid(false);
                InsertRule(copy);
            }
        }, hasRule);
        RemoveRuleCmd = ReactiveCommand.Create(RemoveRule, hasRule);
        MoveUpCmd = ReactiveCommand.Create(() => MoveRule(-1), hasRule);
        MoveDownCmd = ReactiveCommand.Create(() => MoveRule(1), hasRule);
        AddRuleSetCmd = ReactiveCommand.Create(() =>
        {
            RuleSets.Add(CreateRuleSetRow(new RuleSetItem()));
            OnRuleSetsChanged();
        });
        AddPolicyGroupCmd = ReactiveCommand.CreateFromTask(() => EditPolicyGroupAsync(new ProfileItem
        {
            Subid = _config.SubIndexId,
            ConfigType = EConfigType.PolicyGroup,
            IsSub = false,
        }));
        AddProxyChainCmd = ReactiveCommand.CreateFromTask(() => EditPolicyGroupAsync(new ProfileItem
        {
            Subid = _config.SubIndexId,
            ConfigType = EConfigType.ProxyChain,
            IsSub = false,
        }));
        EditPolicyGroupCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedPolicyGroup is { } group)
            {
                await EditPolicyGroupAsync(group.Profile);
            }
        }, hasGroup);
        PreviewCmd = ReactiveCommand.CreateFromTask(PreviewAsync);
        SaveCmd = ReactiveCommand.CreateFromTask(SaveAsync);

        this.WhenAnyValue(x => x.SelectedProfile).Subscribe(LoadProfile);
        this.WhenAnyValue(x => x.SelectedRule).Subscribe(Editor.Load);
        this.WhenAnyValue(x => x.SelectedProfile, x => x.ActiveProfileId)
            .Subscribe(_ => IsSelectedProfileActive = SelectedProfile != null && SelectedProfile.Id == ActiveProfileId);

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        foreach (var item in _config.RuleSetItems ?? [])
        {
            RuleSets.Add(CreateRuleSetRow(item));
        }
        var profiles = await AppManager.Instance.RoutingItems() ?? [];
        Profiles.AddRange(profiles);
        var active = profiles.FirstOrDefault(p => p.IsActive) ?? profiles.FirstOrDefault();
        _savedActiveProfileId = active?.Id ?? string.Empty;
        ActiveProfileId = _savedActiveProfileId;
        RefreshAvailableRuleSets();
        SelectedProfile = active;
        await LoadPolicyGroupsAsync();
    }

    #region Rules

    private void LoadProfile(RoutingItem? profile)
    {
        Rules.Clear();
        if (profile == null)
        {
            SelectedRule = null;
            return;
        }
        if (!_editedRules.TryGetValue(profile.Id, out var rules))
        {
            rules = JsonUtils.Deserialize<List<RulesItem>>(profile.RuleSet) ?? [];
            foreach (var rule in rules.Where(r => r.Id.IsNullOrEmpty()))
            {
                rule.Id = Utils.GetGuid(false);
            }
            _editedRules[profile.Id] = rules;
        }
        RefreshAvailableRuleSets();
        foreach (var rule in rules)
        {
            var item = new RuleListItemViewModel(rule);
            item.Refresh(_availableRuleSets);
            Rules.Add(item);
        }
        SelectedRule = Rules.FirstOrDefault();
    }

    private List<RulesItem>? CurrentRules => SelectedProfile != null ? _editedRules.GetValueOrDefault(SelectedProfile.Id) : null;

    private void InsertRule(RulesItem rule)
    {
        if (CurrentRules is not { } rules)
        {
            return;
        }
        var index = SelectedRule != null ? Rules.IndexOf(SelectedRule) + 1 : Rules.Count;
        rules.Insert(index, rule);
        var item = new RuleListItemViewModel(rule);
        item.Refresh(_availableRuleSets);
        Rules.Insert(index, item);
        SelectedRule = item;
    }

    private void RemoveRule()
    {
        if (SelectedRule is not { } current || CurrentRules is not { } rules)
        {
            return;
        }
        var index = Rules.IndexOf(current);
        rules.Remove(current.Rule);
        Rules.Remove(current);
        SelectedRule = Rules.Count == 0 ? null : Rules[Math.Min(index, Rules.Count - 1)];
    }

    private void MoveRule(int offset)
    {
        if (SelectedRule is not { } current || CurrentRules is not { } rules)
        {
            return;
        }
        var index = Rules.IndexOf(current);
        var target = index + offset;
        if (target < 0 || target >= Rules.Count)
        {
            return;
        }
        Rules.Move(index, target);
        rules.RemoveAt(index);
        rules.Insert(target, current.Rule);
        SelectedRule = current;
    }

    private void OnRuleEdited()
    {
        SelectedRule?.Refresh(_availableRuleSets);
    }

    #endregion Rules

    #region Rule sets

    private RuleSetRowViewModel CreateRuleSetRow(RuleSetItem item) => new(item, row =>
    {
        RuleSets.Remove(row);
        OnRuleSetsChanged();
    }, OnRuleSetsChanged);

    private void OnRuleSetsChanged()
    {
        RefreshAvailableRuleSets();
        foreach (var rule in Rules)
        {
            rule.Refresh(_availableRuleSets);
        }
        Editor.UpdateCompatibilityText();
    }

    private void RefreshAvailableRuleSets()
    {
        var items = RuleSets.Select(r => r.ToItem()).Where(r => r.Name.IsNotEmpty()).ToList();
        var config = new Config { RuleSetItems = items };
        _availableRuleSets = new Dictionary<ECoreType, HashSet<string>>
        {
            [ECoreType.Xray] = [],
            [ECoreType.sing_box] = SingboxRuleSetSources.GetAvailableNames(config, SelectedProfile),
            // Rule providers of a Clash file are unknown here; they are accepted when the file is used.
            [ECoreType.mihomo] = [.. items.Where(t => t.MihomoSource.IsNotEmpty()).Select(t => t.Name)],
        };
    }

    /// <summary>
    /// Returns the rule sets, or null after notifying the user about an invalid row.
    /// </summary>
    private List<RuleSetItem>? ValidateRuleSets()
    {
        var items = RuleSets.Select(r => r.ToItem()).ToList();
        var names = new HashSet<string>();
        foreach (var item in items)
        {
            if (item.Name.IsNullOrEmpty() || item.Name.Contains('=') || item.Name.Contains(',')
                || !names.Add(item.Name)
                || (item.SingboxSource.IsNullOrEmpty() && item.MihomoSource.IsNullOrEmpty()))
            {
                NoticeManager.Instance.Enqueue(string.Format(ResUI.TbClashOverrideInvalidLines, ResUI.TbRuleSets,
                    item.Name.IsNullOrEmpty() ? "?" : item.Name));
                return null;
            }
        }
        return items;
    }

    #endregion Rule sets

    #region Policy groups

    private async Task LoadPolicyGroupsAsync()
    {
        var profiles = await AppManager.Instance.ProfileItems(string.Empty) ?? [];
        var groups = new List<PolicyGroupListItem>();
        foreach (var profile in profiles.Where(p => p.ConfigType.IsGroupType()).OrderBy(p => p.Remarks))
        {
            var (children, extra) = await GroupProfileManager.GetChildProfileItems(profile);
            var type = profile.ConfigType == EConfigType.ProxyChain
                ? ResUI.TbRuleCenterProxyChain
                : $"{ResUI.TbRuleCenterPolicyGroup} · {extra?.MultipleLoad ?? EMultipleLoad.LeastPing}";
            groups.Add(new PolicyGroupListItem(profile, profile.Remarks, type, string.Join(", ", children.Select(c => c.Remarks))));
        }
        PolicyGroups.Clear();
        PolicyGroups.AddRange(groups);

        Editor.OutboundOptions.Clear();
        Editor.OutboundOptions.AddRange([.. Global.OutboundTags, .. groups.Select(g => g.Remarks).Where(r => r.IsNotEmpty()).Distinct()]);
    }

    private async Task EditPolicyGroupAsync(ProfileItem profile)
    {
        var ret = await AppManager.Instance.WindowDialog.ShowDialogAsync(new AddGroupServerViewModel(profile));
        if (ret == true)
        {
            await LoadPolicyGroupsAsync();
        }
    }

    #endregion Policy groups

    #region Preview and save

    private string SerializeRules(RoutingItem profile) => JsonUtils.Serialize(_editedRules.GetValueOrDefault(profile.Id)
        ?? JsonUtils.Deserialize<List<RulesItem>>(profile.RuleSet) ?? [], false);

    /// <summary>
    /// Previews the selected profile with everything entered so far, before saving.
    /// </summary>
    private async Task PreviewAsync()
    {
        if (ValidateRuleSets() is not { } ruleSets || SelectedProfile is not { } profile)
        {
            return;
        }
        // Make sure a routing profile is active using the live configuration (as at startup), so nothing
        // below has to fix it up and save the preview copy.
        await ConfigHandler.GetDefaultRouting(_config);
        if (JsonUtils.DeepCopy(_config) is not { } config)
        {
            PreviewText = string.Format(ResUI.MsgUnifiedPreviewFailed, nameof(JsonUtils.DeepCopy));
            return;
        }
        Extensions.RuleSets = RuleSetTextFormat.ToText(ruleSets);
        if (Extensions.ApplyTo(config) is null)
        {
            return;
        }
        Dns.ApplyTo(config.SimpleDNSItem);
        var routing = JsonUtils.DeepCopy(profile) ?? profile;
        routing.RuleSet = SerializeRules(profile);
        var core = CoreCapabilities.UnifiedCores.First(c => CoreCapabilities.CoreDisplayName(c) == PreviewCore);
        PreviewText = await Task.Run(() => UnifiedPreviewService.PreviewAsync(config, core, routing));
    }

    private async Task SaveAsync()
    {
        // 1. Validate everything first, so nothing is saved when any part is invalid.
        foreach (var (profileId, rules) in _editedRules)
        {
            foreach (var rule in rules)
            {
                if (RuleInputValidator.Validate(rule) is not { } error)
                {
                    continue;
                }
                SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profileId) ?? SelectedProfile;
                SelectedRule = Rules.FirstOrDefault(r => r.Rule.Id == rule.Id) ?? SelectedRule;
                NoticeManager.Instance.Enqueue(error);
                return;
            }
        }
        if (ValidateRuleSets() is not { } ruleSets)
        {
            return;
        }
        Extensions.RuleSets = RuleSetTextFormat.ToText(ruleSets);
        if (JsonUtils.DeepCopy(_config) is not { } probe || Extensions.ApplyTo(probe) is null)
        {
            return;
        }

        // 2. Save; DNS validates its own fields before changing anything.
        if (!await Dns.SaveAsync() || !await Extensions.SaveAsync())
        {
            return;
        }
        foreach (var profile in Profiles.Where(p => _editedRules.ContainsKey(p.Id)))
        {
            profile.RuleSet = SerializeRules(profile);
            profile.RuleNum = _editedRules[profile.Id].Count;
            if (await ConfigHandler.SaveRoutingItem(_config, profile) != 0)
            {
                NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
                return;
            }
        }
        if (ActiveProfileId.IsNotEmpty() && ActiveProfileId != _savedActiveProfileId
            && Profiles.FirstOrDefault(p => p.Id == ActiveProfileId) is { } active)
        {
            await ConfigHandler.SetDefaultRouting(_config, active);
        }
        NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    #endregion Preview and save
}
