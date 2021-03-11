using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;
using static Divvvv.HdsDump;

namespace Divvvv
{
    public class Episode : NotifyPropertyChanged, IDisposable
    {
        public Episode(string showTitle, string manifestLink, string number, string title, string thumbLink)
        {
            ShowTitle = showTitle;
            Number = number;
            Title = title;
            Thumb = thumbLink == "" ? new BitmapImage() : new BitmapImage(new Uri("https://" + thumbLink.ReMatch(@"\/\/(.+)")));
            manifestLink = DecodeManifestLink(manifestLink);
            manifestLink = manifestLink.Contains("akamaihd") ? manifestLink + "?hdcore=3.6.0" : $"https://wowzaondemand.top-ix.org/videomg/_definst_/mp4:{manifestLink}/manifest.f4m";
            FileName = string.Format("{0}\\{0} {1} - {2}.flv", SanitizeFileName(ShowTitle), Number, SanitizeFileName(Title));
            _hds = new HdsDump(manifestLink, FileName);
            _hds.DownloadedFragment += Hds_DownloadedFragment;
            _hds.DownloadStatusChanged += Hds_DownloadStatusChanged;
            DownloadStatus = _hds.Status;
            if (_hds.Status == DownloadStatus.Paused)
            {
                var progress = _hds.GetProgressFromFile();
                DownloadedTS = TimeSpan.FromMilliseconds(progress.Item1);
                Percentage = progress.Item1 * 100 / progress.Item2;
            }
        }

        private HdsDump _hds;

        private DownloadStatus _downloadStatus;
        public DownloadStatus DownloadStatus { get => _downloadStatus; private set { _downloadStatus = value; OnPropertyChanged(); } }
        public string ShowTitle { get; }
        public string Number { get; }
        public string Title { get; }
        public string FileName { get; }
        public BitmapImage Thumb { get; }
        private int _percentage;
        public int Percentage { get => _percentage; private set { _percentage = value; OnPropertyChanged(); } }
        private TimeSpan _downloadedTS;
        public TimeSpan DownloadedTS { get => _downloadedTS; private set { _downloadedTS = value; OnPropertyChanged(); } }

        public void ToggleDownload()
        {
            if (DownloadStatus == DownloadStatus.Downloading)
                _hds.Stop();
            else
                _hds.Start().DoNotAwait();
        }

        private void Hds_DownloadedFragment(object sender, EventArgs e)
        {
            DownloadedTS = _hds.LastDownloadedFragment.TimestampEnd;
            Percentage = (int)_hds.LastDownloadedFragment.Id * 100 / _hds.FragmentsCount;
        }

        private void Hds_DownloadStatusChanged(object sender, EventArgs e)
        {
            DownloadStatus = _hds.Status;
        }

        private string SanitizeFileName(string fileName) => string.Join("", fileName.Split(Path.GetInvalidFileNameChars()));

        private string DecodeManifestLink(string h)
        {
            // TODO: they might change this, find a smart way to retrieve it from vvvvid.js
            var g = "MNOPIJKL89+/4567UVWXQRSTEFGHABCDcdefYZabstuvopqr0123wxyzklmnghij";
            var m = h.Select(c => g.IndexOf(c)).ToArray();
            for (var i = m.Length * 2 - 1; i >= 0; i--)
                m[i % m.Length] ^= m[(i + 1) % m.Length];
            var sb = new StringBuilder(m.Length * 3 / 4);
            for (int i = 0; i < m.Length; i++)
                if (i % 4 != 0)
                    sb.Append((char)((m[i - 1] << (i % 4) * 2 & 255) + (m[i] >> (3 - i % 4) * 2)));
            if (m.Length % 4 == 1)
                sb.Append((char)(m.Last() << 2));
            return sb.ToString();
        }

        public void Dispose() => _hds.Dispose();
    }
}
