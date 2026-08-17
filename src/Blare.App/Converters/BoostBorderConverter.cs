using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Blight.Blare.App.Converters;

/// <summary>Boosted rows get a visible orange edge instead of the plain card stroke — a glance should tell you which apps are boosted.</summary>
public sealed class BoostBorderConverter : IValueConverter
{
    private static readonly SolidColorBrush BoostedBrush = new(Color.FromArgb(255, 255, 140, 0));
    private static readonly SolidColorBrush DefaultBrush = new(Colors.Transparent);

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? BoostedBrush : DefaultBrush;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
