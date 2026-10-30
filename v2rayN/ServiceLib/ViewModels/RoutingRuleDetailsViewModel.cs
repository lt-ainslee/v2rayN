namespace ServiceLib.ViewModels;

public partial class RoutingRuleDetailsViewModel : MyReactiveObject, ICloseable
{
    public event EventHandler? RequestClose;

    public IList<string> ProtocolItems { get; set; }
    public IList<string> InboundTagItems { get; set; }

    [Reactive]
    public partial RulesItem SelectedSource { get; set; }

    [Reactive]
    public partial string Domain { get; set; }

    [Reactive]
    public partial string IP { get; set; }

    [Reactive]
    public partial string Process { get; set; }

    [Reactive]
    public partial string? RuleType { get; set; }

    [Reactive]
    public partial bool AutoSort { get; set; }

    [Reactive]
    public partial string OutboundTag { get; set; }

    [Reactive]
    public partial string Remarks { get; set; }

    [Reactive]
    public partial string Port { get; set; }

    [Reactive]
    public partial string Network { get; set; }

    [Reactive]
    public partial bool Enabled { get; set; }

    [Reactive]
    public partial string SourceIp { get; set; }

    [Reactive]
    public partial string SourcePort { get; set; }

    [Reactive]
    public partial string XrayJson { get; set; }

    [Reactive]
    public partial string SingboxJson { get; set; }

    [Reactive]
    public partial string MihomoRules { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> SelectProfileCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCmd { get; }

    public RoutingRuleDetailsViewModel(RulesItem rulesItem)
    {
        _config = AppManager.Instance.Config;

        SelectProfileCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SelectProfileAsync();
        });
        SaveCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SaveRulesAsync();
        });

        if (rulesItem.Id.IsNullOrEmpty())
        {
            rulesItem.Id = Utils.GetGuid(false);
            rulesItem.OutboundTag = Global.ProxyTag;
            rulesItem.Enabled = true;
            SelectedSource = rulesItem;
        }
        else
        {
            SelectedSource = rulesItem;
        }

        Domain = Utils.List2String(SelectedSource.Domain, true);
        IP = Utils.List2String(SelectedSource.Ip, true);
        Process = Utils.List2String(SelectedSource.Process, true);
        RuleType = SelectedSource.RuleType?.ToString();
        OutboundTag = SelectedSource.OutboundTag;
        Remarks = SelectedSource.Remarks;
        Port = SelectedSource.Port;
        Network = SelectedSource.Network;
        Enabled = SelectedSource.Enabled;
        SourceIp = Utils.List2String(SelectedSource.SourceIp, true);
        SourcePort = SelectedSource.SourcePort ?? string.Empty;
        XrayJson = SelectedSource.CoreExt?.XrayJson ?? string.Empty;
        SingboxJson = SelectedSource.CoreExt?.SingboxJson ?? string.Empty;
        MihomoRules = SelectedSource.CoreExt?.MihomoRules ?? string.Empty;
    }

    private async Task SaveRulesAsync()
    {
        Domain = Utils.Convert2Comma(Domain);
        IP = Utils.Convert2Comma(IP);
        Process = Utils.ParseProcess(Process);

        if (AutoSort)
        {
            SelectedSource.Domain = Utils.String2ListSorted(Domain);
            SelectedSource.Ip = Utils.String2ListSorted(IP);
            SelectedSource.Process = Utils.String2ListSorted(Process);
        }
        else
        {
            SelectedSource.Domain = Utils.String2List(Domain);
            SelectedSource.Ip = Utils.String2List(IP);
            SelectedSource.Process = Utils.String2List(Process);
        }
        SelectedSource.Protocol = ProtocolItems?.ToList();
        SelectedSource.InboundTag = InboundTagItems?.ToList();
        SelectedSource.RuleType = RuleType.IsNullOrEmpty() ? null : Enum.Parse<ERuleType>(RuleType);
        SelectedSource.OutboundTag = OutboundTag;
        SelectedSource.Remarks = Remarks;
        SelectedSource.Port = Port;
        SelectedSource.Network = Network;
        SelectedSource.Enabled = Enabled;
        SelectedSource.SourceIp = Utils.String2List(Utils.Convert2Comma(SourceIp));
        SelectedSource.SourcePort = SourcePort.NullIfEmpty();

        foreach (var (name, json) in new[] { ("Xray", XrayJson), ("sing-box", SingboxJson) })
        {
            if (!UnifiedJsonMerge.IsValidObject(json))
            {
                NoticeManager.Instance.Enqueue(string.Format(ResUI.TbRuleCoreExtInvalidJson, name));
                return;
            }
        }
        var coreExt = new RuleCoreExtension
        {
            XrayJson = XrayJson.NullIfEmpty(),
            SingboxJson = SingboxJson.NullIfEmpty(),
            MihomoRules = MihomoRules.NullIfEmpty(),
        };
        SelectedSource.CoreExt = coreExt.IsEmpty() ? null : coreExt;

        var hasRule = SelectedSource.Domain?.Count > 0
          || SelectedSource.Ip?.Count > 0
          || SelectedSource.Protocol?.Count > 0
          || SelectedSource.Process?.Count > 0
          || SelectedSource.Port.IsNotEmpty()
          || SelectedSource.Network.IsNotEmpty()
          || SelectedSource.SourceIp?.Count > 0
          || SelectedSource.SourcePort.IsNotEmpty();

        if (!hasRule)
        {
            NoticeManager.Instance.Enqueue(string.Format(ResUI.RoutingRuleDetailRequiredTips, "Network/Port/Protocol/Domain/IP/Process"));
            return;
        }
        //NoticeHandler.Instance.Enqueue(ResUI.OperationSuccess);
        RequestClose?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    private async Task SelectProfileAsync()
    {
        var profileSelectViewModel = new ProfilesSelectViewModel();
        profileSelectViewModel.SetConfigTypeFilter([EConfigType.Custom], exclude: true);
        var result = await AppManager.Instance.WindowDialog.ShowDialogAsync(profileSelectViewModel);
        if (result != true)
        {
            return;
        }
        var profileItem = await profileSelectViewModel.GetProfileItem();
        if (profileItem != null)
        {
            OutboundTag = profileItem.Remarks;
        }
    }
}
