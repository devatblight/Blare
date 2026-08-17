using Blight.Blare.App.Services;
using Blight.Blare.Core.Settings;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Blight.Blare.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly SafetyMonitor _safetyMonitor;
    private readonly ThemeService _themeService;
    private readonly BackdropService _backdropService;
    private readonly FlyoutService _flyoutService;
    private readonly UpdateService _updateService;
    private readonly StartupService _startupService;
    private readonly LimitsStore _limits;
    private readonly Dictionary<FlyoutPosition, Button> _positionCells = new();
    private bool _initializing = true;

    public SettingsPage()
    {
        _safetyMonitor = App.Services.GetRequiredService<SafetyMonitor>();
        _themeService = App.Services.GetRequiredService<ThemeService>();
        _backdropService = App.Services.GetRequiredService<BackdropService>();
        _flyoutService = App.Services.GetRequiredService<FlyoutService>();
        _updateService = App.Services.GetRequiredService<UpdateService>();
        _startupService = App.Services.GetRequiredService<StartupService>();
        _limits = App.Services.GetRequiredService<LimitsStore>();

        InitializeComponent();

        BuildPositionGrid();
        LoadQuietHours();

        ThemeComboBox.SelectedIndex = _themeService.Current == BlareTheme.StudioDark ? 1 : 0;
        BackdropComboBox.SelectedIndex = (int)_backdropService.Requested;
        UpdateChecksToggle.IsOn = _updateService.ChecksEnabled;
        RunAtStartupToggle.IsOn = _startupService.RunsAtStartup;
        StartHiddenToggle.IsOn = _startupService.StartHidden;
        UpdateStartupCards();

        // Say so rather than silently substituting when the OS can't do Mica.
        if (!BackdropService.MicaSupported)
        {
            BackdropCard.Description = "Mica needs Windows 11 — Acrylic is used instead on this system.";
        }

        _initializing = false;

        UpdateToggleWarningsButton();
    }

    // ---- quiet hours ---------------------------------------------------------

    private void LoadQuietHours()
    {
        var quiet = _limits.Limits.QuietHours;

        QuietHoursToggle.IsOn = quiet.Enabled;
        QuietStartPicker.SelectedTime = quiet.Start.ToTimeSpan();
        QuietEndPicker.SelectedTime = quiet.End.ToTimeSpan();
        QuietCeilingBox.Value = quiet.CeilingPercent;

        UpdateQuietHoursSummary();
    }

    private void OnQuietHoursToggled(object sender, RoutedEventArgs e) => SaveQuietHours();

    private void OnQuietHoursEdited(TimePicker sender, TimePickerSelectedValueChangedEventArgs args) => SaveQuietHours();

    private void OnQuietCeilingChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        // NaN arrives while the box is being cleared mid-edit.
        if (!double.IsNaN(args.NewValue))
        {
            SaveQuietHours();
        }
    }

    private void SaveQuietHours()
    {
        if (_initializing)
        {
            return;
        }

        var start = TimeOnly.FromTimeSpan(QuietStartPicker.SelectedTime ?? TimeSpan.FromHours(23));
        var end = TimeOnly.FromTimeSpan(QuietEndPicker.SelectedTime ?? TimeSpan.FromHours(7));
        var ceiling = double.IsNaN(QuietCeilingBox.Value) ? 40 : QuietCeilingBox.Value;

        _limits.Limits.SetQuietHours(new Blight.Blare.Core.Safety.QuietHours(
            QuietHoursToggle.IsOn, start, end, ceiling));

        UpdateQuietHoursSummary();
    }

    /// <summary>
    /// Spells out what the window means, including that it runs past midnight —
    /// "23:00 until 07:00" is ambiguous enough to be worth saying plainly.
    /// </summary>
    private void UpdateQuietHoursSummary()
    {
        var quiet = _limits.Limits.QuietHours;

        if (!quiet.Enabled)
        {
            QuietHoursSummary.Text = "Off. Nothing is capped by time of day.";
            return;
        }

        var overnight = quiet.Start > quiet.End ? ", overnight" : string.Empty;
        var active = quiet.Contains(TimeOnly.FromDateTime(DateTime.Now)) ? " In force right now." : string.Empty;

        QuietHoursSummary.Text =
            $"From {quiet.Start:HH\\:mm} until {quiet.End:HH\\:mm}{overnight}, nothing goes above {quiet.CeilingPercent:F0}%.{active}";
    }

    private async void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ThemeComboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        if (Enum.TryParse<BlareTheme>(tag, out var theme))
        {
            await _themeService.SetAsync(theme);
        }
    }

    /// <summary>
    /// Nine cells laid over a mock display. Picking a cell is the whole
    /// interaction — no dropdown of position names, because "bottom right"
    /// is much easier to recognise as a shape than to read as a word.
    /// </summary>
    private void BuildPositionGrid()
    {
        foreach (var position in Enum.GetValues<FlyoutPosition>())
        {
            var cell = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Tag = position,
            };

            ToolTipService.SetToolTip(cell, DescribePosition(position));
            cell.Click += OnPositionCellClick;

            Grid.SetRow(cell, position.Row());
            Grid.SetColumn(cell, position.Column());
            PositionGrid.Children.Add(cell);
            _positionCells[position] = cell;
        }

        UpdatePositionSelection();
    }

    private async void OnPositionCellClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: FlyoutPosition position })
        {
            return;
        }

        await _flyoutService.SetPositionAsync(position);
        UpdatePositionSelection();
    }

    private void UpdatePositionSelection()
    {
        foreach (var (position, cell) in _positionCells)
        {
            var selected = position == _flyoutService.Position;

            cell.Background = selected
                ? (Brush)Application.Current.Resources["BlareAccent"]
                : (Brush)Application.Current.Resources["BlareStripBackground"];
            cell.Opacity = selected ? 1 : 0.5;
        }

        FlyoutPositionLabel.Text = DescribePosition(_flyoutService.Position);
    }

    private static string DescribePosition(FlyoutPosition position)
    {
        var row = position.Row() switch { 0 => "Top", 1 => "Middle", _ => "Bottom" };
        var column = position.Column() switch { 0 => "left", 1 => "centre", _ => "right" };
        return $"{row} {column}";
    }

    private void OnRunAtStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        if (!_startupService.SetRunAtStartup(RunAtStartupToggle.IsOn))
        {
            // Say so rather than leaving a toggle that claims something untrue.
            RunAtStartupToggle.IsOn = _startupService.RunsAtStartup;
            _flyoutService.Show(
                "Couldn't change startup",
                "Windows refused the change. Your startup apps can also be managed from Task Manager.",
                FlyoutTone.Caution,
                TimeSpan.FromSeconds(6));
        }

        UpdateStartupCards();
    }

    private async void OnStartHiddenToggled(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        await _startupService.SetStartHiddenAsync(StartHiddenToggle.IsOn);
    }

    private void UpdateStartupCards()
    {
        // Starting hidden only means anything when Windows is launching it.
        StartHiddenCard.IsEnabled = _startupService.RunsAtStartup;
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = "Checking...";

        var update = await _updateService.CheckAsync(notify: false);

        CheckUpdatesButton.Content = "Check now";
        CheckUpdatesButton.IsEnabled = true;

        if (update is not null)
        {
            _flyoutService.Show(
                $"Blare {update.Version} is available",
                $"You're on {UpdateService.CurrentVersion}.",
                FlyoutTone.Neutral,
                TimeSpan.FromSeconds(10),
                "Get it",
                () => UpdateService.OpenInBrowser(update.ReleaseUrl));
        }
        else
        {
            _flyoutService.Show(
                "Blare is up to date",
                _updateService.LastError is { } error
                    ? $"Couldn't reach GitHub — {error}"
                    : $"You're on {UpdateService.CurrentVersion}.",
                _updateService.LastError is null ? FlyoutTone.Neutral : FlyoutTone.Caution,
                TimeSpan.FromSeconds(5));
        }
    }

    private async void OnUpdateChecksToggled(object sender, RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        await _updateService.SetChecksEnabledAsync(UpdateChecksToggle.IsOn);
    }

    private void OnPreviewFlyoutClick(object sender, RoutedEventArgs e) =>
        _flyoutService.Show(
            "This is Blare",
            "Messages appear here. Hover to keep one on screen.",
            FlyoutTone.Neutral,
            TimeSpan.FromSeconds(4));

    private async void OnBackdropChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || BackdropComboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        if (Enum.TryParse<BackdropKind>(tag, out var kind))
        {
            await _backdropService.SetAsync(kind);
        }
    }

    private async void OnToggleWarningsClick(object sender, RoutedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow;

        if (_safetyMonitor.WarningsDisabled(now))
        {
            _safetyMonitor.ReenableWarnings();
            UpdateToggleWarningsButton();
            return;
        }

        var dialog = new DisableWarningsDialog { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            _safetyMonitor.DisableWarnings(now);
        }

        UpdateToggleWarningsButton();
    }

    private void UpdateToggleWarningsButton()
    {
        ToggleWarningsButton.Content = _safetyMonitor.WarningsDisabled(DateTimeOffset.UtcNow)
            ? "Re-enable"
            : "Disable";
    }
}
