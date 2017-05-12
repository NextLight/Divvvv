using System;
using System.Threading;
using System.Threading.Tasks;

namespace Divvvv
{
    public class HdsDump
    {
        private readonly string _manifestUrl;
        private readonly FlvWriter _flv;
        private Media _media;
        private Segment _segment;
        private bool _stop = true;
        private CancellationTokenSource _cts;
        public HdsDump(string manifest, string fileName)
        {
            _manifestUrl = manifest;
            _flv = new FlvWriter(fileName);
            if (_flv.Resuming)
                Status = Math.Abs(_flv.Duration - _flv.GetLastTagTimestamp()) < 1000 ? DownloadStatus.Downloaded : DownloadStatus.Paused;
        }

        public enum DownloadStatus { Nope, Paused, Downloading, Downloaded }

        private DownloadStatus _status;
        public DownloadStatus Status { get => _status; private set { _status = value; DownloadStatusChanged?.Invoke(this, null); } }

        public Fragment LastDownloadedFragment { get; private set; } = null;

        public int FragmentsCount { get; private set; }

        public event EventHandler DownloadedFragment;
        public event EventHandler DownloadStatusChanged;

        private Task _writeTask = Task.Run(() => 0);
        public async Task Start()
        {
            if (Status == DownloadStatus.Downloading || Status == DownloadStatus.Downloaded)
                return;
            Status = DownloadStatus.Downloading;
            _stop = false;
            _cts = new CancellationTokenSource();
            if (!_initialized)
                await Init();

            uint fragId = (LastDownloadedFragment?.Id ?? 0) + 1;
            if (fragId > 1)
                DownloadedFragment?.Invoke(this, null);
            
            for (; fragId <= _segment.FragsCount && !_stop; fragId++)
            {
                string url = $"/{_media.BaseUrl}/{_media.MediaUrl}Seg{_segment.Id}-Frag{fragId}";
                byte[] fragBytes = await HttpDownloader.GetBytesAsync(_media.Domain, url, _cts.Token);
                if (!_stop)
                {
                    await _writeTask;
                    uint currentId = fragId;
                    _writeTask = Task.Run(async () =>
                    {
                        if (await _flv.WriteFragmentAsync(fragBytes))
                        {
#if DEBUG
                            System.Diagnostics.Debug.Print(currentId + "/" + _segment.FragsCount);
#endif
                            LastDownloadedFragment = _segment.Fragments[currentId];
                            DownloadedFragment?.Invoke(this, null);
                        }
#if DEBUG
                        else
                            System.Diagnostics.Debug.Print("nope " + currentId);
#endif
                    });
                }
            }
            Status = fragId == FragmentsCount + 1 ? DownloadStatus.Downloaded : DownloadStatus.Paused;
            if (Status == DownloadStatus.Downloaded)
                Close();
        }

        public void Stop()
        {
            _stop = true;
            _cts?.Cancel();
        }

        public void Close()
        {
            _flv.Close();
        }
        
        public Tuple<int, int> GetProgressFromFile() => new Tuple<int, int>(_flv.GetLastTagTimestamp(), _flv.Duration);

        private bool _initialized = false;
        private async Task Init()
        {
            _media = await F4m.GetMediaFromManifestUrl(_manifestUrl);
            _segment = F4m.GetSegmentFromBootstrapInfo(_media.BootstrapInfo);
            FragmentsCount = (int)_segment.FragsCount;
            if (!_flv.IsOpen)
                await _flv.Create(_media.Metadata);
            if (_flv.Resuming)
                LastDownloadedFragment = _segment.GetFragmentFromTimestamp(_flv.GetLastTagTimestamp());
            _initialized = true;
        }
    }
}