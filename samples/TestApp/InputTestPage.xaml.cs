using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Linq;

namespace TestApp;

public sealed partial class InputTestPage : Page
{
    private int _clickCount;
    private int _textChangedCount;
    private readonly string[] _suggestions = ["Apple", "Banana", "Cherry", "Date", "Elderberry", "Fig", "Grape"];

    public InputTestPage()
    {
        InitializeComponent();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var query = sender.Text.ToLowerInvariant();
            var filtered = _suggestions.Where(s => s.Contains(query, System.StringComparison.OrdinalIgnoreCase)).ToArray();
            sender.ItemsSource = filtered;
            SearchStatus.Text = $"Status: {filtered.Length} suggestions (reason: UserInput)";
        }
        else
        {
            SearchStatus.Text = $"Status: TextChanged (reason: {args.Reason})";
        }
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        SearchStatus.Text = $"Status: chose '{args.SelectedItem}'";
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _textChangedCount++;
        InputStatus.Text = $"TextChanged count: {_textChangedCount}, text: \"{InputBox.Text}\"";
    }

    private void TestButton_Click(object sender, RoutedEventArgs e)
    {
        _clickCount++;
        ButtonStatus.Text = $"Click count: {_clickCount}";
    }

    private void TestToggle_Toggled(object sender, RoutedEventArgs e)
    {
        ToggleStatus.Text = $"Toggle state: {(TestToggle.IsOn ? "On" : "Off")}";
    }

    private void HotkeyBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var modifiers = new System.Collections.Generic.List<string>();
        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        if (state.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) modifiers.Add("Ctrl");
        state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
        if (state.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) modifiers.Add("Shift");
        state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu);
        if (state.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) modifiers.Add("Alt");

        if (modifiers.Count > 0)
        {
            HotkeyStatus.Text = $"Last hotkey: {string.Join("+", modifiers)}+{e.Key}";
        }
    }
}
