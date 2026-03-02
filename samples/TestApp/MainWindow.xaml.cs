using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TestApp;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(InputTestPage));
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            switch (item.Tag?.ToString())
            {
                case "input": ContentFrame.Navigate(typeof(InputTestPage)); break;
                case "list": ContentFrame.Navigate(typeof(ListTestPage)); break;
                case "states": ContentFrame.Navigate(typeof(VisualStatesPage)); break;
            }
        }
    }
}
