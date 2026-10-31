namespace ServiceLib.Services.CoreConfig;

/// <summary>
/// Validates the per-core extension settings before they are saved, so the desktop windows and the WebAPI
/// reject the same input. Each problem is returned as "field: value".
/// </summary>
public static class CoreExtensionValidator
{
    public static readonly IReadOnlyList<string> XrayQueryStrategies = ["UseIP", "UseIPv4", "UseIPv6", "UseSystem"];
    public static readonly IReadOnlyList<string> MihomoCacheAlgorithms = ["lru", "arc"];

    public static List<string> Validate(CoreExtensionItem? item)
    {
        var errors = new List<string>();
        if (item?.Xray == null || item.Singbox == null || item.Mihomo == null)
        {
            errors.Add(nameof(CoreExtensionItem));
            return errors;
        }

        var xray = item.Xray;
        if (xray.DnsQueryStrategy.IsNotEmpty() && !XrayQueryStrategies.Contains(xray.DnsQueryStrategy))
        {
            errors.Add($"Xray dns.queryStrategy: {xray.DnsQueryStrategy}");
        }
        if (xray.DnsClientIp.IsNotEmpty() && !IPAddress.TryParse(xray.DnsClientIp, out _))
        {
            errors.Add($"Xray dns.clientIp: {xray.DnsClientIp}");
        }
        if (xray.DnsServeExpiredTtl is < 0)
        {
            errors.Add($"Xray dns.serveExpiredTTL: {xray.DnsServeExpiredTtl}");
        }

        var singbox = item.Singbox;
        if (singbox.DnsClientSubnet.IsNotEmpty() && !IsIpOrCidr(singbox.DnsClientSubnet))
        {
            errors.Add($"sing-box dns.client_subnet: {singbox.DnsClientSubnet}");
        }
        if (singbox.DnsCacheCapacity is { } capacity && capacity != 0 && capacity < 1024)
        {
            errors.Add($"sing-box dns.cache_capacity: {capacity} (>= 1024)");
        }
        if (singbox.RouteDefaultMark is < 0)
        {
            errors.Add($"sing-box route.default_mark: {singbox.RouteDefaultMark}");
        }

        var mihomo = item.Mihomo;
        if (!Enum.IsDefined(mihomo.UnifiedMode))
        {
            errors.Add($"mihomo unified mode: {mihomo.UnifiedMode}");
        }
        if (mihomo.DnsCacheAlgorithm.IsNotEmpty() && !MihomoCacheAlgorithms.Contains(mihomo.DnsCacheAlgorithm))
        {
            errors.Add($"mihomo dns.cache-algorithm: {mihomo.DnsCacheAlgorithm}");
        }
        if (mihomo.FindProcessMode.IsNotEmpty() && !CoreConfigClashService.FindProcessModes.Contains(mihomo.FindProcessMode))
        {
            errors.Add($"mihomo find-process-mode: {mihomo.FindProcessMode}");
        }
        if (mihomo.KeepAliveInterval is < 0)
        {
            errors.Add($"mihomo keep-alive-interval: {mihomo.KeepAliveInterval}");
        }
        var invalidGroups = new List<string>();
        ClashSyntax.ParseProxyGroups(mihomo.ExtraProxyGroups, invalidGroups);
        errors.AddRange(invalidGroups.Select(line => $"{ResUI.TbClashProxyGroups}: {line}"));
        errors.AddRange(ClashSyntax.InvalidRuleLines(mihomo.ExtraRules).Select(line => $"{ResUI.TbClashRules}: {line}"));
        return errors;
    }

    /// <summary>An IP address or a CIDR prefix, e.g. 1.2.3.0/24 or 2001:db8::/56.</summary>
    public static bool IsIpOrCidr(string value)
    {
        var slash = value.IndexOf('/');
        if (slash < 0)
        {
            return IPAddress.TryParse(value, out _);
        }
        return IPAddress.TryParse(value[..slash], out var ip)
            && int.TryParse(value[(slash + 1)..], out var prefix)
            && prefix >= 0
            && prefix <= (ip.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32);
    }
}
