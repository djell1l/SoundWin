using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace SoundWin.App.Converters;

public class StringVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return !string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class BooleanVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class BooleanPlayIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? "\uE769" : "\uE768";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class BooleanHeartConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? "\uEB52" : "\uEB51";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class BooleanOrangeBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush OrangeBrush = new(ColorHelper.FromArgb(255, 255, 85, 0));
    private static readonly SolidColorBrush GrayBrush = new(ColorHelper.FromArgb(255, 140, 140, 140));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? OrangeBrush : GrayBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class BooleanOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? 1.0 : 0.45;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
