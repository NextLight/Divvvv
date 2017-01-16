using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using static Divvvv.HdsDump;

namespace Divvvv
{
    class User
    {
        public Dictionary<string, string> ShowsDictionary { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private string _connId;

        public async Task Connect()
        {
            _connId = Json.Value(await HttpDownloader.GetStringAsync("http://www.vvvvid.it/user/login"), "conn_id");
            SyncShowsDictionary();
        }

        public void SyncShowsDictionary()
        {
            Parallel.For('a', 'z' + 1, async c =>
            {
                string json = await HttpDownloader.GetStringAsync($"http://www.vvvvid.it/vvvvid/ondemand/anime/channel/10003/last?filter={c}&conn_id=" + _connId);
                if (json.Contains("\"data\""))
                {
                    string s;
                    while ((s = await HttpDownloader.GetStringAsync($"http://www.vvvvid.it/vvvvid/ondemand/anime/channel/10003?filter={c}&conn_id=" + _connId)).Contains("\"data\""))
                        json += s;
                    foreach (Json j in json.Split("},{"))
                        ShowsDictionary[j["title"].Unescape()] = j["show_id"];
                }
            });
        }

        // TODO: wait for syncing to finish
        public List<string> SearchShow(string text)
        {
            var m = ShowsDictionary.Keys.Where(s => s.ToLower().Contains(text)).ToList();
            m.Sort((s1, s2) =>
            {
                int c = s1.ToLower().IndexOf(text).CompareTo(s2.ToLower().IndexOf(text));
                return c == 0 ? s1.CompareTo(s2) : c;
            });
            return m;
        }

        public async Task<Show> GetShow(string showId)
        {
            Serie[] series = (await HttpDownloader.GetStringAsync($"http://www.vvvvid.it/vvvvid/ondemand/{showId}/seasons/?conn_id=" + _connId))
                .ReMatchesGroups("\"season_id\":(.+?),.*?\"name\":\"(.+?)\"").Select(g => new Serie { Name = g[2], Id = g[1] }).ToArray();
            var show = new Show(showId, series, _connId);
            await show.FetchEpisodesAsync();
            return show; 
        }
    }

    class Serie { public string Name, Id; public IEnumerable<Episode> Episodes; }

    class Show
    {
        private readonly string _connId, _showId;

        public Show(string showId, Serie[] series, string connId)
        {
            _showId = showId;
            Series = series;
            _connId = connId;
        }

        public string ShowTitle { get; private set; } = null;
        public Serie[] Series { get; }

        public async Task FetchEpisodesAsync()
        {
            foreach (var s in Series)
                s.Episodes = await DownloadSerieEpisodesAsync(s.Id);
        }

        private async Task<IEnumerable<Episode>> DownloadSerieEpisodesAsync(string serieId)
        {
            Json js = await HttpDownloader.GetStringAsync($"http://www.vvvvid.it/vvvvid/ondemand/{_showId}/season/{serieId}?conn_id=" + _connId);
            if (ShowTitle == null)
                ShowTitle = js["show_title"];
            return js.ToString().Split("},{").Select(s => new Json(s)).Select(j =>
                new Episode(
                    ShowTitle,
                    j["embed_info"].Replace("master.m3u8", "manifest.f4m").Replace("/i/", "/z/"),
                    j["number"],
                    j["title"].Unescape().Replace('\n', ' '),
                    j["thumbnail"]
                )
            );
        }
    }

    public class Episode : INotifyPropertyChanged
    {
        public Episode(string showTitle, string manifestLink, string epNumber, string epTitle, string thumbLink)
        {
            ShowTitle = showTitle;
            EpNumber = epNumber;
            EpTitle = epTitle;
            Thumb = thumbLink == "" ? new BitmapImage() : new BitmapImage(new Uri("http://" + thumbLink.ReMatch(@"\/\/(.+)")));
            manifestLink = DecodeManifestLink(manifestLink);
            manifestLink = manifestLink.Contains("akamaihd") ? manifestLink + "?hdcore=3.6.0" : $"http://wowzaondemand.top-ix.org/videomg/_definst_/mp4:{manifestLink}/manifest.f4m";
            FileName = string.Format("{0}\\{0} {1} - {2}.flv", SanitizeFileName(ShowTitle), EpNumber, SanitizeFileName(EpTitle));
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
        public DownloadStatus DownloadStatus { get { return _downloadStatus; } private set { _downloadStatus = value; OnPropertyChanged(); } }
        public string ShowTitle { get; }
        public string EpNumber { get; }
        public string EpTitle { get; }
        public string FileName { get; }
        public BitmapImage Thumb { get; }
        private int _percentage;
        public int Percentage { get { return _percentage; } private set { _percentage = value; OnPropertyChanged(); } }
        private TimeSpan _downloadedTS;
        public TimeSpan DownloadedTS { get { return _downloadedTS; } private set { _downloadedTS = value; OnPropertyChanged(); } }

        public async Task Download()
        {
            if (DownloadStatus == DownloadStatus.Downloading)
                _hds.Stop();
            else
                await _hds.Start();
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

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}