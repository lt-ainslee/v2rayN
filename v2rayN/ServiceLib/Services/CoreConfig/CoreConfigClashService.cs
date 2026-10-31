using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Core configuration file processing class.
/// The TUN state is taken as a snapshot so the generated config always agrees
/// with the launch elevation decision (see CoreManager.ShouldRunAsSudo).
/// When a context is given, the shared DNS / routing settings are applied according to
/// the mihomo extension's unified mode.
/// </summary>
public class CoreConfigClashService(Config config, bool isTunEnabled, CoreConfigContext? context = null)
{
    private static readonly string _tag = "CoreConfigClashService";

    public async Task<RetResult> GenerateClientCustomConfig(ProfileItem node, string? fileName)
    {
        var ret = new RetResult();
        if (node == null || fileName is null)
        {
            ret.Msg = ResUI.CheckServerSettings;
            return ret;
        }

        ret.Msg = ResUI.InitialConfiguration;

        try
        {
            if (node == null)
            {
                ret.Msg = ResUI.CheckServerSettings;
                return ret;
            }

            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }

            var addressFileName = node.Address;
            if (addressFileName.IsNullOrEmpty())
            {
                ret.Msg = ResUI.FailedGetDefaultConfiguration;
                return ret;
            }
            if (!File.Exists(addressFileName))
            {
                addressFileName = Path.Combine(Utils.GetConfigPath(), addressFileName);
            }
            if (!File.Exists(addressFileName))
            {
                ret.Msg = ResUI.FailedReadConfiguration + "1";
                return ret;
            }

            var fileContent = ParseClashContent(await File.ReadAllTextAsync(addressFileName));
            if (fileContent == null)
            {
                ret.Msg = ResUI.FailedConversionConfiguration;
                return ret;
            }

            //mixed-port
            fileContent["mixed-port"] = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            //log-level
            fileContent["log-level"] = GetLogLevel(config.CoreBasicItem.Loglevel);

            //external-controller
            fileContent["external-controller"] = $"{Global.Loopback}:{AppManager.Instance.StatePort2}";
            fileContent.Remove("secret");
            //allow-lan
            if (config.Inbound.First().AllowLANConn)
            {
                fileContent["allow-lan"] = "true";
                fileContent["bind-address"] = "*";
            }
            else
            {
                fileContent["allow-lan"] = "false";
            }

            //ipv6
            fileContent["ipv6"] = config.ClashUIItem.EnableIPv6;

            //mode
            fileContent.TryAdd("mode", nameof(ERuleMode.Rule));

            //enable tun mode
            if (isTunEnabled)
            {
                var tun = EmbedUtils.GetEmbedText(Global.ClashTunYaml);
                if (tun.IsNotEmpty())
                {
                    var tunContent = YamlUtils.FromYaml<Dictionary<string, object>>(tun);
                    if (tunContent != null)
                    {
                        fileContent["tun"] = tunContent["tun"];
                    }
                }
            }

            //Shared DNS / routing settings
            try
            {
                await ApplyUnifiedSettings(fileContent);
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"{_tag}-Unified", ex);
            }

