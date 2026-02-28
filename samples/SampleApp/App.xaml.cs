using Microsoft.UI.Xaml;
#if RAKA_DEVTOOLS
using Raka.DevTools;
#endif

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
#if RAKA_DEVTOOLS
        _window.UseRakaDevTools();
#endif
        _window.Activate();
    }
}
