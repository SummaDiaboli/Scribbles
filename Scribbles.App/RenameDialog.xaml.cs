using System.Windows;

namespace Scribbles.App;

/// <summary>Simple name prompt. Returns the trimmed stem on OK, null on cancel.</summary>
public partial class RenameDialog : Window
{
    public string? NewName { get; private set; }

    public RenameDialog(string currentStem)
    {
        InitializeComponent();
        NameBox.Text = currentStem;
        NameBox.SelectAll();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string stem = NameBox.Text.Trim();
        if (stem.Length == 0)
            return;
        NewName = stem;
        DialogResult = true;
    }
}
