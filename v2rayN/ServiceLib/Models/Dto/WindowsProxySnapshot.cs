namespace ServiceLib.Models.Dto;

/// <summary>
/// Windows system proxy settings captured before v2rayN changed them for the first time,
/// so they can be put back when v2rayN clears its own system proxy.
/// </summary>
public class WindowsProxySnapshot
{
    private const int ProxyTypeProxy = 0x00000002;
    private const int ProxyTypeAutoProxyUrl = 0x00000004;

    /// <summary>
    /// Settings of every connection (LAN and dial-up) as they were before v2rayN changed them.
    /// </summary>
    public List<WindowsProxyConnectionSettings> Connections { get; set; } = [];

    /// <summary>
    /// The named proxy v2rayN applied last time, or null when it applied a PAC URL.
    /// </summary>
    public string? AppliedProxyServer { get; set; }

    /// <summary>
    /// The PAC URL v2rayN applied last time, or null when it applied a named proxy.
    /// </summary>
    public string? AppliedAutoConfigUrl { get; set; }

    /// <summary>
    /// Checks whether the given settings are the ones v2rayN applies itself,
    /// either the named proxy or the PAC URL (the query string of a PAC URL is ignored).
    /// </summary>
    public static bool IsSetByApp(WindowsProxyConnectionSettings? settings, string? proxyServer, string? autoConfigUrl)
    {
        if (settings == null)
        {
            return false;
        }

        if (proxyServer.IsNotEmpty()
            && (settings.Flags & ProxyTypeProxy) != 0
            && string.Equals(settings.ProxyServer, proxyServer, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (autoConfigUrl.IsNotEmpty()
            && (settings.Flags & ProxyTypeAutoProxyUrl) != 0
            && settings.AutoConfigUrl.IsNotEmpty()
            && string.Equals(StripQuery(settings.AutoConfigUrl), StripQuery(autoConfigUrl), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string StripQuery(string url)
    {
        var index = url.IndexOf('?');
        return index < 0 ? url : url[..index];
    }
}

/// <summary>
/// Proxy settings of one WinINet connection.
/// </summary>
public class WindowsProxyConnectionSettings
{
    /// <summary>
    /// RAS entry name, or null for the LAN connection.
    /// </summary>
    public string? Connection { get; set; }

    /// <summary>
    /// PROXY_TYPE_* flags (direct, named proxy, PAC URL, auto-detect).
    /// </summary>
    public int Flags { get; set; }

    public string? ProxyServer { get; set; }

    public string? ProxyBypass { get; set; }

    public string? AutoConfigUrl { get; set; }
}
