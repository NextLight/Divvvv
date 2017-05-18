using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Divvvv
{
    class User
    {
        public Dictionary<string, string> ShowsDictionary { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler AddedShows;

        private string _connId;
        private readonly object _showsListLock = new object();

        public User()
        {
            ConnectAsync().DoNotAwait();
        }

        private async Task ConnectAsync()
        {
            _connId = await GetNewConnIdAsync();
            SyncShowsDictionaryAsync().DoNotAwait();
        }

        private static async Task<string> GetNewConnIdAsync() => Json.GetStringRE(await HttpDownloader.GetStringAsync("http://www.vvvvid.it/user/login"), "conn_id");

        private async Task SyncShowsDictionaryAsync()
        {
            await Task.WhenAll(Enumerable.Range('a', 'z' + 1 - 'a').Select(c =>
                Task.Run(async () => {
                    string connId = await GetNewConnIdAsync(); // I need to get a new conn_id for each letter because of the way 10003/last => 10003 works
                    string json = await HttpDownloader.GetStringAsync($"http://www.vvvvid.it/vvvvid/ondemand/anime/channel/10003/last?filter={c}&conn_id={connId}");
                    while (json?.Contains("\"data\"") == true)
                    {
                        foreach (Dictionary<string, object> d in new Json(json).GetList("data"))
                            ShowsDictionary[d["title"].ToString().Unescape()] = d["show_id"].ToString();
                        AddedShows?.Invoke(this, null);
                        json = await HttpDownloader.GetStringAsync($"http://www.vvvvid.it/vvvvid/ondemand/anime/channel/10003?filter={c}&conn_id={connId}");
                    }
                })));
        }

        public List<string> SearchShow(string text)
        {
            text = text.ToLower();
            var m = ShowsDictionary.Keys.Where(s => s != null && s.ToLower().Contains(text)).ToList();
            m.Sort((s1, s2) =>
            {
                int c = s1.ToLower().IndexOf(text).CompareTo(s2.ToLower().IndexOf(text));
                return c == 0 ? s1.CompareTo(s2) : c;
            });
            return m;
        }

        public async Task<Show> GetShow(string showId)
        {
            var show = new Show(showId);
            await show.FetchSeriesAsync(_connId);
            return show; 
        }
    }
}