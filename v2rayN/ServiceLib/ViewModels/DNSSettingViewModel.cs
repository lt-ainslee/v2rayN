namespace ServiceLib.ViewModels;

public partial class DNSSettingViewModel : MyReactiveObject, ICloseable
{
    public event EventHandler? RequestClose;

    [Reactive] public partial bool UseSystemHosts { get; set; }
    [Reactive] public partial bool AddCommonHosts { get; set; }
    [Reactive] public partial bool FakeIP { get; set; }
    [Reactive] public partial string FakeIPRange { get; set; }
    [Reactive] public partial bool BlockBindingQuery { get; set; }
    [Reactive] public partial bool BlockAAAAQuery { get; set; }
    [Reactive] public partial string DirectDNS { get; set; }
    [Reactive] public partial string RemoteDNS { get; set; }
    [Reactive] public partial string BootstrapDNS { get; set; }
    [Reactive] public partial string Strategy4Freedom { get; set; }
    [Reactive] public partial string Strategy4Proxy { get; set; }
    [Reactive] public partial string Strategy4ProxyDial { get; set; }
    [Reactive] public partial string Hosts { get; set; }
    [Reactive] public partial string DirectExpectedIPs { get; set; }
    [Reactive] public partial bool ParallelQuery { get; set; }
    [Reactive] public partial bool ServeStale { get; set; }
    [Reactive] public partial bool EnableHappyEyeballs { get; set; }
    [Reactive] public partial string DnsPolicy { get; set; }

    [Reactive] public partial bool UseSystemHostsCompatible { get; set; }
    [Reactive] public partial string DomainStrategy4FreedomCompatible { get; set; } = string.Empty;
    [Reactive] public partial string DomainDNSAddressCompatible { get; set; } = string.Empty;
    [Reactive] public partial string NormalDNSCompatible { get; set; } = string.Empty;
    [Reactive] public partial string TunDNSCompatible { get; set; } = string.Empty;

    [Reactive] public partial string DomainStrategy4Freedom2Compatible { get; set; } = string.Empty;
    [Reactive] public partial string DomainDNSAddress2Compatible { get; set; } = string.Empty;
    [Reactive] public partial string NormalDNS2Compatible { get; set; } = string.Empty;
    [Reactive] public partial string TunDNS2Compatible { get; set; } = string.Empty;
    [Reactive] public partial bool RayCustomDNSEnableCompatible { get; set; }
    [Reactive] public partial bool SBCustomDNSEnableCompatible { get; set; }

    public bool IsSimpleDNSEnabled => !(RayCustomDNSEnableCompatible && SBCustomDNSEnableCompatible);

    public ReactiveCommand<RxVoid, RxVoid> SaveCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> ImportDefConfig4V2rayCompatibleCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> ImportDefConfig4SingboxCompatibleCmd { get; }

