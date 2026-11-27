using System.Xml.Linq;
using Xunit;

namespace Scolia.Bureau.Tests;

public sealed class MobilePremiumWorkspaceTests
{
    private static XDocument Read(string relative)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Scolia.sln")))
            folder = folder.Parent;
        return XDocument.Load(Path.Combine(folder?.FullName ?? throw new DirectoryNotFoundException(),
            "src", "Scolia.Bureau", relative));
    }

    private static string? Attr(XElement element, string name) =>
        element.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;

    [Fact]
    public void Mobile_tabs_allow_two_lines_without_shrinking_text()
    {
        var style = Assert.Single(Read("Themes/Phone.axaml").Descendants(),
            x => Attr(x, "Selector") == "Button.phonetab TextBlock");
        Assert.Contains(style.Elements(), x => Attr(x, "Property") == "TextWrapping" && Attr(x, "Value") == "Wrap");
        Assert.Contains(style.Elements(), x => Attr(x, "Property") == "MaxLines" && Attr(x, "Value") == "2");
        Assert.Contains(style.Elements(), x => Attr(x, "Property") == "FontSize" && Attr(x, "Value") == "{DynamicResource FsCaption}");
    }

    [Fact]
    public void Teacher_phone_and_general_home_share_scalable_title()
    {
        foreach (var path in new[] { "Views/Phone/HomeView.axaml", "Views/TeacherHomeView.axaml" })
        {
            var doc = Read(path);
            Assert.Contains(doc.Descendants(), x => Attr(x, "Text") == "{Binding Greeting}" &&
                Attr(x, "FontSize") == "{DynamicResource FsTitle}" && Attr(x, "TextWrapping") == "Wrap");
        }
    }

    [Fact]
    public void Promoter_financial_labels_can_wrap()
    {
        var doc = Read("Views/PromoterDashboardView.axaml");
        foreach (var label in new[] { "Encaissé aujourd'hui", "Encaissé ce mois", "Reste à percevoir", "Déjà exigible" })
        {
            Assert.Contains(doc.Descendants(), x => Attr(x, "Text") == label && Attr(x, "TextWrapping") == "Wrap");
        }
    }

    [Fact]
    public void Mobile_forms_keep_tactile_fields_and_password_reserve()
    {
        var doc = Read("Views/FormView.axaml");
        var fields = Assert.Single(doc.Descendants(), x => Attr(x, "Selector") ==
            "LayoutTransformControl.phone Border.sheet TextBox.w11, LayoutTransformControl.phone Border.sheet ComboBox.w11, LayoutTransformControl.phone Border.sheet CalendarDatePicker.w11");
        Assert.Contains(fields.Elements(), x => Attr(x, "Property") == "MinHeight" && Attr(x, "Value") == "48");
        Assert.Contains(fields.Elements(), x => Attr(x, "Property") == "CornerRadius" && Attr(x, "Value") == "{DynamicResource RadiusInput}");
        Assert.Contains(doc.Descendants(), x => Attr(x, "RevealPassword") == "{Binding ShowSecret}" &&
            Attr(x, "Padding") == "14,8,52,8");
    }

    [Fact]
    public void Mobile_sticky_action_adapts_to_narrow_viewport()
    {
        var frame = Read("Views/PhoneFrame.axaml");
        var sticky = Assert.Single(frame.Descendants(), x => Attr(x, "Name") == "PhoneStickyBar");
        Assert.Contains(sticky.Elements(), x => Attr(x, "Adaptive.StackBelow") == "285");
        Assert.Contains(sticky.Descendants(), x => Attr(x, "Command") == "{Binding PhonePage.Sticky.Command}");
    }
}
