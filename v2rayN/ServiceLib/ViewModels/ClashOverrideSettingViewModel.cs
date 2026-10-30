namespace ServiceLib.ViewModels;

public partial class ClashOverrideSettingViewModel : MyReactiveObject, ICloseable
{
    public event EventHandler? RequestClose;

    [Reactive] public partial bool EnableDnsOverride { get; set; }
    [Reactive] public partial string EnhancedMode { get; set; }
    [Reactive] public partial string FakeIPRange { get; set; }
    [Reactive] public partial string FakeIPFilter { get; set; }
    [Reactive] public partial bool RespectRules { get; set; }
    [Reactive] public partial string DefaultNameserver { get; set; }
    [Reactive] public partial string Nameserver { get; set; }
    [Reactive] public partial string Fallback { get; set; }
    [Reactive] public partial bool FallbackFilterGeoIP { get; set; }
    [Reactive] public partial string FallbackFilterGeoIPCode { get; set; }
    [Reactive] public partial string ProxyServerNameserver { get; set; }
    [Reactive] public partial string DirectNameserver { get; set; }
    [Reactive] public partial string NameserverPolicy { get; set; }

    [Reactive] public partial bool EnablePolicyOverride { get; set; }
    [Reactive] public partial string ProxyGroups { get; set; }
    [Reactive] public partial string RuleProviders { get; set; }
    [Reactive] public partial string Rules { get; set; }
    [Reactive] public partial bool ReplaceRules { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> SaveCmd { get; }

    public ClashOverrideSettingViewModel()
    {
        _config = AppManager.Instance.Config;
        SaveCmd = ReactiveCommand.CreateFromTask(SaveSettingAsync);

        var item = _config.ClashOverrideItem;
        EnableDnsOverride = item.EnableDnsOverride;
        EnhancedMode = item.EnhancedMode ?? Global.ClashEnhancedModes.First();
        FakeIPRange = item.FakeIPRange ?? Global.FakeIPRanges.First();
        FakeIPFilter = item.FakeIPFilter ?? string.Empty;
        RespectRules = item.RespectRules;
        DefaultNameserver = item.DefaultNameserver ?? string.Empty;
        Nameserver = item.Nameserver ?? string.Empty;
        Fallback = item.Fallback ?? string.Empty;
        FallbackFilterGeoIP = item.FallbackFilterGeoIP;
        FallbackFilterGeoIPCode = item.FallbackFilterGeoIPCode ?? string.Empty;
        ProxyServerNameserver = item.ProxyServerNameserver ?? string.Empty;
        DirectNameserver = item.DirectNameserver ?? string.Empty;
        NameserverPolicy = item.NameserverPolicy ?? string.Empty;

        EnablePolicyOverride = item.EnablePolicyOverride;
        ProxyGroups = item.ProxyGroups ?? string.Empty;
        RuleProviders = item.RuleProviders ?? string.Empty;
        Rules = item.Rules ?? string.Empty;
        ReplaceRules = item.ReplaceRules;
    }

    private async Task SaveSettingAsync()
    {
        var item = new ClashOverrideItem
        {
            EnableDnsOverride = EnableDnsOverride,
            EnhancedMode = EnhancedMode,
            FakeIPRange = FakeIPRange,
            FakeIPFilter = FakeIPFilter,
            RespectRules = RespectRules,
            DefaultNameserver = DefaultNameserver,
            Nameserver = Nameserver,
            Fallback = Fallback,
            FallbackFilterGeoIP = FallbackFilterGeoIP,
            FallbackFilterGeoIPCode = FallbackFilterGeoIPCode,
            ProxyServerNameserver = ProxyServerNameserver,
            DirectNameserver = DirectNameserver,
            NameserverPolicy = NameserverPolicy,
            EnablePolicyOverride = EnablePolicyOverride,
            ProxyGroups = ProxyGroups,
            RuleProviders = RuleProviders,
            Rules = Rules,
            ReplaceRules = ReplaceRules,
        };

        var result = ClashOverrideService.Validate(item);
        if (!result.IsValid)
        {
            if (result.NameserverMissing)
            {
                NoticeManager.Instance.Enqueue(ResUI.TbClashNameserverRequired);
            }
            NotifyInvalidLines(ResUI.TbClashNameserverPolicy, result.NameserverPolicy);
            NotifyInvalidLines(ResUI.TbClashProxyGroups, result.ProxyGroups);
            NotifyInvalidLines(ResUI.TbClashRuleProviders, result.RuleProviders);
            NotifyInvalidLines(ResUI.TbClashRules, result.Rules);
            return;
        }

        _config.ClashOverrideItem = item;
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
