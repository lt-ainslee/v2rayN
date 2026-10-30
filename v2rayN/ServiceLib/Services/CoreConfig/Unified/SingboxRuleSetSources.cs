namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Rule set tags sing-box can resolve: declared rule sets with a sing-box source, tags from the routing
/// profile's custom rule set file, and local srss/&lt;tag&gt;.srs files (see ConvertGeo2Ruleset).
/// </summary>
public static class SingboxRuleSetSources
{
    public static HashSet<string> GetAvailableNames(Config config, RoutingItem? routing)
    {
        var names = new HashSet<string>(
            (config.RuleSetItems ?? []).Where(t => t.SingboxSource.IsNotEmpty()).Select(t => t.Name));
        try
        {
            if (routing?.CustomRulesetPath4Singbox.IsNotEmpty() == true
                && EmbedUtils.LoadResource(routing.CustomRulesetPath4Singbox) is { Length: > 0 } custom)
            {
                names.UnionWith((JsonUtils.Deserialize<List<Ruleset4Sbox>>(custom) ?? [])
                    .Select(t => t.tag)
                    .OfType<string>());
            }
            var localSrss = Utils.GetBinPath("srss");
            if (Directory.Exists(localSrss))
            {
                names.UnionWith(Directory.EnumerateFiles(localSrss, "*.srs").Select(Path.GetFileNameWithoutExtension).OfType<string>());
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(nameof(SingboxRuleSetSources), ex);
        }
        return names;
    }
}
