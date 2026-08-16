using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media.Imaging;

namespace BLight.Blare.App.ViewModels;

public sealed class SessionRowViewModel : INotifyPropertyChanged
{
    private double _volumePercent;
    private bool _isMuted;
    private BitmapImage? _icon;

    public uint ProcessId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Stable identity used as the persistence key — empty when the process couldn't be resolved.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    public double VolumePercent
    {
        get => _volumePercent;
        set => SetField(ref _volumePercent, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => SetField(ref _isMuted, value);
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
