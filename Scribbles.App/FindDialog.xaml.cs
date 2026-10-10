using System.Windows;
using System.Windows.Input;

namespace Scribbles.App;

/// <summary>Modeless find bar. The owner drives search; this only reports intent.</summary>
public partial class FindDialog : Window
{
    public event Action<string>? QueryChanged;
    public event Action? NextRequested;
    public event Action? PrevRequested;
    public event Action? FindClosed;

    public FindDialog()
    {
        InitializeComponent();
        QueryBox.TextChanged += (_, _) => QueryChanged?.Invoke(QueryBox.Text);
        QueryBox.PreviewKeyDown += QueryBox_PreviewKeyDown;
        Loaded += (_, _) => QueryBox.Focus();
        Closed += (_, _) => FindClosed?.Invoke();
    }

    public void ShowCount(int index1, int total)
    {
        CountText.Text = total == 0 ? "No matches." : $"Match {index1} of {total}.";
    }

    private void QueryBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            PrevRequested?.Invoke();
            e.Handled = true;
        }
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => PrevRequested?.Invoke();

    private void Next_Click(object sender, RoutedEventArgs e) => NextRequested?.Invoke();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
