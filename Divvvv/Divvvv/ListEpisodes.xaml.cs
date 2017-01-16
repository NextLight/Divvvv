using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;

namespace Divvvv
{
    public partial class ListEpisodes : ListBox
    {
        public ListEpisodes()
        {
            InitializeComponent();
        }

        public ListEpisodes(IEnumerable<Episode> episodes) : this()
        {
            ItemsSource = episodes;
        }

        private async void btnDownload_Click(object sender, RoutedEventArgs e)
        {
            var ep = (Episode)((Button)sender).DataContext;
            if (ep.DownloadStatus == HdsDump.DownloadStatus.Downloaded)
                System.Diagnostics.Process.Start(ep.FileName);
            else
                await ep.Download();
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
