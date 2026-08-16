using Microsoft.UI.Xaml.Controls;

namespace BLight.Blare.App.Views;

/// <summary>Same two-gate pattern as <see cref="DisableWarningsDialog"/>, tracked as an independent consent — see plan's Phase 2 safe-boost-ceiling section.</summary>
public sealed partial class RaiseBoostCeilingDialog : ContentDialog
{
    private const string RequiredPhrase = "allow";

    public RaiseBoostCeilingDialog()
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
