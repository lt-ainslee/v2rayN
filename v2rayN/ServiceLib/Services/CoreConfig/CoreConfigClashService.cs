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

            //General mihomo options from the core extensions
            ApplyGeneralOptions(fileContent, config.CoreExtensionItem?.Mihomo);

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

    public static readonly IReadOnlyList<string> FindProcessModes = ["always", "strict", "off"];

    /// <summary>
    /// General options of the mihomo extension. They do not depend on the shared settings, so they apply
    /// whatever the unified mode; an unset option keeps the configuration file's value.
    /// </summary>
    public static void ApplyGeneralOptions(Dictionary<string, object> fileContent, MihomoExtension? ext)
    {
        if (ext == null)
        {
            return;
        }
        if (ext.UnifiedDelay is { } unifiedDelay)
        {
            fileContent["unified-delay"] = unifiedDelay;
        }
        if (ext.TcpConcurrent is { } tcpConcurrent)
        {
            fileContent["tcp-concurrent"] = tcpConcurrent;
        }
        if (ext.FindProcessMode is { } mode && FindProcessModes.Contains(mode))
        {
            fileContent["find-process-mode"] = mode;
        }
        if (ext.KeepAliveInterval is > 0)
        {
            fileContent["keep-alive-interval"] = ext.KeepAliveInterval.Value;
        }
        if (ext.SnifferEnabled is { } snifferEnabled)
        {
            if (fileContent.GetValueOrDefault("sniffer") is Dictionary<object, object> sniffer)
            {
                sniffer["enable"] = snifferEnabled;
            }
            else if (snifferEnabled)
            {
                fileContent["sniffer"] = new Dictionary<object, object>
                {
                    ["enable"] = true,
                    ["parse-pure-ip"] = true,
                    ["sniff"] = new Dictionary<object, object>
                    {
                        ["HTTP"] = new Dictionary<object, object> { ["ports"] = new List<object> { "80", "8080-8880" }, ["override-destination"] = true },
                        ["TLS"] = new Dictionary<object, object> { ["ports"] = new List<object> { "443", "8443" } },
                        ["QUIC"] = new Dictionary<object, object> { ["ports"] = new List<object> { "443", "8443" } },
                    },
                };
            }
        }
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

    /// <summary>
    /// Applies a Mixin "prepend-key" / "append-key" / "removed-key" entry to a list of the configuration.
    /// Removal matches strings by prefix (e.g. a rule) and mappings by name (e.g. a proxy group).
    /// Entries that do not fit (the key is not a list) are skipped instead of failing the whole Mixin.
    /// </summary>
    public static void ModifyContentMerge(Dictionary<string, object> fileContent, string key, object value)
    {
        string action;
        if (key.StartsWith("prepend-", StringComparison.Ordinal))
        {
            action = "prepend";
        }
        else if (key.StartsWith("append-", StringComparison.Ordinal))
        {
            action = "append";
        }
        else if (key.StartsWith("removed-", StringComparison.Ordinal))
        {
            action = "removed";
        }
        else
        {
            return;
        }
        key = key[(action.Length + 1)..];
        var lstValue = value as List<object> ?? (value is null ? [] : [value]);
        var existing = fileContent.GetValueOrDefault(key);

        if (action == "removed")
        {
            if (existing is not List<object> lstRemove)
            {
                return;
            }
            foreach (var item in lstValue.Select(t => t?.ToString()).OfType<string>())
            {
                lstRemove.RemoveAll(t => t switch
                {
                    Dictionary<object, object> map => Equals(map.GetValueOrDefault("name")?.ToString(), item),
                    _ => t?.ToString()?.StartsWith(item, StringComparison.Ordinal) == true,
                });
            }
            return;
        }

        if (existing == null)
        {
            fileContent[key] = lstValue.ToList();
            return;
        }
        if (existing is not List<object> lstOri)
        {
            Logging.SaveLog($"{_tag}-Mixin: {key} is not a list, {action}-{key} skipped");
            return;
        }
        if (action == "prepend")
        {
            lstOri.InsertRange(0, lstValue);
        }
        else
        {
            lstOri.AddRange(lstValue);
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
