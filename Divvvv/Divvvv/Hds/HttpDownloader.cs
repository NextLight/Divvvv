using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Divvvv
{
    static class HttpDownloader
    {
        private readonly static Dictionary<string, HttpClient> _clients = new Dictionary<string, HttpClient>();

        private static async Task<HttpContent> GetContentAsync(string baseUrl, string requestUrl, CancellationToken ct)
        {
            HttpResponseMessage response = null;
            try
            {
                HttpClient client;
                if (!_clients.ContainsKey(baseUrl))
                {
                    client = new HttpClient { BaseAddress = new Uri(baseUrl) };
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; CrOS x86_64 7428.0.2015) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/46.0.2483.0 Safari/537.36");
                    _clients.Add(baseUrl, client);
                }
                else
                    client = _clients[baseUrl];
                response = await client.GetAsync(requestUrl, ct);
                return response.Content;
            }
            catch
            {
                response?.Dispose();
                return null;
            }
        }

        public static async Task<byte[]> GetBytesAsync(string baseUrl, string requestUrl, CancellationToken ct = default(CancellationToken))
        {
            HttpContent content = await GetContentAsync(baseUrl, requestUrl, ct);
            if (content == null)
                return null;
            byte[] res = await content.ReadAsByteArrayAsync();
            content.Dispose();
            return res;
        }

        public static Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default(CancellationToken))
        {
            int slash = url.IndexOf('/', 8);
            return GetBytesAsync(url.Substring(0, slash), url.Substring(slash), ct);
        }

        public static async Task<string> GetStringAsync(string baseUrl, string requestUrl, CancellationToken ct = default(CancellationToken))
        {
            HttpContent content = await GetContentAsync(baseUrl, requestUrl, ct);
            if (content == null)
                return null;
            string res = await content.ReadAsStringAsync();
            content.Dispose();
            return res;
        }

        public static Task<string> GetStringAsync(string url, CancellationToken ct = default(CancellationToken))
        {
            int slash = url.IndexOf('/', 8);
            return GetStringAsync(url.Substring(0, slash), url.Substring(slash), ct);
        }
    }
}
