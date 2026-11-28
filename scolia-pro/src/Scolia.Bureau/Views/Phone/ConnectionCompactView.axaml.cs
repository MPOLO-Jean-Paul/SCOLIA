using System.ComponentModel;
using Avalonia.Controls;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;

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
    private ConnectionViewModel? subscribed;
    private bool keyboard;
    private bool compactNavigation = true;

    public ConnectionCompactView()
    {
        InitializeComponent();
        AuthLayout.Track(CompactRoot, size =>
        {
            // La hauteur réellement disponible réagit aussi à l'ouverture du clavier Android.
            keyboard = size.Height < 470;
            CompactRoot.Classes.Set("keyboard", keyboard);
            // Afficher la barre QR / Code / Wi-Fi UNIQUEMENT si l'espace est réduit.
            compactNavigation = size.Width < 390 || size.Height < 700;
            RefreshVisibleOptions();
        });

        TabQr.Click += (_, _) => Show(Page.Qr);
        TabCode.Click += (_, _) => Show(Page.Code);
        TabWifi.Click += (_, _) => Show(Page.Wifi);
        InlineCodeButton.Click += (_, _) => Show(Page.Code);
        InlineWifiButton.Click += (_, _) => Show(Page.Wifi);
        ReturnToQrFromCode.Click += (_, _) => Show(Page.Qr);
        ReturnToQrFromWifi.Click += (_, _) => Show(Page.Qr);
        ManualAddressButton.Click += (_, _) => Show(Page.Address);
        ChooseServerButton.Click += (_, _) => Show(Page.Servers);
        ReturnToWifiFromServers.Click += (_, _) => Show(Page.Wifi);
        ReturnToWifi.Click += (_, _) => Show(Page.Wifi);
        QrDetails.Click += (_, _) => ShowDetails();
        CodeDetails.Click += (_, _) => ShowDetails();
        WifiDetails.Click += (_, _) => ShowDetails();
        ReturnFromDetails.Click += (_, _) => Show(beforeDetails);

        DataContextChanged += (_, _) => Subscribe();
        AttachedToVisualTree += (_, _) => Subscribe();
        DetachedFromVisualTree += (_, _) => Release();
        Show(Page.Qr);
    }

    private void Subscribe()
    {
        Release();
        subscribed = DataContext as ConnectionViewModel;
        if (subscribed is not null) subscribed.PropertyChanged += OnConnectionChanged;
        RefreshVisibleOptions();
    }

    private void Release()
    {
        if (subscribed is not null) subscribed.PropertyChanged -= OnConnectionChanged;
        subscribed = null;
    }

    private void OnConnectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ConnectionViewModel.Message)
            or nameof(ConnectionViewModel.HasMessage)
            or nameof(ConnectionViewModel.PairingInProgress)) RefreshVisibleOptions();
    }

    private void RefreshVisibleOptions()
    {
        var vm = subscribed;
        var showDetails = !keyboard && vm?.HasMessage == true;
        QrDetails.IsVisible = showDetails;
        CodeDetails.IsVisible = showDetails;
        WifiDetails.IsVisible = showDetails;
        ImportQrButton.IsVisible = !keyboard && vm?.CanImportQrImage == true;
        HostServer.IsVisible = !keyboard && compactNavigation && vm?.PhoneCanBeServer == true;
        HostServerLarge.IsVisible = !keyboard && !compactNavigation && current == Page.Qr
            && vm?.PhoneCanBeServer == true;
        CompactFooter.IsVisible = compactNavigation && vm?.PairingInProgress != true;
        InlineMethods.IsVisible = !compactNavigation && current == Page.Qr;
        ReturnToQrFromCode.IsVisible = !compactNavigation && current == Page.Code;
        ReturnToQrFromWifi.IsVisible = !compactNavigation && current == Page.Wifi;
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
        RefreshVisibleOptions();
    }
}
