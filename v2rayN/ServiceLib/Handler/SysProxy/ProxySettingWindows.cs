using static ServiceLib.Handler.SysProxy.ProxySettingWindows.InternetConnectionOption;

namespace ServiceLib.Handler.SysProxy;

[SupportedOSPlatform("windows")]
public static class ProxySettingWindows
{
    private const string _regPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    private static bool SetProxyFallback(string? strProxy, string? exceptions, int type)
    {
        if (type == 1)
        {
            WindowsUtils.RegWriteValue(_regPath, "ProxyEnable", 0);
            WindowsUtils.RegWriteValue(_regPath, "ProxyServer", string.Empty);
            WindowsUtils.RegWriteValue(_regPath, "ProxyOverride", string.Empty);
            WindowsUtils.RegWriteValue(_regPath, "AutoConfigURL", string.Empty);
        }
        if (type == 2)
        {
            WindowsUtils.RegWriteValue(_regPath, "ProxyEnable", 1);
            WindowsUtils.RegWriteValue(_regPath, "ProxyServer", strProxy ?? string.Empty);
            WindowsUtils.RegWriteValue(_regPath, "ProxyOverride", exceptions ?? string.Empty);
            WindowsUtils.RegWriteValue(_regPath, "AutoConfigURL", string.Empty);
        }
        else if (type == 4)
        {
            WindowsUtils.RegWriteValue(_regPath, "ProxyEnable", 0);
            WindowsUtils.RegWriteValue(_regPath, "ProxyServer", string.Empty);
            WindowsUtils.RegWriteValue(_regPath, "ProxyOverride", string.Empty);
            WindowsUtils.RegWriteValue(_regPath, "AutoConfigURL", strProxy ?? string.Empty);
        }
        return true;
    }

    /// <summary>
    // set to use no proxy
    /// </summary>
    /// <exception cref="ApplicationException">Error message with win32 error code</exception>
    public static bool UnsetProxy()
    {
        return SetProxy(null, null, 1);
    }

    /// <summary>
    /// Set system proxy settings
    /// </summary>
    /// <param name="strProxy"> proxy address</param>
    /// <param name="exceptions">exception addresses that do not use proxy</param>
    /// <param name="type">type of proxy defined in PerConnFlags
    ///     PROXY_TYPE_DIRECT           = 0x00000001, // direct connection (no proxy)
    ///     PROXY_TYPE_PROXY            = 0x00000002, // via named proxy
    ///     PROXY_TYPE_AUTO_PROXY_URL   = 0x00000004, // autoproxy script URL
    ///     PROXY_TYPE_AUTO_DETECT      = 0x00000008  // use autoproxy detection
    /// </param>
    /// <exception cref="ApplicationException">Error message with win32 error code</exception>
    /// <returns>true: one of connection is successfully updated proxy settings</returns>
    public static bool SetProxy(string? strProxy, string? exceptions, int type)
    {
        try
        {
            // set proxy for LAN
            var result = SetConnectionProxy(null, strProxy, exceptions, type);
            // set proxy for dial up connections
            var connections = EnumerateRasEntries();
            foreach (var connection in connections)
            {
                result |= SetConnectionProxy(connection, strProxy, exceptions, type);
            }
            return result;
        }
        catch
        {
            _ = SetProxyFallback(strProxy, exceptions, type);
            return false;
        }
    }

