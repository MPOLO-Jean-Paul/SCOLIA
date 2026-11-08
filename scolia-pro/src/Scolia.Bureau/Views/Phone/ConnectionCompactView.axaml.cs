using Avalonia.Controls;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Views.Phone;

/// <summary>
/// Choix du serveur sur Android : navigation par pages courtes, sans ScrollViewer.
/// Les champs, le QR et les commandes réutilisent exactement ConnectionViewModel.
/// </summary>
public partial class ConnectionCompactView : UserControl
{
    private enum Page { Qr, Code, Wifi, Servers, Address, Details }
    private Page current = Page.Qr;
    private Page beforeDetails = Page.Qr;

    public ConnectionCompactView()
    {
        InitializeComponent();
        AuthLayout.Track(CompactRoot, size =>
        {
            // La hauteur réellement disponible réagit aussi à l'ouverture du clavier Android.
            CompactRoot.Classes.Set("keyboard", size.Height < 470);
        });

        TabQr.Click += (_, _) => Show(Page.Qr);
        TabCode.Click += (_, _) => Show(Page.Code);
        TabWifi.Click += (_, _) => Show(Page.Wifi);
        ManualAddressButton.Click += (_, _) => Show(Page.Address);
        ChooseServerButton.Click += (_, _) => Show(Page.Servers);
        ReturnToWifiFromServers.Click += (_, _) => Show(Page.Wifi);
        ReturnToWifi.Click += (_, _) => Show(Page.Wifi);
        QrDetails.Click += (_, _) => ShowDetails();
        CodeDetails.Click += (_, _) => ShowDetails();
        WifiDetails.Click += (_, _) => ShowDetails();
        ReturnFromDetails.Click += (_, _) => Show(beforeDetails);

        Show(Page.Qr);
    }

    private void ShowDetails()
    {
        if (current != Page.Details) beforeDetails = current;
        Show(Page.Details);
    }

    private void Show(Page page)
    {
        current = page;
        QrPage.IsVisible = page == Page.Qr;
        CodePage.IsVisible = page == Page.Code;
        WifiPage.IsVisible = page == Page.Wifi;
        ServerPage.IsVisible = page == Page.Servers;
        AddressPage.IsVisible = page == Page.Address;
        DetailsPage.IsVisible = page == Page.Details;
        TabQr.Classes.Set("selected", page == Page.Qr);
        TabCode.Classes.Set("selected", page == Page.Code);
        TabWifi.Classes.Set("selected", page is Page.Wifi or Page.Servers or Page.Address);
    }
}
