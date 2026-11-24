namespace ServiceLib.Tests.Models;

public class WindowsProxySnapshotTests
{
    private const int ProxyTypeDirect = 0x00000001;
    private const int ProxyTypeProxy = 0x00000002;
    private const int ProxyTypeAutoProxyUrl = 0x00000004;
    private const int ProxyTypeAutoDetect = 0x00000008;

    [Test]
    public async Task IsSetByApp_NamedProxyApplied_ShouldBeTrue()
    {
        var settings = new WindowsProxyConnectionSettings
        {
            Flags = ProxyTypeDirect | ProxyTypeProxy,
            ProxyServer = "127.0.0.1:10808",
        };

        await WindowsProxySnapshot.IsSetByApp(settings, "127.0.0.1:10808", null).Should().BeTrue();
    }

    [Test]
    public async Task IsSetByApp_ProxyReplacedByGroupPolicy_ShouldBeFalse()
    {
        var settings = new WindowsProxyConnectionSettings
        {
            Flags = ProxyTypeDirect | ProxyTypeProxy,
            ProxyServer = "proxy.corp.local:3128",
            ProxyBypass = "<local>",
        };

        await WindowsProxySnapshot.IsSetByApp(settings, "127.0.0.1:10808", null).Should().BeFalse();
    }

    [Test]
    public async Task IsSetByApp_ProxyTurnedOff_ShouldBeFalse()
    {
        var settings = new WindowsProxyConnectionSettings
        {
            Flags = ProxyTypeDirect,
            ProxyServer = "127.0.0.1:10808",
        };

        await WindowsProxySnapshot.IsSetByApp(settings, "127.0.0.1:10808", null).Should().BeFalse();
    }

    [Test]
    public async Task IsSetByApp_PacUrlWithDifferentQuery_ShouldBeTrue()
    {
        var settings = new WindowsProxyConnectionSettings
        {
            Flags = ProxyTypeDirect | ProxyTypeAutoProxyUrl,
            AutoConfigUrl = "http://127.0.0.1:10810/pac?t=639000000000000000",
        };

        await WindowsProxySnapshot.IsSetByApp(settings, null, "http://127.0.0.1:10810/pac").Should().BeTrue();
    }

    [Test]
    public async Task IsSetByApp_CorporatePacUrl_ShouldBeFalse()
    {
        var settings = new WindowsProxyConnectionSettings
        {
            Flags = ProxyTypeDirect | ProxyTypeAutoProxyUrl | ProxyTypeAutoDetect,
            AutoConfigUrl = "http://wpad.corp.local/proxy.pac",
        };

        await WindowsProxySnapshot.IsSetByApp(settings, "127.0.0.1:10808", "http://127.0.0.1:10810/pac").Should().BeFalse();
    }

    [Test]
    public async Task IsSetByApp_NoSettings_ShouldBeFalse()
    {
        await WindowsProxySnapshot.IsSetByApp(null, "127.0.0.1:10808", null).Should().BeFalse();
    }

    [Test]
    public async Task Snapshot_JsonRoundTrip_ShouldKeepAllConnections()
    {
        var snapshot = new WindowsProxySnapshot
        {
            AppliedProxyServer = "127.0.0.1:10808",
            Connections =
            [
                new WindowsProxyConnectionSettings
                {
                    Flags = ProxyTypeDirect | ProxyTypeProxy | ProxyTypeAutoDetect,
                    ProxyServer = "proxy.corp.local:3128",
                    ProxyBypass = "*.corp.local;<local>",
                },
                new WindowsProxyConnectionSettings
                {
                    Connection = "VPN dial-up",
                    Flags = ProxyTypeDirect | ProxyTypeAutoProxyUrl,
                    AutoConfigUrl = "http://wpad.corp.local/proxy.pac",
                },
            ],
        };

        var result = JsonUtils.Deserialize<WindowsProxySnapshot>(JsonUtils.Serialize(snapshot))!;

        await result.Should().NotBeNull();
        await result.AppliedProxyServer.Should().BeEqualTo("127.0.0.1:10808");
        await result.AppliedAutoConfigUrl.Should().BeNull();
        await result.Connections.Should().HaveCount(2);

        var lan = result.Connections.First(t => t.Connection == null);
        await lan.Flags.Should().BeEqualTo(ProxyTypeDirect | ProxyTypeProxy | ProxyTypeAutoDetect);
        await lan.ProxyServer.Should().BeEqualTo("proxy.corp.local:3128");
        await lan.ProxyBypass.Should().BeEqualTo("*.corp.local;<local>");

        var dialUp = result.Connections.First(t => t.Connection == "VPN dial-up");
        await dialUp.AutoConfigUrl.Should().BeEqualTo("http://wpad.corp.local/proxy.pac");
    }
}
