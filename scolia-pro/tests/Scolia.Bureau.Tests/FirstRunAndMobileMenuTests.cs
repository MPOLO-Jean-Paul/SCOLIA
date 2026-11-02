using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

public class FirstRunAndMobileMenuTests
{
    [Fact]
    public void Aucun_login_avant_configuration_connue()
    {
        var shell = new ShellViewModel(
            new ScoliaClient(new Uri("http://localhost:5080/")), new Preferences());
        Assert.True(shell.ShowStartup);
        Assert.False(shell.ShowLogin);
        Assert.False(shell.ShowSetup);
        shell.IsSetupRequired = true;
        Assert.False(shell.ShowLogin);
        Assert.False(shell.ShowSetup);
    }

    [Fact]
    public void Categories_mobiles_repliables()
    {
        var home = new NavGroupVm("", Array.Empty<NavItemVm>());
        var settings = new NavGroupVm("Paramètres", Array.Empty<NavItemVm>());
        Assert.True(home.IsExpanded);
        Assert.False(settings.IsExpanded);
        settings.Toggle.Execute(null);
        Assert.True(settings.IsExpanded);

        var tools = new PhoneToolGroupVm("Paramètres", Array.Empty<RibbonCommandVm>());
        Assert.False(tools.IsExpanded);
        tools.Toggle.Execute(null);
        Assert.True(tools.IsExpanded);
    }
}
