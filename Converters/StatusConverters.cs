using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace influx2Exporter.Converters
{
 public class StatusToBackgroundConverter : IValueConverter
 {
 public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
 {
 var status = (value as string)?.Trim() ?? string.Empty;
 switch (status.ToLowerInvariant())
 {
 case "connected":
 return (SolidColorBrush)new BrushConverter().ConvertFrom("#2BB24C");
 case "connecting":
 return (SolidColorBrush)new BrushConverter().ConvertFrom("#FFB74D");
 case "failed":
 case "disconnected":
 return (SolidColorBrush)new BrushConverter().ConvertFrom("#E57373");
 default:
 return (SolidColorBrush)new BrushConverter().ConvertFrom("#CCCCCC");
 }
 }

 public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
 {
 throw new NotSupportedException();
 }
 }

 public class StatusToBorderBrushConverter : IValueConverter
 {
 public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
 {
 var status = (value as string)?.Trim() ?? string.Empty;
 switch (status.ToLowerInvariant())
 {
 case "connected":
 return (SolidColorBrush)new BrushConverter().ConvertFrom("#23913F");
 case "connecting":
 return (SolidColorBrush)new BrushConverter().ConvertFrom("#F57C00");
 case "failed":
 case "disconnected":
 return (SolidColorBrush)new BrushConverter().ConvertFrom("#C62828");
 default:
 return (SolidColorBrush)new BrushConverter().ConvertFrom("#BBBBBB");
 }
 }

 public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
 {
 throw new NotSupportedException();
 }
 }
}