    private static bool SetConnectionProxy(string? connectionName, string? strProxy, string? exceptions, int type)
    {
        var list = new InternetPerConnOptionList();

        var optionCount = 1;
        if (type == 1) // No proxy
        {
            optionCount = 1;
        }
        else if (type is 2 or 4) // named proxy or autoproxy script URL
        {
            optionCount = exceptions.IsNullOrEmpty() ? 2 : 3;
        }

        var m_Int = (int)PerConnFlags.PROXY_TYPE_DIRECT;
        var m_Option = PerConnOption.INTERNET_PER_CONN_FLAGS;
        if (type == 2) // named proxy
        {
            m_Int = (int)(PerConnFlags.PROXY_TYPE_DIRECT | PerConnFlags.PROXY_TYPE_PROXY);
            m_Option = PerConnOption.INTERNET_PER_CONN_PROXY_SERVER;
        }
        else if (type == 4) // autoproxy script url
        {
            m_Int = (int)(PerConnFlags.PROXY_TYPE_DIRECT | PerConnFlags.PROXY_TYPE_AUTO_PROXY_URL);
            m_Option = PerConnOption.INTERNET_PER_CONN_AUTOCONFIG_URL;
        }

        var options = new InternetConnectionOption[optionCount];
        // USE a proxy server ...
        options[0].m_Option = PerConnOption.INTERNET_PER_CONN_FLAGS;
        options[0].m_Value.m_Int = m_Int;
        // use THIS proxy server
        if (optionCount > 1)
        {
            options[1].m_Option = m_Option;
            options[1].m_Value.m_StringPtr = Marshal.StringToHGlobalAuto(strProxy); // !! remember to deallocate memory 1
                                                                                    // except for these addresses ...
            if (optionCount > 2)
            {
                options[2].m_Option = PerConnOption.INTERNET_PER_CONN_PROXY_BYPASS;
                options[2].m_Value.m_StringPtr = Marshal.StringToHGlobalAuto(exceptions); // !! remember to deallocate memory 2
            }
        }

        // default stuff
        list.dwSize = Marshal.SizeOf(list);
        if (connectionName != null)
        {
            list.szConnection = Marshal.StringToHGlobalAuto(connectionName); // !! remember to deallocate memory 3
        }
        else
        {
            list.szConnection = nint.Zero;
        }
        list.dwOptionCount = options.Length;
        list.dwOptionError = 0;

        var optSize = Marshal.SizeOf<InternetConnectionOption>();
        // make a pointer out of all that ...
        var optionsPtr = Marshal.AllocCoTaskMem(optSize * options.Length); // !! remember to deallocate memory 4
                                                                           // copy the array over into that spot in memory ...
        for (var i = 0; i < options.Length; ++i)
        {
            if (Environment.Is64BitOperatingSystem)
            {
                var opt = new nint(optionsPtr.ToInt64() + (i * optSize));
                Marshal.StructureToPtr(options[i], opt, false);
            }
            else
            {
                var opt = new nint(optionsPtr.ToInt32() + (i * optSize));
                Marshal.StructureToPtr(options[i], opt, false);
            }
        }

        list.options = optionsPtr;

        // and then make a pointer out of the whole list
        var ipcoListPtr = Marshal.AllocCoTaskMem(list.dwSize); // !! remember to deallocate memory 5
        Marshal.StructureToPtr(list, ipcoListPtr, false);

        // and finally, call the API method!
        var isSuccess = NativeMethods.InternetSetOption(nint.Zero,
           InternetOption.INTERNET_OPTION_PER_CONNECTION_OPTION,
           ipcoListPtr, list.dwSize);
        var returnvalue = 0; // ERROR_SUCCESS
        if (!isSuccess)
        {  // get the error codes, they might be helpful
            returnvalue = Marshal.GetLastPInvokeError();
        }
        else
        {
            // Notify the system that the registry settings have been changed and cause them to be refreshed
            _ = NativeMethods.InternetSetOption(nint.Zero, InternetOption.INTERNET_OPTION_SETTINGS_CHANGED, nint.Zero, 0);
            _ = NativeMethods.InternetSetOption(nint.Zero, InternetOption.INTERNET_OPTION_REFRESH, nint.Zero, 0);
        }

        // FREE the data ASAP
        if (list.szConnection != nint.Zero)
        {
            Marshal.FreeHGlobal(list.szConnection); // release mem 3
        }
        if (optionCount > 1)
        {
            Marshal.FreeHGlobal(options[1].m_Value.m_StringPtr); // release mem 1
            if (optionCount > 2)
            {
                Marshal.FreeHGlobal(options[2].m_Value.m_StringPtr); // release mem 2
            }
        }
        Marshal.FreeCoTaskMem(optionsPtr); // release mem 4
        Marshal.FreeCoTaskMem(ipcoListPtr); // release mem 5
        if (returnvalue != 0)
        {
            // throw the error codes, they might be helpful
            throw new ApplicationException($"Set Internet Proxy failed with error code: {Marshal.GetLastWin32Error()}");
        }

        return true;
    }