            //Mixin
            try
            {
                await MixinContent(fileContent, node);
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"{_tag}-Mixin", ex);
            }

            // Mihomo parses plain values such as 815458e4 as floats, so quote REALITY short IDs.
            var originalRealityShortIds = new List<(Dictionary<object, object> RealityOptions, string ShortId)>();
            if (fileContent.GetValueOrDefault("proxies") is List<object> proxies)
            {
                foreach (var proxy in proxies.OfType<Dictionary<object, object>>())
                {
                    if (proxy.GetValueOrDefault("reality-opts") is Dictionary<object, object> realityOptions
                        && realityOptions.GetValueOrDefault("short-id") is string shortId
                        && !shortId.StartsWith(StrTagPlaceholder, StringComparison.Ordinal))
                    {
                        originalRealityShortIds.Add((realityOptions, shortId));
                        realityOptions["short-id"] = new YamlScalarNode(shortId) { Style = ScalarStyle.DoubleQuoted };
                    }
                }
            }

            var txtFileNew = RestoreStrTags(YamlUtils.ToYaml(fileContent));
            foreach (var (realityOptions, shortId) in originalRealityShortIds)
            {
                realityOptions["short-id"] = shortId;
            }
            await File.WriteAllTextAsync(fileName, txtFileNew);
            //check again
            if (!File.Exists(fileName))
            {
                ret.Msg = ResUI.FailedReadConfiguration + "2";
                return ret;
            }

            ret.Msg = string.Format(ResUI.SuccessfulConfiguration, $"{node.GetSummary()}");
            ret.Success = true;
            return ret;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            ret.Msg = ResUI.FailedGenDefaultConfiguration;
            return ret;
        }
    }

    /// <summary>
    /// Stands in for the "!&lt;str&gt;" tag, which YamlDotNet does not round-trip, until the YAML is written back.
    /// </summary>
    public const string StrTagPlaceholder = "__strn__";

    /// <summary>
    /// Parses a Clash configuration the way it is loaded at runtime (explicit string tags, merge keys).
    /// </summary>
    public static Dictionary<string, object>? ParseClashContent(string text)
    {
        text = text.Replace("!<str>", StrTagPlaceholder);
        if (text.Contains("<<:") && text.Contains('*') && text.Contains('&'))
        {
            text = YamlUtils.PreprocessYaml(text) ?? text;
        }
        return YamlUtils.FromYaml<Dictionary<string, object>>(text);
    }

    public static string RestoreStrTags(string yaml) => yaml.Replace(StrTagPlaceholder, "!!str");

    /// <summary>
    /// Loads the Clash configuration of a custom mihomo profile; null when it is not one or the file is missing.
    /// </summary>
    public static async Task<Dictionary<string, object>?> LoadProfileContentAsync(ProfileItem? node)
    {
        if (node is not { ConfigType: EConfigType.Custom, CoreType: ECoreType.mihomo } || node.Address.IsNullOrEmpty())
        {
            return null;
        }
        var path = File.Exists(node.Address) ? node.Address : Path.Combine(Utils.GetConfigPath(), node.Address);
        return File.Exists(path) ? ParseClashContent(await File.ReadAllTextAsync(path)) : null;
    }

    /// <summary>
    /// Rule sets mihomo can resolve: declared ones with a mihomo source and the file's own rule-providers.
    /// </summary>
    public static HashSet<string> GetAvailableRuleSetNames(Config config, Dictionary<string, object>? fileContent)
    {
        var names = new HashSet<string>((config.RuleSetItems ?? []).Where(t => t.MihomoSource.IsNotEmpty()).Select(t => t.Name));
        if (fileContent?.GetValueOrDefault("rule-providers") is Dictionary<object, object> providers)
        {
            names.UnionWith(providers.Keys.Select(k => k.ToString()).OfType<string>());
        }
        return names;
    }

    private async Task ApplyUnifiedSettings(Dictionary<string, object> fileContent)
    {
        var ext = config.CoreExtensionItem?.Mihomo;
        if (ext == null || ext.UnifiedMode == EMihomoUnifiedMode.Off || context == null)
        {
            return;
        }

        var groupProfiles = context.AllProxiesMap
            .Where(kvp => kvp.Key.StartsWith("remark:"))
            .Select(kvp => kvp.Value);
        var plan = UnifiedPlanBuilder.Build(
            context.SimpleDnsItem,
            context.RoutingItem,
            config.RoutingBasicItem?.DomainStrategy,
            config.RuleSetItems,
            await UnifiedPlanBuilder.BuildGroupsAsync(groupProfiles));

        // Validation diagnostics are reported when the context is built; only translation results are reported here.
        var validated = UnifiedPlanValidator.Validate(plan, ECoreType.mihomo, new UnifiedDiagnostics(),
            GetAvailableRuleSetNames(config, fileContent));
        var diagnostics = new UnifiedDiagnostics();
        new MihomoTranslator(validated, ext, config.ClashUIItem.EnableIPv6, diagnostics).Apply(fileContent);
        foreach (var item in diagnostics.Items)
        {
            NoticeManager.Instance.SendMessage(item.Message);
        }
    }

    private async Task MixinContent(Dictionary<string, object> fileContent, ProfileItem node)
    {
        if (!config.ClashUIItem.EnableMixinContent)
        {
            return;
        }

        var path = Utils.GetConfigPath(Global.ClashMixinConfigFileName);
        if (!File.Exists(path))
        {
            var mixin = EmbedUtils.GetEmbedText(Global.ClashMixinYaml);
            await File.AppendAllTextAsync(path, mixin);
        }

        var txtFile = await File.ReadAllTextAsync(Utils.GetConfigPath(Global.ClashMixinConfigFileName));

        var mixinContent = YamlUtils.FromYaml<Dictionary<string, object>>(txtFile);
        if (mixinContent == null)
        {
            return;
        }
        foreach (var item in mixinContent)
        {
            if (!isTunEnabled && item.Key == "tun")
            {
                continue;
            }

            if (item.Key.StartsWith("prepend-")
                || item.Key.StartsWith("append-")
                || item.Key.StartsWith("removed-"))
            {
                ModifyContentMerge(fileContent, item.Key, item.Value);
            }
            else
            {
                fileContent[item.Key] = item.Value;
            }
        }
        return;
    }

    private void ModifyContentMerge(Dictionary<string, object> fileContent, string key, object value)
    {
        var blPrepend = false;
        var blRemoved = false;
        if (key.StartsWith("prepend-"))
        {
            blPrepend = true;
            key = key.Replace("prepend-", "");
        }
        else if (key.StartsWith("append-"))
        {
            blPrepend = false;
            key = key.Replace("append-", "");
        }
        else if (key.StartsWith("removed-"))
        {
            blRemoved = true;
            key = key.Replace("removed-", "");
        }
        else
        {
            return;
        }

        if (!blRemoved && fileContent.TryAdd(key, value))
        {
            return;
        }
        var lstOri = (List<object>)fileContent[key];
        var lstValue = (List<object>)value;

        if (blRemoved)
        {
            foreach (var item in lstValue)
            {
                lstOri.RemoveAll(t => t.ToString().StartsWith(item.ToString()));
            }
            return;
        }

        if (blPrepend)
        {
            lstValue.Reverse();
            lstValue.ForEach(item => lstOri.Insert(0, item));
        }
        else
        {
            lstValue.ForEach(lstOri.Add);
        }
    }

    private string GetLogLevel(string level)
    {
        if (level == "none")
        {
            return "silent";
        }
        else
        {
            return level;
        }
    }
}
