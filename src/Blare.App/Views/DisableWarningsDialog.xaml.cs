using Microsoft.UI.Xaml.Controls;

namespace BLight.Blare.App.Views;

/// <summary>
/// The deliberately-frictioned opt-out dialog from the plan: both a
/// checkbox acknowledgment AND an exact-match typed phrase are required
/// before the primary button enables — either gate alone would make this
/// too easy to click through without reading it.
/// </summary>
public sealed partial class DisableWarningsDialog : ContentDialog
{
    private const string RequiredPhrase = "disable";

    public DisableWarningsDialog()
    {
        InitializeComponent();
    }

    private void OnGateChanged(object sender, object e)
    {
        var checkboxOk = AcknowledgeCheckBox.IsChecked == true;
        var phraseOk = ConfirmationTextBox.Text.Trim().Equals(RequiredPhrase, StringComparison.OrdinalIgnoreCase);

        IsPrimaryButtonEnabled = checkboxOk && phraseOk;
    }
}