    /// <summary>
    /// Read the current proxy settings of the LAN connection and of every dial up connection
    /// </summary>
    /// <exception cref="ApplicationException">Error message with win32 error code</exception>
    public static WindowsProxySnapshot GetProxySnapshot()
    {
        var snapshot = new WindowsProxySnapshot();
        snapshot.Connections.Add(GetConnectionProxy(null));

        IEnumerable<string> connections;
        try
        {
            connections = EnumerateRasEntries();
        }
        catch
        {
            // keep at least the LAN settings when dial up connections cannot be listed
            connections = [];
        }
        foreach (var connection in connections)
        {
            snapshot.Connections.Add(GetConnectionProxy(connection));
        }
        return snapshot;
    }

    /// <summary>
    /// Read the current proxy settings of one connection
    /// </summary>
    /// <param name="connectionName">dial up connection name, null for LAN</param>
    /// <exception cref="ApplicationException">Error message with win32 error code</exception>
    public static WindowsProxyConnectionSettings GetConnectionProxy(string? connectionName)
    {
        var options = new InternetConnectionOption[4];
        // INTERNET_PER_CONN_FLAGS_UI returns the flags as the user set them, including auto detect
        options[0].m_Option = PerConnOption.INTERNET_PER_CONN_FLAGS_UI;
        options[1].m_Option = PerConnOption.INTERNET_PER_CONN_PROXY_SERVER;
        options[2].m_Option = PerConnOption.INTERNET_PER_CONN_PROXY_BYPASS;
        options[3].m_Option = PerConnOption.INTERNET_PER_CONN_AUTOCONFIG_URL;

        var list = new InternetPerConnOptionList();
        list.dwSize = Marshal.SizeOf(list);
        list.szConnection = connectionName != null ? Marshal.StringToHGlobalAuto(connectionName) : nint.Zero;
        list.dwOptionCount = options.Length;
        list.dwOptionError = 0;

        var optSize = Marshal.SizeOf<InternetConnectionOption>();
        var optionsPtr = Marshal.AllocCoTaskMem(optSize * options.Length);
        var listPtr = nint.Zero;
        try
        {
            for (var i = 0; i < options.Length; ++i)
            {
                Marshal.StructureToPtr(options[i], optionsPtr + (i * optSize), false);
            }
            list.options = optionsPtr;

            listPtr = Marshal.AllocCoTaskMem(list.dwSize);
            Marshal.StructureToPtr(list, listPtr, false);

            var size = list.dwSize;
            if (!NativeMethods.InternetQueryOption(nint.Zero, InternetOption.INTERNET_OPTION_PER_CONNECTION_OPTION, listPtr, ref size))
            {
                throw new ApplicationException($"Query Internet Proxy failed with error code: {Marshal.GetLastPInvokeError()}");
            }

            for (var i = 0; i < options.Length; ++i)
            {
                options[i] = Marshal.PtrToStructure<InternetConnectionOption>(optionsPtr + (i * optSize));
            }

            return new WindowsProxyConnectionSettings
            {
                Connection = connectionName,
                Flags = options[0].m_Value.m_Int,
                ProxyServer = PtrToStringAndFree(options[1].m_Value.m_StringPtr),
                ProxyBypass = PtrToStringAndFree(options[2].m_Value.m_StringPtr),
                AutoConfigUrl = PtrToStringAndFree(options[3].m_Value.m_StringPtr),
            };
        }
        finally
        {
            if (list.szConnection != nint.Zero)
            {
                Marshal.FreeHGlobal(list.szConnection);
            }
            Marshal.FreeCoTaskMem(optionsPtr);
            if (listPtr != nint.Zero)
            {
                Marshal.FreeCoTaskMem(listPtr);
            }
        }
    }

