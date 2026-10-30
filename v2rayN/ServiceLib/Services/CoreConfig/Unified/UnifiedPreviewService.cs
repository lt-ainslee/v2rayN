using YamlDotNet.Core;

namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Shows what a core receives from the shared settings without starting it:
/// the compatibility diagnostics followed by the generated DNS and routing sections.
/// </summary>
public static class UnifiedPreviewService
{
    private const string PreviewNodeId = "unified-preview";

    public static async Task<string> PreviewAsync(Config config, ECoreType core)
    {
        try
        {
            var mihomoMode = config.CoreExtensionItem?.Mihomo.UnifiedMode ?? EMihomoUnifiedMode.Off;
            if (core == ECoreType.mihomo && mihomoMode == EMihomoUnifiedMode.Off)
            {
                return ResUI.MsgUnifiedPreviewMihomoOff;
            }
            var routing = await ConfigHandler.GetDefaultRouting(config);
            var diagnostics = new UnifiedDiagnostics();
            var body = core == ECoreType.mihomo
                ? await PreviewMihomoAsync(config, routing, diagnostics)
                : await PreviewJsonAsync(config, routing, core, diagnostics);
            if (core == ECoreType.mihomo && mihomoMode == EMihomoUnifiedMode.DnsOnly)
            {
                // Rules are not applied in this mode, so only DNS notes are relevant (same as at startup).
                var dnsOnly = new UnifiedDiagnostics();
                foreach (var item in diagnostics.Items.Where(d => d.Subject == "DNS"))
                {
                    dnsOnly.Info(item.Core, item.Subject, item.Message);
                }
                diagnostics = dnsOnly;
            }
            return Compose(diagnostics, body);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(UnifiedPreviewService), ex);
            return string.Format(ResUI.MsgUnifiedPreviewFailed, ex.Message);
        }
    }

    public static string Compose(UnifiedDiagnostics diagnostics, string body)
    {
        var sb = new StringBuilder();
        if (diagnostics.Items.Count == 0)
        {
            sb.AppendLine(ResUI.MsgUnifiedPreviewNoIssues);
        }
        foreach (var item in diagnostics.Items.DistinctBy(t => t.Message))
        {
            sb.AppendLine($"{(item.Level == EDiagnosticLevel.Warning ? "!" : "~")} {item.Message}");
        }
        sb.AppendLine();
        sb.Append(body);
        return sb.ToString();
    }

    private static async Task<string> PreviewJsonAsync(Config config, RoutingItem routing, ECoreType core, UnifiedDiagnostics diagnostics)
    {
        // A placeholder node: only the DNS and routing sections are shown, which do not depend on it.
        var node = new ProfileItem
        {
            IndexId = PreviewNodeId,
            ConfigType = EConfigType.SOCKS,
            CoreType = core,
            Remarks = "preview",
            Address = Global.Loopback,
            Port = 1080,
            Network = nameof(ETransport.raw),
        };
        var builder = await CoreConfigContextBuilder.Build(config, node);
        var context = builder.Context with { RunCoreType = core, RoutingItem = routing, Node = node };
        UnifiedPlanFactory.ForContext(context, core, diagnostics);

        var result = core == ECoreType.sing_box
            ? new CoreConfigSingboxService(context).GenerateClientConfigContent()
            : new CoreConfigV2rayService(context).GenerateClientConfigContent();
        if (result.Success != true || JsonUtils.ParseJson(result.Data?.ToString()) is not JsonObject json)
        {
            return result.Msg ?? string.Empty;
        }
        var sections = new JsonObject();
        foreach (var key in new[] { "dns", "routing", "route" })
        {
            if (json[key] is { } section)
            {
                sections[key] = section.DeepClone();
            }
        }
        return JsonUtils.Serialize(sections, true);
    }

    private static async Task<string> PreviewMihomoAsync(Config config, RoutingItem routing, UnifiedDiagnostics diagnostics)
    {
        var ext = config.CoreExtensionItem?.Mihomo ?? new MihomoExtension();
        var groupProfiles = new List<ProfileItem>();
        foreach (var rule in JsonUtils.Deserialize<List<RulesItem>>(routing?.RuleSet) ?? [])
        {
            if (rule.Enabled
                && !Global.OutboundTags.Contains(rule.OutboundTag ?? Global.ProxyTag)
                && await AppManager.Instance.GetProfileItemViaRemarks(rule.OutboundTag) is { } profile)
            {
                groupProfiles.Add(profile);
            }
        }
        var plan = UnifiedPlanBuilder.Build(config.SimpleDNSItem, routing, config.RoutingBasicItem?.DomainStrategy,
            config.RuleSetItems, await UnifiedPlanBuilder.BuildGroupsAsync(groupProfiles));
        var validated = UnifiedPlanValidator.Validate(plan, ECoreType.mihomo, diagnostics);

        var fileContent = await LoadActiveClashConfig(config) ?? CreateStubClashConfig(ext);
        new MihomoTranslator(validated, ext, config.ClashUIItem?.EnableIPv6 ?? false, diagnostics).Apply(fileContent);

        var sections = new Dictionary<string, object>();
        foreach (var key in new[] { "dns", "hosts", "rule-providers", "rules" })
        {
            if (fileContent.GetValueOrDefault(key) is { } section)
            {
                sections[key] = section;
            }
        }
        if (fileContent.GetValueOrDefault("proxy-groups") is List<object> groups)
        {
            // Members are listed by the configuration itself; only show which groups exist.
            sections["proxy-groups"] = groups.OfType<Dictionary<object, object>>()
                .Select(g => $"{g.GetValueOrDefault("name")} ({g.GetValueOrDefault("type")})")
                .Cast<object>()
                .ToList();
        }
        return YamlUtils.ToYaml(sections);
    }

    /// <summary>
    /// Uses the selected server's Clash configuration when it is one, so the preview matches what runs.
    /// </summary>
    private static async Task<Dictionary<string, object>?> LoadActiveClashConfig(Config config)
    {
        var node = await ConfigHandler.GetDefaultServer(config);
        if (node is not { ConfigType: EConfigType.Custom, CoreType: ECoreType.mihomo } || node.Address.IsNullOrEmpty())
        {
            return null;
        }
        var path = File.Exists(node.Address) ? node.Address : Path.Combine(Utils.GetConfigPath(), node.Address);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var text = await File.ReadAllTextAsync(path);
            if (text.Contains("<<:") && text.Contains('*') && text.Contains('&'))
            {
                text = YamlUtils.PreprocessYaml(text) ?? text;
            }
            return YamlUtils.FromYaml<Dictionary<string, object>>(text);
        }
        catch (YamlException)
        {
            return null;
        }
    }

    public static Dictionary<string, object> CreateStubClashConfig(MihomoExtension ext)
    {
        var proxyGroup = ext.ProxyGroupName.IsNullOrEmpty() ? "Proxy" : ext.ProxyGroupName;
        return new Dictionary<string, object>
        {
            ["proxies"] = new List<object>(),
            ["proxy-groups"] = new List<object>
            {
                new Dictionary<object, object>
                {
                    ["name"] = proxyGroup,
                    ["type"] = "select",
                    ["proxies"] = new List<object> { "DIRECT" },
                },
            },
            ["rules"] = new List<object> { $"MATCH,{proxyGroup}" },
        };
    }
}
