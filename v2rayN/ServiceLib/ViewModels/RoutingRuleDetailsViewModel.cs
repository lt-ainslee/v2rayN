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
        // The rule object is edited in place, so everything is validated before it is changed.
        var domain = Utils.Convert2Comma(Domain);
        var ip = Utils.Convert2Comma(IP);
        var process = Utils.ParseProcess(Process);
        var domains = AutoSort ? Utils.String2ListSorted(domain) : Utils.String2List(domain);
        var ips = AutoSort ? Utils.String2ListSorted(ip) : Utils.String2List(ip);
        var processes = AutoSort ? Utils.String2ListSorted(process) : Utils.String2List(process);
        var protocols = ProtocolItems?.ToList();
        var sourceIps = Utils.String2List(Utils.Convert2Comma(SourceIp)) ?? [];

        var invalidSourcePorts = (Utils.String2List(SourcePort) ?? []).Where(p => UnifiedRuleParser.ParsePorts(p).Count == 0).ToList();
        if (invalidSourcePorts.Count > 0)
        {
            NoticeManager.Instance.Enqueue(string.Format(ResUI.TbClashOverrideInvalidLines, ResUI.TbRuleSourcePort, string.Join("; ", invalidSourcePorts)));
            return;
        }
        // Source conditions are matched against the client address: only CIDRs and geoip:private are meaningful.
        var invalidSourceIps = sourceIps.Where(t => !IsValidSourceIp(t)).ToList();
        if (invalidSourceIps.Count > 0)
        {
            NoticeManager.Instance.Enqueue(string.Format(ResUI.TbClashOverrideInvalidLines, ResUI.TbRuleSourceIp, string.Join("; ", invalidSourceIps)));
            return;
        }
        var invalidMihomoRules = ClashSyntax.InvalidRuleLines(MihomoRules);
        if (invalidMihomoRules.Count > 0)
        {
            NoticeManager.Instance.Enqueue(string.Format(ResUI.TbClashOverrideInvalidLines, "mihomo", string.Join("; ", invalidMihomoRules)));
            return;
        }
        foreach (var (name, json) in new[] { ("Xray", XrayJson), ("sing-box", SingboxJson) })
        {
            if (!UnifiedJsonMerge.IsValidObject(json))
            {
                NoticeManager.Instance.Enqueue(string.Format(ResUI.TbRuleCoreExtInvalidJson, name));
                return;
            }
        }

        var hasRule = domains?.Count > 0
          || ips?.Count > 0
          || protocols?.Count > 0
          || processes?.Count > 0
          || Port.IsNotEmpty()
          || Network.IsNotEmpty()
          || sourceIps.Count > 0
          || SourcePort.IsNotEmpty();
        if (!hasRule)
        {
            NoticeManager.Instance.Enqueue(string.Format(ResUI.RoutingRuleDetailRequiredTips, "Network/Port/Protocol/Domain/IP/Process"));
            return;
        }

        Domain = domain;
        IP = ip;
        Process = process;
        SelectedSource.Domain = domains;
        SelectedSource.Ip = ips;
        SelectedSource.Process = processes;
        SelectedSource.Protocol = protocols;
        SelectedSource.InboundTag = InboundTagItems?.ToList();
        SelectedSource.RuleType = RuleType.IsNullOrEmpty() ? null : Enum.Parse<ERuleType>(RuleType);
        SelectedSource.OutboundTag = OutboundTag;
        SelectedSource.Remarks = Remarks;
        SelectedSource.Port = Port;
        SelectedSource.Network = Network;
        SelectedSource.Enabled = Enabled;
        SelectedSource.SourceIp = sourceIps.Count > 0 ? sourceIps : null;
        SelectedSource.SourcePort = SourcePort.NullIfEmpty();
        var coreExt = new RuleCoreExtension
        {
            XrayJson = XrayJson.NullIfEmpty(),
            SingboxJson = SingboxJson.NullIfEmpty(),
            MihomoRules = MihomoRules.NullIfEmpty(),
        };
        SelectedSource.CoreExt = coreExt.IsEmpty() ? null : coreExt;

        //NoticeHandler.Instance.Enqueue(ResUI.OperationSuccess);
        RequestClose?.Invoke(this, EventArgs.Empty);
        await Task.CompletedTask;
    }

    private static bool IsValidSourceIp(string value) => UnifiedRuleParser.ParseIp(value) switch
    {
        { Negated: true } => false,
        { Kind: EIpMatchKind.Private } => true,
        { Kind: EIpMatchKind.Cidr } match => UnifiedRuleParser.IsValidCidr(match.Value),
        _ => false,
    };

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
