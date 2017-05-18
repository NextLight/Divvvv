using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;

namespace Divvvv
{
    public partial class ListEpisode : DockPanel
    {
        private Episode _ep;

        public ListEpisode()
        {
            InitializeComponent();
        }

        private void DockPanel_Loaded(object sender, RoutedEventArgs e) => _ep = (Episode)DataContext;

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (_ep.DownloadStatus == HdsDump.DownloadStatus.Downloaded)
                Process.Start(_ep.FileName);
            else
                _ep.ToggleDownload();
        }

        private void Image_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_ep.DownloadStatus != HdsDump.DownloadStatus.Downloading)
                _ep.ToggleDownload();
            if (File.Exists(_ep.FileName))
                Process.Start(_ep.FileName);
        }
    }

    public class TimeSpanToStringConverter : MarkupExtension, IValueConverter
    {
        public override object ProvideValue(IServiceProvider serviceProvider) => this;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var ts = (TimeSpan)value;
            return (ts.Hours > 0 ? ts.Hours + ":" : "") +
                (ts.Seconds > 0 || ts.Minutes > 0 ? ts.Minutes.ToString().PadLeft(ts.Hours > 0 ? 2 : 1, '0') + ":" : "") +
                ts.Seconds.ToString("D2");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
    }
}
