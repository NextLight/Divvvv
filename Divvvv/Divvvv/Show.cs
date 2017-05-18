using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Divvvv
{
    class Show
    {
        public Show(string id) => Id = id;

        public string Id { get; }
        public string Title { get; private set; }
        public Serie[] Series { get; private set; }

        public async Task FetchSeriesAsync(string connId)
        {
            Series = await Task.WhenAll(await HttpDownloader.GetStringAsync($"http://www.vvvvid.it/vvvvid/ondemand/{Id}/seasons/?conn_id={connId}")
                    .ContinueWith(t =>
                        t.Result.ReMatchesGroups("\"season_id\":(.+?),.*?\"name\":\"(.+?)\"")
                        .Select(async g => new Serie(g[1], g[2], await FetchSerieEpisodesAsync(g[1], connId))))
                ).ContinueWith(t => t.Result.ToArray());
        }

        private async Task<IEnumerable<Episode>> FetchSerieEpisodesAsync(string serieId, string connId)
        {
            var j = new Json(await HttpDownloader.GetStringAsync($"http://www.vvvvid.it/vvvvid/ondemand/{Id}/season/{serieId}?conn_id={connId}"));
            var eps = j.GetList<Dictionary<string, object>>("data");
            if (Title == null && eps.Any())
                Title = eps.First()["show_title"].ToString();
            return eps.Select(d =>
                new Episode(
                    Title,
                    d["embed_info"].ToString().Replace("master.m3u8", "manifest.f4m").Replace("/i/", "/z/"),
                    d["number"].ToString(),
                    d["title"].ToString().Unescape().Replace('\n', ' '),
                    d["thumbnail"].ToString()
                )
            );
        }
    }

    struct Serie
    {
        public Serie(string id, string name, IEnumerable<Episode> episodes)
        {
            Id = id;
            Name = name;
            Episodes = episodes.ToArray();
        }
        public string Id { get; set; }
        public string Name { get; set; }
        public Episode[] Episodes { get; set; }
    }
}