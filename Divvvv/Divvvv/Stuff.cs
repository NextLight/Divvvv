using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Divvvv
{
    public static class Extensions
    {
        public static TSource MaxBy<TSource>(this IEnumerable<TSource> source, Func<TSource, IComparable> selector)
        {
            TSource max = default(TSource);
            foreach (TSource i in source)
                if (selector(i).CompareTo(max) > 0)
                    max = i;
            return max;
        }

        public static bool IsInt(this string s)
        {
            int n;
            return int.TryParse(s, out n);
        }

        public static string[] Split(this string s, string separator) => s.Split(new[] {separator}, StringSplitOptions.None);

        public static string Unescape(this string s) => Regex.Unescape(s);

        public static string ReMatch(this string s, string re, int groupIdx = 1) => Regex.Match(s, re).Groups[groupIdx].Value;

        public static string[] ReMatchGroups(this string s, string re) => Regex.Match(s, re).Groups.Cast<Group>().Select(g => g.Value).ToArray();
        
        public static IEnumerable<string> ReMatches(this string s, string re, int groupIdx = 1) =>
            Regex.Matches(s, re).Cast<Match>().Select(m => m.Groups[groupIdx].Value);

        public static IEnumerable<string[]> ReMatchesGroups(this string s, string re) =>
            Regex.Matches(s, re).Cast<Match>().Select(m => m.Groups.Cast<Group>().Select(g => g.Value).ToArray());
    }

    public class Json
    {
        private readonly string _json;
        public Json(string s)
        {
            _json = s;
        }

        public string this[string key] => Value(key);

        public string Value(string key) => Value(_json, key);

        public static string Value(string json, string key) => json.ReMatch($"\"{key}\":" + "(\")?(.*?)(?(1)\")[,}]", 2);

        public static implicit operator Json(string s) => new Json(s);

        public override string ToString() => _json;
    }

    // just because
    public class XmlNode
    {
        private readonly string _xml;
        private string _innerString = null;
        public XmlNode(string s)
        {
            _xml = s;
            Tag = s.ReMatch("<([^? ]+?)[ >]");
        }

        public string Tag { get; }

        public string InnerString => 
            _innerString ?? (_innerString = _xml.ReMatch($@"<{Tag}.*?>\s*([\s\S]*?)\s*</{Tag}>"));

        public IEnumerable<XmlNode> Nodes => 
            InnerString.ReMatches(@"<([^? ]+).*?>[\s\S]*?</\1>", 0).Select(s => new XmlNode(s));  

        public string this[string attribute] => _xml.ReMatch($"<{Tag}.*?{attribute}=\"(.*?)\".*?>");

        public static implicit operator XmlNode(string s) => new XmlNode(s);
    }
}