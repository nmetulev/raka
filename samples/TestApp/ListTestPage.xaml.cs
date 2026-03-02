using Microsoft.UI.Xaml.Controls;

namespace TestApp;

public sealed partial class ListTestPage : Page
{
    public ListTestPage()
    {
        InitializeComponent();
        BoundItems.ItemsSource = new[] { "Bound Text Alpha", "Bound Text Beta", "Bound Text Gamma" };
    }

    private void ClickableList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TextBlock tb)
            ListClickStatus.Text = $"Last clicked: {tb.Text}";
    }

    private void SelectableList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectableList.SelectedItem is TextBlock tb)
            SelectionStatus.Text = $"Selected: {tb.Text}";
    }
}
