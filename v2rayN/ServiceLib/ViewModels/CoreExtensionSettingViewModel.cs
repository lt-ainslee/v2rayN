namespace ServiceLib.ViewModels;

/// <summary>
/// Settings that extend the shared DNS / routing configuration for a specific core,
/// plus the rule sets that rules can reference with ruleset:name.
/// </summary>
public partial class CoreExtensionSettingViewModel : MyReactiveObject, ICloseable
{
    public event EventHandler? RequestClose;

    public static IReadOnlyList<string> MihomoUnifiedModes { get; } =
    [
        ResUI.TbMihomoUnifiedModeOff,
        ResUI.TbMihomoUnifiedModeDnsOnly,
        ResUI.TbMihomoUnifiedModeDnsAndRules,
        ResUI.TbMihomoUnifiedModeReplaceRules,
    ];

    public static IReadOnlyList<string> XrayQueryStrategies { get; } = ["", "UseIP", "UseIPv4", "UseIPv6"];

    [Reactive] public partial string MihomoUnifiedMode { get; set; }
    [Reactive] public partial string MihomoProxyGroupName { get; set; }
    [Reactive] public partial bool MihomoRespectRules { get; set; }
    [Reactive] public partial bool MihomoPreferH3 { get; set; }
    [Reactive] public partial string MihomoFakeIpFilter { get; set; }
    [Reactive] public partial string MihomoExtraProxyGroups { get; set; }
    [Reactive] public partial string MihomoExtraRules { get; set; }

    [Reactive] public partial string SingboxDnsClientSubnet { get; set; }
    [Reactive] public partial bool SingboxDnsIndependentCache { get; set; }

    [Reactive] public partial string XrayDnsQueryStrategy { get; set; }
    [Reactive] public partial bool XrayDnsDisableCache { get; set; }

    [Reactive] public partial string RuleSets { get; set; }

    public string CapabilityMatrix { get; } = CoreCapabilityMatrix.ToText();

    public ReactiveCommand<RxVoid, RxVoid> SaveCmd { get; }

    public CoreExtensionSettingViewModel()
    {
        _config = AppManager.Instance.Config;
        SaveCmd = ReactiveCommand.CreateFromTask(SaveSettingAsync);

        var ext = _config.CoreExtensionItem ?? new CoreExtensionItem();
        MihomoUnifiedMode = MihomoUnifiedModes[(int)ext.Mihomo.UnifiedMode];
        MihomoProxyGroupName = ext.Mihomo.ProxyGroupName ?? string.Empty;
        MihomoRespectRules = ext.Mihomo.RespectRules;
        MihomoPreferH3 = ext.Mihomo.PreferH3;
        MihomoFakeIpFilter = ext.Mihomo.FakeIpFilter ?? string.Empty;
        MihomoExtraProxyGroups = ext.Mihomo.ExtraProxyGroups ?? string.Empty;
        MihomoExtraRules = ext.Mihomo.ExtraRules ?? string.Empty;

        SingboxDnsClientSubnet = ext.Singbox.DnsClientSubnet ?? string.Empty;
        SingboxDnsIndependentCache = ext.Singbox.DnsIndependentCache ?? false;

        XrayDnsQueryStrategy = ext.Xray.DnsQueryStrategy ?? string.Empty;
        XrayDnsDisableCache = ext.Xray.DnsDisableCache ?? false;

        RuleSets = RuleSetTextFormat.ToText(_config.RuleSetItems);
    }

    private async Task SaveSettingAsync()
    {
        var invalidGroups = new List<string>();
        ClashSyntax.ParseProxyGroups(MihomoExtraProxyGroups, invalidGroups);
        var invalidRules = UnifiedRuleParser.SplitLines(MihomoExtraRules).Where(r => ClashSyntax.SplitTopLevel(r).Count < 2).ToList();
        var invalidRuleSets = new List<string>();
        var ruleSets = RuleSetTextFormat.Parse(RuleSets, invalidRuleSets);
        if (invalidGroups.Count > 0 || invalidRules.Count > 0 || invalidRuleSets.Count > 0)
        {
            NotifyInvalidLines(ResUI.TbClashProxyGroups, invalidGroups);
            NotifyInvalidLines(ResUI.TbClashRules, invalidRules);
            NotifyInvalidLines(ResUI.TbRuleSets, invalidRuleSets);
            return;
        }

        var mode = (EMihomoUnifiedMode)Math.Max(0, MihomoUnifiedModes.ToList().IndexOf(MihomoUnifiedMode));
        _config.CoreExtensionItem = new CoreExtensionItem
        {
            Mihomo = new MihomoExtension
            {
                UnifiedMode = mode,
                ProxyGroupName = MihomoProxyGroupName.NullIfEmpty(),
                RespectRules = MihomoRespectRules,
                PreferH3 = MihomoPreferH3,
                FakeIpFilter = MihomoFakeIpFilter.NullIfEmpty(),
                ExtraProxyGroups = MihomoExtraProxyGroups.NullIfEmpty(),
                ExtraRules = MihomoExtraRules.NullIfEmpty(),
            },
            Singbox = new SingboxExtension
            {
                DnsClientSubnet = SingboxDnsClientSubnet.NullIfEmpty(),
                DnsIndependentCache = SingboxDnsIndependentCache ? true : null,
            },
            Xray = new XrayExtension
            {
                DnsQueryStrategy = XrayDnsQueryStrategy.NullIfEmpty(),
                DnsDisableCache = XrayDnsDisableCache ? true : null,
            },
        };
        _config.RuleSetItems = ruleSets;

        if (await ConfigHandler.SaveConfig(_config) == 0)
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
        }
    }

    private static void NotifyInvalidLines(string section, List<string> lines)
    {
        if (lines.Count > 0)
        {
            NoticeManager.Instance.Enqueue(string.Format(ResUI.TbClashOverrideInvalidLines, section, string.Join("; ", lines)));
        }
    }
}
