using System.Xml.Linq;

namespace Scolia.Mobile.Tests;

public sealed class AndroidPrivacyManifestTests
{
    [Fact]
    public void Auto_backup_and_device_transfer_do_not_include_school_records()
    {
        var manifest = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AndroidManifest.xml"));
        var app = Assert.Single(manifest.Descendants("application"));
        XNamespace android = "http://schemas.android.com/apk/res/android";
        Assert.Equal("false", (string?)app.Attribute(android + "allowBackup"));
        Assert.Equal("false", (string?)app.Attribute(android + "fullBackupContent"));
        Assert.Equal("@xml/scolia_data_extraction", (string?)app.Attribute(android + "dataExtractionRules"));

        var rules = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "scolia_data_extraction.xml"));
        foreach (var section in new[] { "cloud-backup", "device-transfer" })
        {
            var scope = Assert.Single(rules.Root!.Elements(section));
            Assert.Contains(scope.Elements("exclude"), node =>
                (string?)node.Attribute("domain") == "root" && (string?)node.Attribute("path") == ".");
            Assert.Contains(scope.Elements("exclude"), node =>
                (string?)node.Attribute("domain") == "external" && (string?)node.Attribute("path") == ".");
        }
    }
}
