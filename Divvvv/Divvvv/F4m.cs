using System;
using System.Linq;
using System.Threading.Tasks;

namespace Divvvv
{
    static class F4m
    {
        public static async Task<Media> GetMediaFromManifestUrl(string manifestUrl)
        {
            XmlNode xmlManifest = await Web.DownloadStringAsync(manifestUrl);
            // pick media with higher bitrate (i.e. better quality)
            XmlNode xmlMedia = xmlManifest.Nodes.Where(x => x.Tag == "media").MaxBy(x => int.Parse(x["bitrate"]));
            return new Media
            {
                BaseUrl = manifestUrl.Substring(0, manifestUrl.LastIndexOf('/')),
                MediaUrl = xmlMedia["url"],
                BootstrapInfo = Convert.FromBase64String(xmlManifest.Nodes.First(x => x.Tag == "bootstrapInfo" && x["id"] == xmlMedia["bootstrapInfoId"]).InnerString),
                Metadata = Convert.FromBase64String(xmlMedia.Nodes.First().InnerString)
            };
        }

        public static Segment GetSegmentFromBootstrapInfo(byte[] bootstrapInfo)
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
            Segment segment = new Segment { Id = 1 };
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
                    ).OrderBy(fr => fr.Id).ToArray();
            }
            return segment;
        }
    }

    class Media
    {
        public string BaseUrl { get; set; }
        public string MediaUrl { get; set; }
        public byte[] BootstrapInfo { get; set; }
        public byte[] Metadata { get; set; }
    }

    class FragmentRun : IComparable
    {
        public uint Id { get; set; }
        public ulong Timestamp { get; set; }
        public uint Duration { get; set; }

        public int CompareTo(object obj)
        {
            return Timestamp.CompareTo(((FragmentRun)obj).Timestamp);
        }
    }

    class Segment
    {
        public uint Id { get; set; }
        public FragmentRun[] FragmentRunTable { get; set; }
    }

}
