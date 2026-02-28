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
    }

    private void GreetButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameInput.Text;
        OutputText.Text = string.IsNullOrWhiteSpace(name) ? "Hello, World!" : $"Hello, {name}!";
    }
}
