using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Divvvv
{
    class User
    {
        public Dictionary<string, string> ShowsDictionary { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly string _connId;
        public User()
        {
            _connId = Json.Value(Web.DownloadString("http://www.vvvvid.it/user/login"), "conn_id");
            SyncShowsDictionary();
        }

        public void SyncShowsDictionary()
        {
            Parallel.For('a', 'z' + 1, async c =>
            {
                string json = await Web.DownloadStringAsync($"http://www.vvvvid.it/vvvvid/ondemand/anime/channel/10003/last?filter={c}&conn_id=" + _connId);
                if (json.Contains("\"data\""))
                {
                    string s;
                    while ((s = await Web.DownloadStringAsync($"http://www.vvvvid.it/vvvvid/ondemand/anime/channel/10003?filter={c}&conn_id=" + _connId)).Contains("\"data\""))
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
            Serie[] series = (await Web.DownloadStringAsync($"http://www.vvvvid.it/vvvvid/ondemand/{showId}/seasons/?conn_id=" + _connId))
                .ReMatchesGroups("\"season_id\":(.+?),.*?\"name\":\"(.+?)\"").Select(g => new Serie { Name = g[2], Id = g[1] }).ToArray();
            return new Show(showId, series, _connId);
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
            GetSeriesEpisodes();
        }

        public string ShowTitle { get; private set; } = null;
        public Serie[] Series { get; }

        private void GetSeriesEpisodes()
        {
            foreach (var s in Series)
                s.Episodes = DownloadSerieEpisodes(s.Id);
        }

        private IEnumerable<Episode> DownloadSerieEpisodes(string serieId)
        {
            Json js = Web.DownloadString($"http://www.vvvvid.it/vvvvid/ondemand/{_showId}/season/{serieId}?conn_id=" + _connId);
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
        private readonly string _manifestLink;

        public Episode(string showTitle, string manifestLink, string epNumber, string epTitle, string thumbLink)
        {
            ShowTitle = showTitle;
            EpNumber = epNumber;
            EpTitle = epTitle;
            Thumb = thumbLink == "" ? new BitmapImage() : new BitmapImage(new Uri("http://" + thumbLink.ReMatch(@"\/\/(.+)")));
            _manifestLink = manifestLink.Contains("akamaihd") ? manifestLink + "?hdcore=3.6.0" : $"http://wowzaondemand.top-ix.org/videomg/_definst_/mp4:{manifestLink}/manifest.f4m";

        }

        private bool _isDownloading;
        public bool IsDownloading { get { return _isDownloading; } private set { _isDownloading = value; OnPropertyChanged(); } }
        public string ShowTitle { get; }
        public string EpNumber { get; }
        public string EpTitle { get; }
        public BitmapImage Thumb { get; }
        private int _percentage;
        public int Percentage { get { return _percentage; } private set { _percentage = value; OnPropertyChanged(); } }
        private TimeSpan _timeRemaining;
        public TimeSpan TimeRemaining { get { return _timeRemaining; } private set { _timeRemaining = value; OnPropertyChanged(); } }

        private HdsDump _hds;

        public async void Download()
        {
            IsDownloading = !IsDownloading;
            if (IsDownloading)
            {
                if (!Directory.Exists(ShowTitle))
                    Directory.CreateDirectory(ShowTitle);
                string fileName = string.Format("{0}\\{0} {1} - {2}.flv", SanitizeFileName(ShowTitle), EpNumber, SanitizeFileName(EpTitle));
                _hds = new HdsDump(_manifestLink, fileName);
                //_hds.DownloadedFragment += Program_DownloadedFragment;
                _hds.DownloadedFile += Program_DownloadedFile;
                await _hds.Start();
            }
            else
            {
                //_hds.DownloadedFragment -= Program_DownloadedFragment;
                _hds.Close();
            }
        }

        private void Program_DownloadedFragment(object sender, EventArgs e)//DownloadedFragmentArgs e)
        {
            //Percentage = e.Downloaded * 100 / e.FragmentsCount;
            //TimeRemaining = e.TimeRemaining;
        }

        private void Program_DownloadedFile(object sender, EventArgs e)
        {
            _hds.DownloadedFile -= Program_DownloadedFile;
            Percentage = 0;
            TimeRemaining = new TimeSpan();
            IsDownloading = false;
        }

        private string SanitizeFileName(string fileName) => string.Join("", fileName.Split(Path.GetInvalidFileNameChars()));

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}