    /// <summary>
    /// Put back proxy settings read by <see cref="GetProxySnapshot"/>.
    /// Dial up connections that did not exist when the snapshot was taken are set to use no proxy.
    /// </summary>
    /// <returns>true: one of connection is successfully restored</returns>
    public static bool RestoreProxySnapshot(WindowsProxySnapshot snapshot)
    {
        var lan = snapshot.Connections.FirstOrDefault(t => t.Connection == null);
        try
        {
            var result = RestoreConnectionProxy(null, lan);
            var connections = EnumerateRasEntries();
            foreach (var connection in connections)
            {
                var settings = snapshot.Connections.FirstOrDefault(t => t.Connection == connection);
                result |= RestoreConnectionProxy(connection, settings);
            }
            return result;
        }
        catch
        {
            _ = RestoreProxyFallback(lan);
            return false;
        }
    }

    private static bool RestoreConnectionProxy(string? connectionName, WindowsProxyConnectionSettings? settings)
    {
        if (settings == null)
        {
            return SetConnectionProxy(connectionName, null, null, 1);
        }
        return SetConnectionOptions(connectionName, settings.Flags, settings.ProxyServer, settings.ProxyBypass, settings.AutoConfigUrl);
    }

    private static bool RestoreProxyFallback(WindowsProxyConnectionSettings? settings)
    {
        if (settings == null)
        {
            return SetProxyFallback(null, null, 1);
        }

        var useProxy = (settings.Flags & (int)PerConnFlags.PROXY_TYPE_PROXY) != 0;
        var useAutoConfig = (settings.Flags & (int)PerConnFlags.PROXY_TYPE_AUTO_PROXY_URL) != 0;
        WindowsUtils.RegWriteValue(_regPath, "ProxyEnable", useProxy ? 1 : 0);
        WindowsUtils.RegWriteValue(_regPath, "ProxyServer", settings.ProxyServer ?? string.Empty);
        WindowsUtils.RegWriteValue(_regPath, "ProxyOverride", settings.ProxyBypass ?? string.Empty);
        WindowsUtils.RegWriteValue(_regPath, "AutoConfigURL", useAutoConfig ? settings.AutoConfigUrl ?? string.Empty : string.Empty);
        return true;
    }

