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

    public static IReadOnlyList<string> XrayQueryStrategies { get; } = ["", .. CoreExtensionValidator.XrayQueryStrategies];
    public static IReadOnlyList<string> MihomoCacheAlgorithms { get; } = ["", .. CoreExtensionValidator.MihomoCacheAlgorithms];
    public static IReadOnlyList<string> MihomoFindProcessModes { get; } = ["", .. CoreConfigClashService.FindProcessModes];

    /// <summary>Options that may keep the configuration file's value: keep / on / off.</summary>
    public static IReadOnlyList<string> TriStateItems { get; } = [ResUI.TbTriStateKeep, ResUI.TbTriStateOn, ResUI.TbTriStateOff];

    [Reactive] public partial string MihomoUnifiedMode { get; set; }
    [Reactive] public partial string MihomoProxyGroupName { get; set; }
    [Reactive] public partial bool MihomoRespectRules { get; set; }
    [Reactive] public partial bool MihomoPreferH3 { get; set; }
    [Reactive] public partial string MihomoFakeIpFilter { get; set; }
    [Reactive] public partial string MihomoExtraProxyGroups { get; set; }
    [Reactive] public partial string MihomoExtraRules { get; set; }
    [Reactive] public partial string MihomoDnsCacheAlgorithm { get; set; }
    [Reactive] public partial string MihomoUnifiedDelay { get; set; }
    [Reactive] public partial string MihomoTcpConcurrent { get; set; }
    [Reactive] public partial string MihomoFindProcessMode { get; set; }
    [Reactive] public partial string MihomoKeepAliveInterval { get; set; }
    [Reactive] public partial string MihomoSniffer { get; set; }

    [Reactive] public partial string SingboxDnsClientSubnet { get; set; }
    [Reactive] public partial bool SingboxDnsIndependentCache { get; set; }
    [Reactive] public partial bool SingboxDnsDisableExpire { get; set; }
    [Reactive] public partial string SingboxDnsCacheCapacity { get; set; }
    [Reactive] public partial bool SingboxDnsReverseMapping { get; set; }
    [Reactive] public partial bool SingboxRouteFindProcess { get; set; }
    [Reactive] public partial string SingboxRouteDefaultMark { get; set; }
    [Reactive] public partial bool SingboxCacheFileStoreRdrc { get; set; }

    [Reactive] public partial string XrayDnsQueryStrategy { get; set; }
    [Reactive] public partial bool XrayDnsDisableCache { get; set; }
    [Reactive] public partial bool XrayDnsDisableFallback { get; set; }
    [Reactive] public partial bool XrayDnsDisableFallbackIfMatch { get; set; }
    [Reactive] public partial string XrayDnsClientIp { get; set; }
    [Reactive] public partial string XrayDnsServeExpiredTtl { get; set; }

    [Reactive] public partial string RuleSets { get; set; }

    public static IReadOnlyList<string> PreviewCores { get; } = [.. CoreCapabilities.UnifiedCores.Select(CoreCapabilities.CoreDisplayName)];
    [Reactive] public partial string PreviewCore { get; set; }
    [Reactive] public partial string PreviewText { get; set; }

    public IReadOnlyList<CoreCapabilityRow> CapabilityRows { get; } = CoreCapabilityMatrix.Rows();

    /// <summary>The other mihomo options only take effect when the shared settings are applied.</summary>
    public bool IsMihomoUnifiedEnabled => MihomoUnifiedMode != MihomoUnifiedModes[0];

    public ReactiveCommand<RxVoid, RxVoid> SaveCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> PreviewCmd { get; }

    public CoreExtensionSettingViewModel()
    {
        _config = AppManager.Instance.Config;
        SaveCmd = ReactiveCommand.CreateFromTask(SaveSettingAsync);
        PreviewCmd = ReactiveCommand.CreateFromTask(PreviewAsync);
        PreviewCore = PreviewCores[0];
        PreviewText = string.Empty;

        var ext = _config.CoreExtensionItem ?? new CoreExtensionItem();
        MihomoUnifiedMode = MihomoUnifiedModes[Math.Clamp((int)ext.Mihomo.UnifiedMode, 0, MihomoUnifiedModes.Count - 1)];
        MihomoProxyGroupName = ext.Mihomo.ProxyGroupName ?? string.Empty;
        MihomoRespectRules = ext.Mihomo.RespectRules;
        MihomoPreferH3 = ext.Mihomo.PreferH3;
        MihomoFakeIpFilter = ext.Mihomo.FakeIpFilter ?? string.Empty;
        MihomoExtraProxyGroups = ext.Mihomo.ExtraProxyGroups ?? string.Empty;
        MihomoExtraRules = ext.Mihomo.ExtraRules ?? string.Empty;
        MihomoDnsCacheAlgorithm = ext.Mihomo.DnsCacheAlgorithm ?? string.Empty;
        MihomoUnifiedDelay = ToTriState(ext.Mihomo.UnifiedDelay);
        MihomoTcpConcurrent = ToTriState(ext.Mihomo.TcpConcurrent);
        MihomoFindProcessMode = ext.Mihomo.FindProcessMode ?? string.Empty;
        MihomoKeepAliveInterval = ext.Mihomo.KeepAliveInterval?.ToString() ?? string.Empty;
        MihomoSniffer = ToTriState(ext.Mihomo.SnifferEnabled);

        SingboxDnsClientSubnet = ext.Singbox.DnsClientSubnet ?? string.Empty;
        SingboxDnsIndependentCache = ext.Singbox.DnsIndependentCache ?? false;
        SingboxDnsDisableExpire = ext.Singbox.DnsDisableExpire ?? false;
        SingboxDnsCacheCapacity = ext.Singbox.DnsCacheCapacity?.ToString() ?? string.Empty;
        SingboxDnsReverseMapping = ext.Singbox.DnsReverseMapping ?? false;
        SingboxRouteFindProcess = ext.Singbox.RouteFindProcess ?? false;
        SingboxRouteDefaultMark = ext.Singbox.RouteDefaultMark?.ToString() ?? string.Empty;
        SingboxCacheFileStoreRdrc = ext.Singbox.CacheFileStoreRdrc ?? false;

        XrayDnsQueryStrategy = ext.Xray.DnsQueryStrategy ?? string.Empty;
        XrayDnsDisableCache = ext.Xray.DnsDisableCache ?? false;
        XrayDnsDisableFallback = ext.Xray.DnsDisableFallback ?? false;
        XrayDnsDisableFallbackIfMatch = ext.Xray.DnsDisableFallbackIfMatch ?? false;
        XrayDnsClientIp = ext.Xray.DnsClientIp ?? string.Empty;
        XrayDnsServeExpiredTtl = ext.Xray.DnsServeExpiredTtl?.ToString() ?? string.Empty;

        RuleSets = RuleSetTextFormat.ToText(_config.RuleSetItems);

        this.WhenAnyValue(x => x.MihomoUnifiedMode)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(IsMihomoUnifiedEnabled)));
    }

    /// <summary>
    /// Validates the input; returns the extension settings and the parsed rule sets, or null after notifying the user.
    /// </summary>
    private (CoreExtensionItem Extension, List<RuleSetItem> RuleSets)? ValidateInput()
    {
        var numberErrors = new List<string>();
        var extension = BuildExtensionItem(numberErrors);
        var errors = numberErrors.Concat(CoreExtensionValidator.Validate(extension)).ToList();
        var invalidRuleSets = new List<string>();
        var ruleSets = RuleSetTextFormat.Parse(RuleSets, invalidRuleSets);
        errors.AddRange(invalidRuleSets.Select(line => $"{ResUI.TbRuleSets}: {line}"));
        if (errors.Count == 0)
        {
            return (extension, ruleSets);
        }
        NoticeManager.Instance.Enqueue(string.Format(ResUI.TbCoreExtensionInvalidInput, string.Join("; ", errors)));
        return null;
    }

    private CoreExtensionItem BuildExtensionItem(List<string> errors)
    {
        var mode = (EMihomoUnifiedMode)Math.Max(0, MihomoUnifiedModes.ToList().IndexOf(MihomoUnifiedMode));
        return new CoreExtensionItem
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
                DnsCacheAlgorithm = MihomoDnsCacheAlgorithm.NullIfEmpty(),
                UnifiedDelay = FromTriState(MihomoUnifiedDelay),
                TcpConcurrent = FromTriState(MihomoTcpConcurrent),
                FindProcessMode = MihomoFindProcessMode.NullIfEmpty(),
                KeepAliveInterval = ParseOptionalInt(MihomoKeepAliveInterval, "mihomo keep-alive-interval", errors),
                SnifferEnabled = FromTriState(MihomoSniffer),
            },
            Singbox = new SingboxExtension
            {
                DnsClientSubnet = SingboxDnsClientSubnet.Trim().NullIfEmpty(),
                DnsIndependentCache = SingboxDnsIndependentCache ? true : null,
                DnsDisableExpire = SingboxDnsDisableExpire ? true : null,
                DnsCacheCapacity = ParseOptionalInt(SingboxDnsCacheCapacity, "sing-box dns.cache_capacity", errors),
                DnsReverseMapping = SingboxDnsReverseMapping ? true : null,
                RouteFindProcess = SingboxRouteFindProcess ? true : null,
                RouteDefaultMark = ParseOptionalInt(SingboxRouteDefaultMark, "sing-box route.default_mark", errors),
                CacheFileStoreRdrc = SingboxCacheFileStoreRdrc ? true : null,
            },
            Xray = new XrayExtension
            {
                DnsQueryStrategy = XrayDnsQueryStrategy.NullIfEmpty(),
                DnsDisableCache = XrayDnsDisableCache ? true : null,
                DnsDisableFallback = XrayDnsDisableFallback ? true : null,
                DnsDisableFallbackIfMatch = XrayDnsDisableFallbackIfMatch ? true : null,
                DnsClientIp = XrayDnsClientIp.Trim().NullIfEmpty(),
                DnsServeExpiredTtl = ParseOptionalInt(XrayDnsServeExpiredTtl, "Xray dns.serveExpiredTTL", errors),
            },
        };
    }

    private static int? ParseOptionalInt(string? text, string field, List<string> errors)
    {
        if (text.IsNullOrEmpty() || text.Trim().Length == 0)
        {
            return null;
        }
        if (int.TryParse(text.Trim(), out var value))
        {
            return value;
        }
        errors.Add($"{field}: {text}");
        return null;
    }

    private static string ToTriState(bool? value) => value switch
    {
        true => TriStateItems[1],
        false => TriStateItems[2],
        null => TriStateItems[0],
    };

    private static bool? FromTriState(string? value) =>
        value == TriStateItems[1] ? true : value == TriStateItems[2] ? false : null;

    private async Task SaveSettingAsync()
    {
        if (ValidateInput() is not { } input)
        {
            return;
        }
        _config.CoreExtensionItem = input.Extension;
        _config.RuleSetItems = input.RuleSets;

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

    /// <summary>
    /// Previews the selected core with the values currently entered, before they are saved.
    /// </summary>
    private async Task PreviewAsync()
    {
        if (ValidateInput() is not { } input)
        {
            return;
        }
        // Make sure a routing profile is active using the live configuration (as at startup), so nothing
        // below has to fix it up and save the preview copy.
        await ConfigHandler.GetDefaultRouting(_config);
        // Work on a copy: the preview must never change the live configuration.
        if (JsonUtils.DeepCopy(_config) is not { } config)
        {
            PreviewText = string.Format(ResUI.MsgUnifiedPreviewFailed, nameof(JsonUtils.DeepCopy));
            return;
        }
        config.CoreExtensionItem = input.Extension;
        config.RuleSetItems = input.RuleSets;
        var core = CoreCapabilities.UnifiedCores.First(c => CoreCapabilities.CoreDisplayName(c) == PreviewCore);
        PreviewText = await Task.Run(() => UnifiedPreviewService.PreviewAsync(config, core));
    }
}
