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
        // The rule object is edited in place, so a candidate is built and validated first; the checks are
        // shared with the unified rule settings, so both editors accept the same rules.
        var domain = Utils.Convert2Comma(Domain);
        var ip = Utils.Convert2Comma(IP);
        var process = Utils.ParseProcess(Process);
        var sourceIps = Utils.String2List(Utils.Convert2Comma(SourceIp)) ?? [];
        var coreExt = new RuleCoreExtension
        {
            XrayJson = XrayJson.NullIfEmpty(),
            SingboxJson = SingboxJson.NullIfEmpty(),
            MihomoRules = MihomoRules.NullIfEmpty(),
        };
        var candidate = new RulesItem
        {
            Id = SelectedSource.Id,
            Domain = AutoSort ? Utils.String2ListSorted(domain) : Utils.String2List(domain),
            Ip = AutoSort ? Utils.String2ListSorted(ip) : Utils.String2List(ip),
            Process = AutoSort ? Utils.String2ListSorted(process) : Utils.String2List(process),
            Protocol = ProtocolItems?.ToList(),
            InboundTag = InboundTagItems?.ToList(),
            RuleType = RuleType.IsNullOrEmpty() ? null : Enum.Parse<ERuleType>(RuleType),
            OutboundTag = OutboundTag,
            Remarks = Remarks,
            Port = Port,
            Network = Network,
            Enabled = Enabled,
            SourceIp = sourceIps.Count > 0 ? sourceIps : null,
            SourcePort = SourcePort.NullIfEmpty(),
            CoreExt = coreExt.IsEmpty() ? null : coreExt,
        };
        if (RuleInputValidator.Validate(candidate) is { } error)
        {
            NoticeManager.Instance.Enqueue(error);
            return;
        }

        Domain = domain;
        IP = ip;
        Process = process;
        SelectedSource.Domain = candidate.Domain;
        SelectedSource.Ip = candidate.Ip;
        SelectedSource.Process = candidate.Process;
        SelectedSource.Protocol = candidate.Protocol;
        SelectedSource.InboundTag = candidate.InboundTag;
        SelectedSource.RuleType = candidate.RuleType;
        SelectedSource.OutboundTag = candidate.OutboundTag;
        SelectedSource.Remarks = candidate.Remarks;
        SelectedSource.Port = candidate.Port;
        SelectedSource.Network = candidate.Network;
        SelectedSource.Enabled = candidate.Enabled;
        SelectedSource.SourceIp = candidate.SourceIp;
        SelectedSource.SourcePort = candidate.SourcePort;
        SelectedSource.CoreExt = candidate.CoreExt;

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
