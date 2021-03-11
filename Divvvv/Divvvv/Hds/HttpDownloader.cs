using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Divvvv
{
    static class HttpDownloader
    {
        private static readonly HttpClient _client = new HttpClient(new HttpClientHandler { UseCookies = true });

        static HttpDownloader()
        {
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/89.0.4389.82 Safari/537.36");
        }

        public static async Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default)
        {
            using (var r = await _client.GetAsync(url, ct))
                return await r.Content.ReadAsByteArrayAsync();
        }

        public static async Task<string> GetStringAsync(string url, CancellationToken ct = default)
        {
            using (var r = await _client.GetAsync(url, ct))
                return await r.Content.ReadAsStringAsync();
        }
    }
}
