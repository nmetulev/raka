using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SampleApp;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(HomePage));
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            switch (tag)
            {
                case "home": ContentFrame.Navigate(typeof(HomePage)); break;
                case "tools": ContentFrame.Navigate(typeof(ToolsPage)); break;
                case "settings": ContentFrame.Navigate(typeof(SettingsPage)); break;
            }
        }
    }
}
