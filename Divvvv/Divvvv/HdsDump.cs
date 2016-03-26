using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Divvvv
{
    class HdsDump
    {
        private readonly string _manifestUrl;
        private readonly FileStream _fs;
        private bool _resume = false;
        public HdsDump(string manifest, string fileName)
        {
            _manifestUrl = manifest;
            var fi = new FileInfo(fileName);
            if (fi.Exists && fi.Length > 10)
                _resume = true;
            _fs = new FileStream(fileName, FileMode.OpenOrCreate);
        }

        public async Task Start()
        {
            Media media = await GetMediaFromManifest();
            Segment segment = GetSegmentFromBootstrapInfo(media.BootstrapInfo);
            _fs.Seek(0, SeekOrigin.End);
            await _fs.WriteAsync(media.Metadata, 0, media.Metadata.Length);
        }

        private async Task<Media> GetMediaFromManifest()
        {
            XmlNode xmlManifest = await Stuff.GetHtmlPageAsync(_manifestUrl);
            // pick media with higher bitrate (i.e. better quality)
            XmlNode xmlMedia = xmlManifest.Nodes.Where(x => x.Tag == "media").MaxBy(x => int.Parse(x["bitrate"]));
            return new Media
            {
                BaseUrl = _manifestUrl.Substring(_manifestUrl.LastIndexOf('/') + 1),
                MediaUrl = xmlMedia["url"],
                Duration = xmlManifest.Nodes.FirstOrDefault(x => x.Tag == "duration")?.InnerString.ParseOrNull<float>() ?? 0,
                BootstrapInfo = Convert.FromBase64String(xmlManifest.Nodes.First(x => x.Tag == "bootstrapInfo" && x["id"] == xmlMedia["bootstrapInfoId"]).InnerString),
                Metadata = Convert.FromBase64String(xmlMedia.Nodes.First().InnerString)
            };
        }

        private Segment GetSegmentFromBootstrapInfo(byte[] bootstrapInfo)
        {
            // parse bootstrap info box (abst)
            var bootstrap = new BoxReader(bootstrapInfo);
            // skip useless stuff
            bootstrap.SkipBoxHeader();
            bootstrap.Skip(1 + 3 + 4 + 1 + 4 + 8 + 8);
            bootstrap.SkipString();
            byte serverEntryCount = bootstrap.ReadByte();
            for (byte _ = 0; _ < serverEntryCount; _++)
                bootstrap.SkipString();
            byte qualityEntryCount = bootstrap.ReadByte();
            for (byte _ = 0; _ < qualityEntryCount; _++)
                bootstrap.SkipString();
            bootstrap.SkipString();
            bootstrap.SkipString();

            // get and parse (skip) segment run table (asrt) boxes
            // MEH: there COULD be more than 1 asrt box, ignoring them for now
            byte segmentRunTableCount = bootstrap.ReadByte();
            for (byte _ = 0; _ < segmentRunTableCount; _++)
                bootstrap.SkipBox();

            // get and parse fragment run table (afrt) boxes (MEH: keep only the last one)
            Segment segment = new Segment {Id = 1};
            byte fragmentRunTableCount = bootstrap.ReadByte();
            for (byte _ = 0; _ < fragmentRunTableCount; _++)
            {
                bootstrap.SkipBoxHeader();
                bootstrap.Skip(1 + 3 + 4);
                byte qualitySegmentUrlModifiers = bootstrap.ReadByte(); // hopefully this is 0 or 1
                for (byte __ = 0; __ < qualitySegmentUrlModifiers; __++)
                    bootstrap.SkipString();
                // this isn't really a segment, but whatever.
                segment.FragmentRunTable = new FragmentRun[bootstrap.ReadUInt32()].Select(fr =>
                    new FragmentRun
                    {
                        Id = bootstrap.ReadUInt32(),
                        Timestamp = bootstrap.ReadUInt64(),
                        Duration = bootstrap.ReadUInt32()
                    }
                    ).ToArray();
            }
            return segment;
        }
    }

    class Media
    {
        public string BaseUrl { get; set; }
        public string MediaUrl { get; set; }
        public float Duration { get; set; }
        public byte[] BootstrapInfo { get; set; }
        public byte[] Metadata { get; set; }
    }

    class FragmentRun
    {
        public uint Id { get; set; }
        public ulong Timestamp { get; set; }
        public uint Duration { get; set; }
    }

    class Segment
    {
        public uint Id { get; set; }
        public FragmentRun[] FragmentRunTable { get; set; }
    }
}