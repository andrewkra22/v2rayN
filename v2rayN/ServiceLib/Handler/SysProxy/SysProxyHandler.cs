namespace ServiceLib.Handler.SysProxy;

public static class SysProxyHandler
{
    private static readonly string _tag = "SysProxyHandler";
    private static readonly Lazy<PacManager> _pacManager = new(() => new PacManager());

    public static async Task<bool> UpdateSysProxy(Config config, bool forceDisable)
    {
        var type = config.SystemProxyItem.SysProxyType;

        if (forceDisable && type != ESysProxyType.Unchanged)
        {
            type = ESysProxyType.ForcedClear;
        }

        try
        {
            var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
            if (port <= 0)
            {
                return false;
            }
            switch (type)
            {
                case ESysProxyType.ForcedChange when Utils.IsWindows():
                    var (strProxy, strExceptions) = GetWindowsProxyString(config, port);
                    BackupWindowsProxy(config, port, strProxy, null);
                    ProxySettingWindows.SetProxy(strProxy, strExceptions, 2);
                    break;

                case ESysProxyType.ForcedChange when Utils.IsLinux():
                    var exceptions = SanitizeExceptions(config);
                    await ProxySettingLinux.SetProxy(Global.Loopback, port, exceptions);
                    break;

                case ESysProxyType.ForcedChange when Utils.IsMacOS():
                    var exceptions2 = SanitizeExceptions(config);
                    await ProxySettingOSX.SetProxy(Global.Loopback, port, exceptions2);
                    break;

                case ESysProxyType.ForcedClear when Utils.IsWindows():
                    if (!RestoreWindowsProxy())
                    {
                        ProxySettingWindows.UnsetProxy();
                    }
                    break;

                case ESysProxyType.ForcedClear when Utils.IsLinux():
                    await ProxySettingLinux.UnsetProxy();
                    break;

                case ESysProxyType.ForcedClear when Utils.IsMacOS():
                    await ProxySettingOSX.UnsetProxy();
                    break;

                case ESysProxyType.Pac when Utils.IsWindows():
                    await SetWindowsProxyPac(config, port);
                    break;
            }

            if (type != ESysProxyType.Pac && Utils.IsWindows())
            {
                _pacManager.Value.Stop();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        return true;
    }

    private static string SanitizeExceptions(Config config)
    {
        var exceptions = config.SystemProxyItem.SystemProxyExceptions;
        if (exceptions.IsNullOrEmpty())
        {
            return string.Empty;
        }

        var items = exceptions
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Replace(" ", string.Empty))
            .Where(item => item.Length > 0);

        return string.Join(',', items);
    }

    private static (string strProxy, string strExceptions) GetWindowsProxyString(Config config, int port)
    {
        var strExceptions = config.SystemProxyItem.SystemProxyExceptions.Replace(" ", "");
        if (config.SystemProxyItem.NotProxyLocalAddress)
        {
            strExceptions = $"<local>;{strExceptions}";
        }

        return (GetWindowsProxyServer(config, port), strExceptions);
    }

    private static string GetWindowsProxyServer(Config config, int port)
    {
        if (config.SystemProxyItem.SystemProxyAdvancedProtocol.IsNullOrEmpty())
        {
            return $"{Global.Loopback}:{port}";
        }

        return config.SystemProxyItem.SystemProxyAdvancedProtocol
            .Replace("{ip}", Global.Loopback)
            .Replace("{http_port}", port.ToString())
            .Replace("{socks_port}", port.ToString());
    }

    [SupportedOSPlatform("windows")]
    private static async Task SetWindowsProxyPac(Config config, int port)
    {
        var portPac = AppManager.Instance.GetLocalPort(EInboundProtocol.pac);
        await _pacManager.Value.StartAsync(port, portPac);
        var strProxy = $"{Global.HttpProtocol}{Global.Loopback}:{portPac}/pac?t={DateTime.Now.Ticks}";
        BackupWindowsProxy(config, port, null, strProxy);
        ProxySettingWindows.SetProxy(strProxy, "", 4);
    }

    #region Windows original proxy backup

    private const string WindowsProxySnapshotFile = "sysproxy_backup.json";

    /// <summary>
    /// Save the system proxy settings v2rayN is about to replace, so they can be put back later.
    /// While v2rayN's own proxy is still set (mode switch, core reload, or a crash last time),
    /// the snapshot taken before it is kept; otherwise the current settings are the original ones.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void BackupWindowsProxy(Config config, int port, string? appliedProxyServer, string? appliedAutoConfigUrl)
    {
        try
        {
            var fileName = Utils.GetConfigPath(WindowsProxySnapshotFile);
            var snapshot = File.Exists(fileName)
                ? JsonUtils.Deserialize<WindowsProxySnapshot>(File.ReadAllText(fileName))
                : null;

            var current = ProxySettingWindows.GetProxySnapshot();
            var lan = current.Connections.FirstOrDefault(t => t.Connection == null);
            var ownProxyServer = GetWindowsProxyServer(config, port);
            var ownAutoConfigUrl = $"{Global.HttpProtocol}{Global.Loopback}:{AppManager.Instance.GetLocalPort(EInboundProtocol.pac)}/pac";
            var isSetByApp = WindowsProxySnapshot.IsSetByApp(lan, ownProxyServer, ownAutoConfigUrl)
                || (snapshot != null && WindowsProxySnapshot.IsSetByApp(lan, snapshot.AppliedProxyServer, snapshot.AppliedAutoConfigUrl));

            if (!isSetByApp)
            {
                snapshot = current;
            }
            else if (snapshot == null)
            {
                // v2rayN's proxy was left behind without a snapshot (e.g. by an earlier version);
                // the original settings are unknown, so restoring will clear the proxy as before.
                snapshot = new WindowsProxySnapshot();
            }

            snapshot.AppliedProxyServer = appliedProxyServer;
            snapshot.AppliedAutoConfigUrl = appliedAutoConfigUrl;
            File.WriteAllText(fileName, JsonUtils.Serialize(snapshot));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    /// <summary>
    /// Put back the system proxy settings saved by <see cref="BackupWindowsProxy"/>.
    /// Nothing is changed when another program (e.g. a group policy refresh) has replaced
    /// v2rayN's proxy in the meantime.
    /// </summary>
    /// <returns>false when there is nothing to restore and the proxy should be cleared as before</returns>
    [SupportedOSPlatform("windows")]
    private static bool RestoreWindowsProxy()
    {
        var fileName = Utils.GetConfigPath(WindowsProxySnapshotFile);
        if (!File.Exists(fileName))
        {
            return false;
        }

        try
        {
            var snapshot = JsonUtils.Deserialize<WindowsProxySnapshot>(File.ReadAllText(fileName));
            if (snapshot == null)
            {
                return false;
            }

            var current = ProxySettingWindows.GetConnectionProxy(null);
            if (!WindowsProxySnapshot.IsSetByApp(current, snapshot.AppliedProxyServer, snapshot.AppliedAutoConfigUrl))
            {
                Logging.SaveLog($"{_tag}: system proxy was changed by another program, keep the current settings");
                return true;
            }

            ProxySettingWindows.RestoreProxySnapshot(snapshot);
            Logging.SaveLog($"{_tag}: original system proxy restored");
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return false;
        }
        finally
        {
            try
            {
                File.Delete(fileName);
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
            }
        }
    }

    #endregion Windows original proxy backup
}
