using Microsoft.UI.Xaml;
using Raka.DevTools;

namespace SampleApp;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.UseRakaDevTools();
        _window.Activate();
    }
}