    /// <summary>
    /// Set all proxy options of one connection at once
    /// </summary>
    /// <exception cref="ApplicationException">Error message with win32 error code</exception>
    private static bool SetConnectionOptions(string? connectionName, int flags, string? proxyServer, string? proxyBypass, string? autoConfigUrl)
    {
        var options = new InternetConnectionOption[4];
        options[0].m_Option = PerConnOption.INTERNET_PER_CONN_FLAGS;
        options[0].m_Value.m_Int = flags;
        options[1].m_Option = PerConnOption.INTERNET_PER_CONN_PROXY_SERVER;
        options[1].m_Value.m_StringPtr = Marshal.StringToHGlobalAuto(proxyServer ?? string.Empty);
        options[2].m_Option = PerConnOption.INTERNET_PER_CONN_PROXY_BYPASS;
        options[2].m_Value.m_StringPtr = Marshal.StringToHGlobalAuto(proxyBypass ?? string.Empty);
        options[3].m_Option = PerConnOption.INTERNET_PER_CONN_AUTOCONFIG_URL;
        options[3].m_Value.m_StringPtr = Marshal.StringToHGlobalAuto(autoConfigUrl ?? string.Empty);

        var list = new InternetPerConnOptionList();
        list.dwSize = Marshal.SizeOf(list);
        list.szConnection = connectionName != null ? Marshal.StringToHGlobalAuto(connectionName) : nint.Zero;
        list.dwOptionCount = options.Length;
        list.dwOptionError = 0;

        var optSize = Marshal.SizeOf<InternetConnectionOption>();
        var optionsPtr = Marshal.AllocCoTaskMem(optSize * options.Length);
        var listPtr = nint.Zero;
        try
        {
            for (var i = 0; i < options.Length; ++i)
            {
                Marshal.StructureToPtr(options[i], optionsPtr + (i * optSize), false);
            }
            list.options = optionsPtr;

            listPtr = Marshal.AllocCoTaskMem(list.dwSize);
            Marshal.StructureToPtr(list, listPtr, false);

            if (!NativeMethods.InternetSetOption(nint.Zero, InternetOption.INTERNET_OPTION_PER_CONNECTION_OPTION, listPtr, list.dwSize))
            {
                throw new ApplicationException($"Restore Internet Proxy failed with error code: {Marshal.GetLastPInvokeError()}");
            }

            // Notify the system that the registry settings have been changed and cause them to be refreshed
            _ = NativeMethods.InternetSetOption(nint.Zero, InternetOption.INTERNET_OPTION_SETTINGS_CHANGED, nint.Zero, 0);
            _ = NativeMethods.InternetSetOption(nint.Zero, InternetOption.INTERNET_OPTION_REFRESH, nint.Zero, 0);
            return true;
        }
        finally
        {
            if (list.szConnection != nint.Zero)
            {
                Marshal.FreeHGlobal(list.szConnection);
            }
            for (var i = 1; i < options.Length; ++i)
            {
                Marshal.FreeHGlobal(options[i].m_Value.m_StringPtr);
            }
            Marshal.FreeCoTaskMem(optionsPtr);
            if (listPtr != nint.Zero)
            {
                Marshal.FreeCoTaskMem(listPtr);
            }
        }
    }

    /// <summary>
    /// Strings returned by InternetQueryOption are allocated with GlobalAlloc and must be freed by the caller
    /// </summary>
    private static string? PtrToStringAndFree(nint ptr)
    {
        if (ptr == nint.Zero)
        {
            return null;
        }
        try
        {
            return Marshal.PtrToStringAuto(ptr);
        }
        finally
        {
            _ = NativeMethods.GlobalFree(ptr);
        }
    }

    /// <summary>
    /// Retrieve list of connections including LAN and WAN to support PPPoE connection
    /// </summary>
    /// <returns>A list of RAS connection names. May be empty list if no dial up connection.</returns>
    /// <exception cref="ApplicationException">Error message with win32 error code</exception>
    private static IEnumerable<string> EnumerateRasEntries()
    {
        var entries = 0;
        // attempt to query with 1 entry buffer
        var rasEntryNames = new RASENTRYNAME[1];
        var bufferSize = Marshal.SizeOf<RASENTRYNAME>();
        rasEntryNames[0].dwSize = Marshal.SizeOf<RASENTRYNAME>();

        var result = NativeMethods.RasEnumEntries(null, null, rasEntryNames, ref bufferSize, ref entries);
        // increase buffer if the buffer is not large enough
        if (result == (uint)ErrorCode.ERROR_BUFFER_TOO_SMALL)
        {
            rasEntryNames = new RASENTRYNAME[bufferSize / Marshal.SizeOf<RASENTRYNAME>()];
            for (var i = 0; i < rasEntryNames.Length; i++)
            {
                rasEntryNames[i].dwSize = Marshal.SizeOf<RASENTRYNAME>();
            }

            result = NativeMethods.RasEnumEntries(null, null, rasEntryNames, ref bufferSize, ref entries);
        }
        if (result == 0)
        {
            var entryNames = new List<string>();
            for (var i = 0; i < entries; i++)
            {
                entryNames.Add(rasEntryNames[i].szEntryName);
            }

            return entryNames;
        }
        throw new ApplicationException($"RasEnumEntries failed with error code: {result}");
    }

