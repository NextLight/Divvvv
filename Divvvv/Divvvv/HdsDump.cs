using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Divvvv
{
    class HdsDump
    {
        private readonly string _manifestUrl;
        private readonly FlvWriter _flv;
        private readonly WebClient _web;
        private bool _downloaded = false;
        private bool _close = false;
        public HdsDump(string manifest, string fileName)
        {
            _manifestUrl = manifest;
            _flv = new FlvWriter(fileName);
            if (_flv.Resuming)
                _downloaded = Math.Abs(_flv.Duration - _flv.LastTimestamp) < 1000;
            _web = new WebClient();
        }


        public bool Resuming => _flv.Resuming;

        public bool Downloaded => _downloaded;

        public event EventHandler DownloadedFile;


        public async Task Start()
        {
            Media media = await F4m.GetMediaFromManifestUrl(_manifestUrl);
            if (!_flv.Resuming)
                await _flv.Create(media.Metadata);
            Segment segment = F4m.GetSegmentFromBootstrapInfo(media.BootstrapInfo);
            int fragId = 1;
            if (_flv.Resuming)
            {
                int bs = segment.FragmentRunTable.ToList().BinarySearch(new FragmentRun { Timestamp = (ulong)_flv.LastTimestamp });
                if (bs < 0)
                    bs = ~bs - 1;
                FragmentRun fr = segment.FragmentRunTable[bs];
                fragId = (int)fr.Id + (_flv.LastTimestamp - (int)fr.Timestamp) / (int)fr.Duration;
            }
            for (; fragId <= segment.FragmentRunTable.Last().Id && !_close; fragId++)
            {
                Console.Write(fragId + "/" + segment.FragmentRunTable.Last().Id);
                string url = $"{media.BaseUrl}/{media.MediaUrl}Seg{segment.Id}-Frag{fragId}";
                byte[] frag = await _web.DownloadDataTaskAsync(url); // TODO: better async
                if (!_close)
                    await _flv.WriteFragmentAsync(frag);
            }
            _downloaded = !_close;
            _flv.Close();
            if (!_close)
                DownloadedFile?.Invoke(this, null);
        }

        public void Close()
        {
            _close = true;
            _flv.Close();
        }
    }
}