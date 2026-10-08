using System.Windows;
using System.Windows.Controls;
using Scribbles.Core;

namespace Scribbles.App;

/// <summary>Edits a copy of AppSettings; returns the result on OK.</summary>
public partial class SettingsWindow : Window
{
    public AppSettings Result { get; private set; }

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        Result = current;

        ThemeBox.ItemsSource = Enum.GetValues<AppTheme>();
        ThemeBox.SelectedItem = current.Theme;
        PenBox.ItemsSource = Enum.GetValues<PenKind>();
        PenBox.SelectedItem = current.PenType;
        SizeBox.ItemsSource = new[] { "Fine (2)", "Medium (3.5)", "Bold (5)" };
        SizeBox.SelectedIndex = current.PenSizeDip switch { <= 2.5 => 0, >= 4.5 => 2, _ => 1 };
        AutosaveSlider.Value = Math.Clamp(current.AutosaveSeconds, 10, 120);
        CopyableBox.IsChecked = current.ExportCopyableDefault;
    }

    private void AutosaveSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Fires during InitializeComponent before AutosaveLabel exists.
        if (AutosaveLabel is not null)
            AutosaveLabel.Text = $"{(int)AutosaveSlider.Value}s";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        double size = SizeBox.SelectedIndex switch { 0 => 2.0, 2 => 5.0, _ => 3.5 };
        Result = Result with
        {
            Theme = (AppTheme)ThemeBox.SelectedItem,
            PenType = (PenKind)PenBox.SelectedItem,
            PenSizeDip = size,
            AutosaveSeconds = (int)AutosaveSlider.Value,
            ExportCopyableDefault = CopyableBox.IsChecked == true,
        };
        DialogResult = true;
    }
}