    #region WinInet structures

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct InternetPerConnOptionList
    {
        public int dwSize;               // size of the INTERNET_PER_CONN_OPTION_LIST struct
        public nint szConnection;         // connection name to set/query options
        public int dwOptionCount;        // number of options to set/query
        public int dwOptionError;           // on error, which option failed

        //[MarshalAs(UnmanagedType.)]
        public nint options;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct InternetConnectionOption
    {
        private static readonly int Size;
        public PerConnOption m_Option;
        public InternetConnectionOptionValue m_Value;

        static InternetConnectionOption()
        {
            Size = Marshal.SizeOf<InternetConnectionOption>();
        }

        // Nested Types
        [StructLayout(LayoutKind.Explicit)]
        public struct InternetConnectionOptionValue
        {
            // Fields
            [FieldOffset(0)]
            public System.Runtime.InteropServices.ComTypes.FILETIME m_FileTime;

            [FieldOffset(0)]
            public int m_Int;

            [FieldOffset(0)]
            public nint m_StringPtr;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct RASENTRYNAME
        {
            public int dwSize;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = RAS_MaxEntryName + 1)]
            public string szEntryName;

            public int dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH + 1)]
            public string szPhonebookPath;
        }

        // Constants
        public const int RAS_MaxEntryName = 256;

        public const int MAX_PATH = 260; // Standard MAX_PATH value in Windows
    }

    #endregion WinInet structures

    #region WinInet enums

    //
    // options manifests for Internet{Query|Set}Option
    //
    public enum InternetOption : uint
    {
        INTERNET_OPTION_PER_CONNECTION_OPTION = 75,
        INTERNET_OPTION_REFRESH = 37,
        INTERNET_OPTION_SETTINGS_CHANGED = 39
    }

    //
    // Options used in INTERNET_PER_CONN_OPTON struct
    //
    public enum PerConnOption
    {
        INTERNET_PER_CONN_FLAGS = 1, // Sets or retrieves the connection type. The Value member will contain one or more of the values from PerConnFlags
        INTERNET_PER_CONN_PROXY_SERVER = 2, // Sets or retrieves a string containing the proxy servers.
        INTERNET_PER_CONN_PROXY_BYPASS = 3, // Sets or retrieves a string containing the URLs that do not use the proxy server.
        INTERNET_PER_CONN_AUTOCONFIG_URL = 4, // Sets or retrieves a string containing the URL to the automatic configuration script.
        INTERNET_PER_CONN_FLAGS_UI = 10 // Retrieves the connection type as set in the UI, including PROXY_TYPE_AUTO_DETECT (Windows 7 and later).
    }

    //
    // PER_CONN_FLAGS
    //
    [Flags]
    public enum PerConnFlags
    {
        PROXY_TYPE_DIRECT = 0x00000001,  // direct to net
        PROXY_TYPE_PROXY = 0x00000002,  // via named proxy
        PROXY_TYPE_AUTO_PROXY_URL = 0x00000004,  // autoproxy URL
        PROXY_TYPE_AUTO_DETECT = 0x00000008   // use autoproxy detection
    }

    public enum ErrorCode : uint
    {
        ERROR_BUFFER_TOO_SMALL = 603,
        ERROR_INVALID_SIZE = 632
    }

    #endregion WinInet enums

    internal static class NativeMethods
    {
        [DllImport("WinInet.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InternetSetOption(nint hInternet, InternetOption dwOption, nint lpBuffer, int dwBufferLength);

        [DllImport("WinInet.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InternetQueryOption(nint hInternet, InternetOption dwOption, nint lpBuffer, ref int lpdwBufferLength);

        [DllImport("kernel32.dll")]
        public static extern nint GlobalFree(nint hMem);

        [DllImport("Rasapi32.dll", CharSet = CharSet.Auto)]
        public static extern uint RasEnumEntries(
            string? reserved,          // Reserved, must be null
            string? lpszPhonebook,     // Pointer to full path and filename of phone-book file. If this parameter is NULL, the entries are enumerated from all the remote access phone-book files
            [In, Out] RASENTRYNAME[]? lprasentryname, // Buffer to receive RAS entry names
            ref int lpcb,             // Size of the buffer
            ref int lpcEntries        // Number of entries written to the buffer
        );
    }
}
