using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Divvvv
{
    public partial class MainWindow : Window
    {
        private readonly User user;
        public MainWindow()
        {
            InitializeComponent();
            txtTitle.Focus();
            user = new User();
        }

        private async void button_Click(object sender, RoutedEventArgs e)
        {
            txtTitle.CaretIndex = txtTitle.Text.Length;
            string match = txtTitle.Text.ReMatch(@"https?:\/\/(.*?)\/?$");
            string showId, serieId = null;
            if (!string.IsNullOrEmpty(match))
            {
                string[] urlSplit = match.Split('/');
                if (urlSplit.Length < 4 || !match.Contains("vvvvid") || !urlSplit[2].IsInt())
                    return;
                showId = urlSplit[2];
                if (urlSplit.Length > 6 && urlSplit[4].IsInt())
                    serieId = urlSplit[4];
            }
            else
            {
                if (!user.ShowsDictionary.ContainsKey(txtTitle.Text))
                    return;
                showId = user.ShowsDictionary[txtTitle.Text];
            }
            var show = await user.GetShow(showId, serieId);
            lstEpisodes.ItemsSource = show.Episodes;
            lblTitle.Content = show.ShowTitle;
            txtTitle.Focus();
        }

        private void btnDownload_Click(object sender, RoutedEventArgs e) =>
            ((Episode) ((Button) sender).DataContext).Download();

        private void txtTitle_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = txtTitle.Text.Trim().ToLower();
            if (text.StartsWith("http") && text.Contains("vvvvid"))
            {
                lstHint.Visibility = Visibility.Hidden;
            }
            else
            {
                var m = user.SearchShow(text);
                lstHint.ItemsSource = m;
                lstHint.Visibility = m.Any() ? Visibility.Visible : Visibility.Hidden;
            }
        }

        private void LstHintSelection()
        {
            if (lstHint.ItemsSource.Cast<object>().Count() == 1)
                lstHint.SelectedIndex = 0;
            if (lstHint.SelectedIndex == -1)
                return;
            txtTitle.Text = (string) lstHint.SelectedItem;
            lstHint.Visibility = Visibility.Hidden;
        }

        private void LstHint_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            LstHintSelection();
        }

        private void LstHint_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                LstHintSelection();
            else if (e.Key != Key.Up && e.Key != Key.Down)
                txtTitle.Focus();
        }
        
        private void txtTitle_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up:
                case Key.Down:
                    lstHint.Focus();
                    break;
                case Key.Enter:
                    LstHintSelection();
                    break;
                case Key.Escape:
                    lstHint.Visibility = Visibility.Hidden;
                    break;
            }
        }
    }
}
