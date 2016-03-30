using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

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

        private void btnDownload_Click(object sender, RoutedEventArgs e) =>
            ((Episode)((Button)sender).DataContext).Download();
    }
}