    public DNSSettingViewModel()
    {
        _config = AppManager.Instance.Config;
        SaveCmd = ReactiveCommand.CreateFromTask(SaveSettingAsync);

        ImportDefConfig4V2rayCompatibleCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            NormalDNSCompatible = EmbedUtils.GetEmbedText(Global.DNSV2rayNormalFileName);
            TunDNSCompatible = EmbedUtils.GetEmbedText(Global.DNSV2rayNormalFileName);
            await Task.CompletedTask;
        });

        ImportDefConfig4SingboxCompatibleCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            NormalDNS2Compatible = EmbedUtils.GetEmbedText(Global.DNSSingboxNormalFileName);
            TunDNS2Compatible = EmbedUtils.GetEmbedText(Global.TunSingboxDNSFileName);
            await Task.CompletedTask;
        });

        this.WhenAnyValue(x => x.RayCustomDNSEnableCompatible, x => x.SBCustomDNSEnableCompatible)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(IsSimpleDNSEnabled)));

        _ = Init();
    }

    private async Task Init()
    {
        _config = AppManager.Instance.Config;
        var item = _config.SimpleDNSItem;
        UseSystemHosts = item.UseSystemHosts ?? false;
        AddCommonHosts = item.AddCommonHosts ?? false;
        FakeIP = item.FakeIP ?? false;
        FakeIPRange = item.FakeIPRange ?? string.Empty;
        BlockBindingQuery = item.BlockBindingQuery ?? false;
        BlockAAAAQuery = item.BlockAAAAQuery ?? false;
        DirectDNS = item.DirectDNS ?? string.Empty;
        RemoteDNS = item.RemoteDNS ?? string.Empty;
        BootstrapDNS = item.BootstrapDNS ?? string.Empty;
        Strategy4Freedom = item.Strategy4Freedom ?? string.Empty;
        Strategy4Proxy = item.Strategy4Proxy ?? string.Empty;
        Strategy4ProxyDial = item.Strategy4ProxyDial ?? string.Empty;
        Hosts = item.Hosts ?? string.Empty;
        DirectExpectedIPs = item.DirectExpectedIPs ?? string.Empty;
        ParallelQuery = item.ParallelQuery ?? false;
        ServeStale = item.ServeStale ?? false;
        EnableHappyEyeballs = item.EnableHappyEyeballs ?? false;
        DnsPolicy = item.DnsPolicy ?? string.Empty;
        var item1 = await AppManager.Instance.GetDNSItem(ECoreType.Xray);
        RayCustomDNSEnableCompatible = item1.Enabled;
        UseSystemHostsCompatible = item1.UseSystemHosts;
        DomainStrategy4FreedomCompatible = item1?.DomainStrategy4Freedom ?? string.Empty;
        DomainDNSAddressCompatible = item1?.DomainDNSAddress ?? string.Empty;
        NormalDNSCompatible = item1?.NormalDNS ?? string.Empty;
        TunDNSCompatible = item1?.TunDNS ?? string.Empty;

        var item2 = await AppManager.Instance.GetDNSItem(ECoreType.sing_box);
        SBCustomDNSEnableCompatible = item2.Enabled;
        DomainStrategy4Freedom2Compatible = item2?.DomainStrategy4Freedom ?? string.Empty;
        DomainDNSAddress2Compatible = item2?.DomainDNSAddress ?? string.Empty;
        NormalDNS2Compatible = item2?.NormalDNS ?? string.Empty;
        TunDNS2Compatible = item2?.TunDNS ?? string.Empty;
    }

    private async Task SaveSettingAsync()
    {
        if (await SaveAsync())
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Validates every field first and only then saves, so nothing is half-applied when saving is refused.
    /// Returns false (after notifying the user) when the input is invalid.
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        var invalidPolicies = UnifiedRuleParser.SplitLines(DnsPolicy)
            .Where((line, i) => UnifiedRuleParser.ParseDnsPolicyLine(line, i) == null)
            .ToList();
        if (invalidPolicies.Count > 0)
        {
            NoticeManager.Instance.Enqueue(string.Format(ResUI.TbClashOverrideInvalidLines, ResUI.TbDnsPolicy, string.Join("; ", invalidPolicies)));
            return false;
        }
        if (NormalDNSCompatible.IsNotEmpty())
        {
            var obj = JsonUtils.ParseJson(NormalDNSCompatible);
            if (obj != null && obj["servers"] != null)
            {
            }
            else
            {
                if (NormalDNSCompatible.Contains('{') || NormalDNSCompatible.Contains('}'))
                {
                    NoticeManager.Instance.Enqueue(ResUI.FillCorrectDNSText);
                    return false;
                }
            }
        }
        if (TunDNSCompatible.IsNotEmpty())
        {
            var obj = JsonUtils.ParseJson(TunDNSCompatible);
            if (obj != null && obj["servers"] != null)
            {
            }
            else
            {
                if (TunDNSCompatible.Contains('{') || TunDNSCompatible.Contains('}'))
                {
                    NoticeManager.Instance.Enqueue(ResUI.FillCorrectDNSText);
                    return false;
                }
            }
        }
        if (NormalDNS2Compatible.IsNotEmpty())
        {
            var obj2 = JsonUtils.Deserialize<Dns4Sbox>(NormalDNS2Compatible);
            if (obj2 == null
                || obj2.servers.Count == 0
                || obj2.servers.Any(s => s.type.IsNullOrEmpty()))
            {
                NoticeManager.Instance.Enqueue(ResUI.FillCorrectDNSText);
                return false;
            }
        }
        if (TunDNS2Compatible.IsNotEmpty())
        {
            var obj2 = JsonUtils.Deserialize<Dns4Sbox>(TunDNS2Compatible);
            if (obj2 == null
                || obj2.servers.Count == 0
                || obj2.servers.Any(s => s.type.IsNullOrEmpty()))
            {
                NoticeManager.Instance.Enqueue(ResUI.FillCorrectDNSText);
                return false;
            }
        }

        var item1 = await AppManager.Instance.GetDNSItem(ECoreType.Xray);
        item1.Enabled = RayCustomDNSEnableCompatible;
        item1.DomainStrategy4Freedom = DomainStrategy4FreedomCompatible;
        item1.DomainDNSAddress = DomainDNSAddressCompatible;
        item1.UseSystemHosts = UseSystemHostsCompatible;
        item1.NormalDNS = NormalDNSCompatible;
        item1.TunDNS = TunDNSCompatible;
        await ConfigHandler.SaveDNSItems(_config, item1);

        var item2 = await AppManager.Instance.GetDNSItem(ECoreType.sing_box);
        item2.Enabled = SBCustomDNSEnableCompatible;
        item2.DomainStrategy4Freedom = DomainStrategy4Freedom2Compatible;
        item2.DomainDNSAddress = DomainDNSAddress2Compatible;
        item2.NormalDNS = JsonUtils.Serialize(JsonUtils.Deserialize<Dns4Sbox>(NormalDNS2Compatible));
        item2.TunDNS = JsonUtils.Serialize(JsonUtils.Deserialize<Dns4Sbox>(TunDNS2Compatible));
        await ConfigHandler.SaveDNSItems(_config, item2);

        ApplyTo(_config.SimpleDNSItem);
        await ConfigHandler.SaveConfig(_config);
        return true;
    }

    /// <summary>
    /// Writes the simple DNS fields into the given item (used for saving and for previews of unsaved values).
    /// </summary>
    public void ApplyTo(SimpleDNSItem target)
    {
        target.UseSystemHosts = UseSystemHosts;
        target.AddCommonHosts = AddCommonHosts;
        target.FakeIP = FakeIP;
        target.FakeIPRange = FakeIPRange;
        target.BlockBindingQuery = BlockBindingQuery;
        target.BlockAAAAQuery = BlockAAAAQuery;
        target.DirectDNS = DirectDNS;
        target.RemoteDNS = RemoteDNS;
        target.BootstrapDNS = BootstrapDNS;
        target.Strategy4Freedom = Strategy4Freedom;
        target.Strategy4Proxy = Strategy4Proxy;
        target.Strategy4ProxyDial = Strategy4ProxyDial;
        target.Hosts = Hosts;
        target.DirectExpectedIPs = DirectExpectedIPs;
        target.ParallelQuery = ParallelQuery;
        target.ServeStale = ServeStale;
        target.EnableHappyEyeballs = EnableHappyEyeballs;
        target.DnsPolicy = DnsPolicy.NullIfEmpty();
    }
}
