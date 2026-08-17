using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Blight.Blare.App.ViewModels;

public sealed class SessionRowViewModel : INotifyPropertyChanged
{
    private double _volumePercent;
    private double _maxVolumePercent = 150;
    private double _meterPercent;
    private bool _isMuted;
    private BitmapImage? _icon;

    /// <summary>A representative process for this app — an app may be backed by several.</summary>
    public uint ProcessId { get; set; }

    /// <summary>Stable grouping key for the app this strip represents.</summary>
    public string AppKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Stable identity used as the persistence key — empty when the process couldn't be resolved.</summary>
    public string ExecutablePath { get; set; } = string.Empty;


    public double VolumePercent
    {
        get => _volumePercent;
        set => SetField(ref _volumePercent, value);
    }

    /// <summary>Upper bound for the volume slider — 150 by default, 300 once the ceiling override consent is granted.</summary>
    public double MaxVolumePercent
    {
        get => _maxVolumePercent;
        set => SetField(ref _maxVolumePercent, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => SetField(ref _isMuted, value);
    }

    /// <summary>0-100 live peak level for the meter — attack-fast/decay-slow smoothed by whoever's polling, not raw per-tick jitter.</summary>
    public double MeterPercent
    {
        get => _meterPercent;
        set => SetField(ref _meterPercent, value);
    }


    public BitmapImage? Icon
    {
        get => _icon;
        set => SetField(ref _icon, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
