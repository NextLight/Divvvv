using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Divvvv
{
    static class F4m
    {
        public static async Task<Media> GetMediaFromManifestUrl(string manifestUrl)
        {
            XmlNode xmlManifest = await HttpDownloader.GetStringAsync(manifestUrl);
            // pick media with higher bitrate (i.e. better quality)
            XmlNode xmlMedia = xmlManifest.Nodes.Where(x => x.Tag == "media").MaxBy(x => int.Parse(x["bitrate"]));
            var ulrMatches = manifestUrl.ReMatchGroups(@"(^https?://[a-zA-Z-0-9\-\.]+?/)(.+)/");
            return new Media
            {
                Domain = ulrMatches[1],
                BaseUrl = ulrMatches[2],
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
                var frags = new List<Fragment> { new Fragment(0, 0, 0) };
                uint fragRuns = bootstrap.ReadUInt32();
                for (uint __ = 0; __ < fragRuns; __++)
                {
                    var readFrag = new Fragment(bootstrap.ReadUInt32(), bootstrap.ReadUInt64(), bootstrap.ReadUInt32());
                    Fragment last = frags.Last();
                    // hopefully they are already sorted by id
                    for (uint i = last.Id + 1; i < readFrag.Id; i++)
                        frags.Add(last = new Fragment(i, last.TimestampEnd.TotalMilliseconds, last.Duration));
                    if (readFrag.Id != 0)
                        frags.Add(readFrag);
                }
                segment.Fragments = frags.ToArray();
            }
            return segment;
        }
    }

    class Media
    {
        public string Domain { get; internal set; }
        public string BaseUrl { get; set; }
        public string MediaUrl { get; set; }
        public byte[] BootstrapInfo { get; set; }
        public byte[] Metadata { get; set; }
    }

    public class Fragment : IComparable
    {
        public uint Id { get; }
        public TimeSpan TimestampEnd { get; }
        public uint Duration { get; }

        public Fragment(uint id, TimeSpan timestampStart, uint duration)
        {
            Id = id;
            TimestampEnd = timestampStart + TimeSpan.FromMilliseconds(duration);
            Duration = duration;
        }

        public Fragment(uint id, double timestamp, uint duration)
            : this(id, TimeSpan.FromMilliseconds(timestamp), duration)
        {
        }

        public int CompareTo(object obj)
        {
            if (obj is TimeSpan)
                return TimestampEnd.CompareTo((TimeSpan) obj);
            throw new Exception();
        }
    }

    class Segment
    {
        public uint Id { get; set; }
        public Fragment[] Fragments { get; set; }
        public TimeSpan TotalTimestamp => Fragments.Last().TimestampEnd;
        public uint FragsCount => Fragments.Last().Id;

        public Fragment GetFragmentFromTimestamp(TimeSpan ts)
        {
            int bs = Array.BinarySearch(Fragments, ts);
            if (bs < 0)
                bs = ~bs - 1;
            return Fragments[bs];
        }

        public Fragment GetFragmentFromTimestamp(long ms) => 
            GetFragmentFromTimestamp(TimeSpan.FromMilliseconds(ms));
    }

}
