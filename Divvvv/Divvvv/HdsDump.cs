using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Xml;
using Microsoft.Win32.SafeHandles;

namespace HDS
{
    internal static class Constants
    {
        public const int AUDIO = 0x08;
        public const int VIDEO = 0x09;
        public const int SCRIPT_DATA = 0x12;
        public const int FRAME_TYPE_INFO = 0x05;
        public const int CODEC_ID_AVC = 0x07;
        public const int CODEC_ID_AAC = 0x0A;
        public const int AVC_SEQUENCE_HEADER = 0x00;
        public const int AAC_SEQUENCE_HEADER = 0x00;
        public const int AVC_NALU = 0x01;
        public const int AVC_SEQUENCE_END = 0x02;
        public const int FRAMEFIX_STEP = 0x28;
        public const int STOP_PROCESSING = 0x02;
        public const int INVALID_TIMESTAMP = -1;
        public const int TIMECODE_DURATION = 8;
    }

    internal class DownloadedFragmentArgs : EventArgs
    {
        public int Downloaded { get; }
        public int FragmentsCount { get; }
        public TimeSpan CurrentTimestamp { get; }
        public TimeSpan TimeRemaining { get; }
        public DownloadedFragmentArgs(int downloaded, int fragCount, TimeSpan currentTimestamp, TimeSpan timeRemaining)
        {
            Downloaded = downloaded;
            FragmentsCount = fragCount;
            CurrentTimestamp = currentTimestamp;
            TimeRemaining = timeRemaining;
        }
    }

    internal class HdsDump
    {
        public static bool debug = false;
        public static string logfile = "STDOUT";
        public static int threads = 1;
        public static bool fproxy = false;
        private F4F f4f;

        public event EventHandler<DownloadedFragmentArgs> DownloadedFragment;
        public event EventHandler<EventArgs> DownloadedFile;

        public HdsDump(string manifest, string outFile, bool restore)
        {
            if (HTTP.Referer == "")
                RegExMatch(@"^(.*?://.*?/)", manifest, out HTTP.Referer);
            
            if (manifest == "")
                Quit("<c:Red>Please specify the manifest.</c>");

            f4f = new F4F(manifest, outFile);
            if (restore)
                f4f.CheckLastTSExistingFile();
        }

        public void Start()
        {
            foreach (var f in f4f.DownloadFragments())
                DownloadedFragment?.Invoke(this, f);
            f4f.Close();
            DownloadedFile?.Invoke(null, new EventArgs());
        }

        public void Close()
        {
            f4f.Exit = true;
        }
        
        public static void Quit(string msg)
        {
            Console.WriteLine(msg);
            MessageBox.Show(Regex.Replace(msg, @"(<c:\w+>|</c>)", ""));
        }

        public static void ShowHeader(string header)
        {
            string h = Regex.Replace(header, @"(<c:\w+>|</c>)", "");
            int width = Console.WindowWidth/2 + h.Length/2;

            Message(string.Format("\n{0," + width + "}\n", header));
        }
        
        public static void Message(string msg)
        {
            //Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.Gray;
            List<ConsoleColor> colorsStack = new List<ConsoleColor>();
            string[] chars = msg.Split('<');
            string spChar = "";
            foreach (string s in chars)
            {
                string sText = s;
                Match m = Regex.Match(s, @"^c:(\w+)>");
                if (m.Success)
                {
                    sText = s.Replace(m.Groups[0].Value, "");
                    try
                    {
                        colorsStack.Add(Console.ForegroundColor);
                        Console.ForegroundColor = (ConsoleColor) Enum.Parse(typeof (ConsoleColor), m.Groups[1].Value);
                    }
                    catch
                    {
                    }
                }
                else if (Regex.IsMatch(s, @"^/c>"))
                {
                    if (colorsStack.Count > 0)
                    {
                        Console.ForegroundColor = colorsStack[colorsStack.Count - 1];
                        colorsStack.RemoveAt(colorsStack.Count - 1);
                    }
                    else
                        Console.ResetColor();
                    sText = s.Substring(3);
                }
                else sText = spChar + sText;
                Console.Write(sText);
                spChar = "<";
            }
            if (!msg.EndsWith("\r")) Console.Write("\n\r");
            Console.ResetColor();
        }

        public static void DebugLog(string msg)
        {
            if (!debug)
                return;
            if (logfile == "STDERR")
                Console.Error.WriteLine(msg);
            else if (logfile == "STDOUT")
                Console.WriteLine(msg);
            else
                File.AppendAllText(logfile, msg + "\n");
        }

        public static bool RegExMatch(string RegX, string wherelook, out string resultValue)
        {
            Match m = Regex.Match(wherelook, @RegX, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            resultValue = m.Groups[1].Value;
            return m.Groups[1].Success;
        }

        public static bool RegExMatch3(string RegX, string wherelook, out string resultValue1, out string resultValue2,
            out string resultValue3)
        {
            Match m = Regex.Match(wherelook, @RegX, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            resultValue1 = m.Groups[1].Value;
            resultValue2 = m.Groups[2].Value;
            resultValue3 = m.Groups[3].Value;
            return m.Groups[1].Success && m.Groups[2].Success && m.Groups[3].Success;
        }
        
        public static string StripHtml(string source)
        {
            string output;

            //get rid of HTML tags
            output = Regex.Replace(source, "<[^>]*>", string.Empty);

            //get rid of multiple blank lines
            output = Regex.Replace(output, @"^\s*$\n", string.Empty, RegexOptions.Multiline);

            return output;
        }
    }
    
    internal static class UriHacks
    {
        // System.UriSyntaxFlags is internal, so let's duplicate the flag privately
        private const int UnEscapeDotsAndSlashes = 0x2000000;

        public static void LeaveDotsAndSlashesEscaped(Uri uri)
        {
            if (uri == null) throw new ArgumentNullException("uri");
            FieldInfo fieldInfo = uri.GetType().GetField("m_Syntax", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fieldInfo != null)
            {
                object uriParser = fieldInfo.GetValue(uri);
                fieldInfo = typeof (UriParser).GetField("m_Flags", BindingFlags.Instance | BindingFlags.NonPublic);
                if (fieldInfo != null)
                {
                    object uriSyntaxFlags = fieldInfo.GetValue(uriParser);
                    // Clear the flag that we don't want
                    uriSyntaxFlags = (int) uriSyntaxFlags & ~UnEscapeDotsAndSlashes;
                    fieldInfo.SetValue(uriParser, uriSyntaxFlags);
                }
            }
        }
    }

    internal class HTTP
    {
        public static string Useragent = "Mozilla/5.0 (Windows NT 5.1; rv:20.0) Gecko/20100101 Firefox/20.0";
        public static string Referer = "";
        public static string Cookies = "";
        public static string Proxy = "";
        public static string ProxyUsername = "";
        public static string ProxyPassword = "";
        public static string Username = "";
        public static string Password = "";
        public static bool POST;
        private readonly int bufferLenght = 1048576;
        public WebHeaderCollection Headers = new WebHeaderCollection();
        public bool notUseProxy;
        public byte[] ResponseData;
        public string Status = "";
        public string Url = "";

        public HTTP(bool lnotUseProxy = false)
        {
            notUseProxy = lnotUseProxy;
        }

        public string responseText
        {
            get { return Encoding.ASCII.GetString(ResponseData); }
        }

        public int get(string sUrl)
        {
            int RetCode = 0;
            Url = sUrl;
            ResponseData = new byte[0];
            if (!sUrl.StartsWith("http"))
            {
                // if not http url - try load as file
                if (File.Exists(sUrl))
                {
                    ResponseData = File.ReadAllBytes(sUrl);
                    RetCode = 200;
                }
                else
                {
                    Status = "File not found.";
                    RetCode = 404;
                }
                return RetCode;
            }
            HttpWebRequest request = CreateRequest();
            string postData = "";
            if (POST)
            {
                int questPos = sUrl.IndexOf('?');
                if (questPos > 0)
                {
                    Url = sUrl.Substring(0, questPos);
                    postData = sUrl.Substring(questPos + 1);
                }
            }
            string s = Cookies.Trim();
            if (s != "" && s.Substring(s.Length - 1, 1) != ";")
                Cookies = s + "; ";
            if (POST)
            {
                StreamWriter sw = new StreamWriter(request.GetRequestStream());
                sw.Write(postData);
                sw.Close();
            }
            HttpWebResponse response = HttpWebResponseExt.GetResponseNoException(request);
            Status = response.StatusDescription;
            RetCode = (int) response.StatusCode;
            if (response.Headers.Get("Set-cookie") != null)
            {
                HdsDump.RegExMatch("^(.*?);", response.Headers.Get("Set-cookie"), out s);
                Cookies += s + "; ";
            }
            Stream dataStream = response.GetResponseStream();
            if (response.ContentEncoding.ToLower().Contains("gzip"))
                dataStream = new GZipStream(dataStream, CompressionMode.Decompress);
            else if (response.ContentEncoding.ToLower().Contains("deflate"))
                dataStream = new DeflateStream(dataStream, CompressionMode.Decompress);

            byte[] buffer = new byte[bufferLenght];
            using (MemoryStream ms = new MemoryStream())
            {
                int readBytes;
                while ((readBytes = dataStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, readBytes);
                }
                ResponseData = ms.ToArray();
            }
            dataStream.Close();
            response.Close();
            return RetCode;
        }

        private HttpWebRequest CreateRequest()
        {
            Uri myUri = new Uri(Url);
            UriHacks.LeaveDotsAndSlashesEscaped(myUri);
            HttpWebRequest request = (HttpWebRequest) WebRequest.Create(myUri);
            if (Useragent != "") request.UserAgent = Useragent;
            if (Referer != "") request.Referer = Referer;
            if (Username != "") request.Credentials = new NetworkCredential(Username, Password);
            if ((Proxy != "") && !notUseProxy)
            {
                if (!Proxy.StartsWith("http")) Proxy = "http://" + Proxy;
                WebProxy myProxy = new WebProxy();
                myProxy.Address = new Uri(Proxy);
                if (ProxyUsername != "")
                    myProxy.Credentials = new NetworkCredential(ProxyUsername, ProxyPassword);
                request.Proxy = myProxy;
            }
            if (POST) request.Method = "POST";
            else request.Method = "GET";
            request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
            request.Headers.Add("Accept-Language: en-us,en;q=0.5");
            request.Headers.Add("Accept-Encoding: gzip,deflate");
            request.Headers.Add("Accept-Charset: ISO-8859-1,utf-8;q=0.7,*;q=0.7");
            request.KeepAlive = true;
            request.Headers.Add("Keep-Alive: 900");
            foreach (string key in Headers.AllKeys)
            {
                request.Headers.Set(key, Headers[key]);
            }
            if (Cookies != "") request.Headers.Add("Cookie: " + Cookies);
            request.ContentType = "application/x-www-form-urlencoded";
            return request;
        }
    }

    internal class F4F
    {
        public const int INVALID_HANDLE_VALUE = -1;
        public const uint OPEN_EXISTING = 3;
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private readonly List<string> _qualityEntryTable = new List<string>();
        private readonly List<string> _qualitySegmentUrlModifiers = new List<string>();
        private readonly List<string> _serverEntryTable = new List<string>();
        public bool AAC_HeaderWritten;
        public bool audio;
        public string auth = "";
        public bool AVC_HeaderWritten;
        public long baseTS;
        public string baseUrl = "";
        public string bootstrapUrl = "";
        private int currentDuration;
        private long currentFilesize;
        public long currentTS;
        public int discontinuity;
        public int duration;
        public int fileCount = 1;
        public int filesize;
        public int fixWindow = 1000;
        private bool FLVContinue;
        private bool FLVHeaderWritten;
        public string format = " {0,-8}{1,-16}{2,-16}{3,-8}";
        public int fragCount;

        private Fragment2Dwnld[] Fragments2Download;
        private int fragmentsComplete;
        public int fragNum;
        public int fragsPerSeg;
        private int fragStart = -1;
        private readonly List<Fragment> fragTable = new List<Fragment>();
        public string fragUrl = "";
        public string fragUrlTemplate = "<FRAGURL>Seg<SEGNUM>-Frag<FRAGNUM>";
        public long fromTimestamp = -1;
        public int lastFrag;
        public bool live;

        public int manifesttype = 0; // 0 - hds, 1 - xml playlist, 2 - m3u playlist, 3 - json manifest with template
        private readonly Dictionary<string, Media> media = new Dictionary<string, Media>();
        public bool metadata = true;
        public long negTS;
        private XmlNamespaceManager nsMgr;
        public long pAudioTagLen;
        public long pAudioTagPos;
        public SafeFileHandle pipeHandle;
        public FileStream pipeStream;
        public BinaryWriter pipeWriter;
        public bool play;
        public bool prevAAC_Header;
        public long prevAudioTS = -1;
        public bool prevAVC_Header;
        public int prevTagSize = 4;
        public long prevVideoTS = -1;
        public long pVideoTagLen;
        public long pVideoTagPos;
        public string quality = "high";
        public int segNum = 1;
        private int segStart = -1;
        private readonly List<Segment> segTable = new List<Segment>();
        private Media selectedMedia;
        public int start;
        public int tagHeaderLen = 11;
        public int threads = 1;
        private int threadsRun;
        public bool usePipe;
        public bool video;
        public bool Exit = false;
        private string outFile;
        private string manifestUrl;

        public F4F(string manifestUrl, string outFile)
        {
            this.manifestUrl = manifestUrl;
            this.outFile = outFile;
            InitDecoder();
        }

        [DllImport("Kernel32.dll", SetLastError = true)]
        public static extern int SetStdHandle(int device, IntPtr handle);

        ~F4F()
        {
            Close();
        }

        public void Close()
        {
            pipeWriter?.Close();
            pipeStream?.Close();
            pipeHandle?.Close();
        }

        private void InitDecoder()
        {
            baseTS = FLVContinue ? 0 : Constants.INVALID_TIMESTAMP;
            audio = false;
            negTS = Constants.INVALID_TIMESTAMP;
            video = false;
            prevTagSize = 4;
            tagHeaderLen = 11;
            prevAudioTS = Constants.INVALID_TIMESTAMP;
            prevVideoTS = Constants.INVALID_TIMESTAMP;
            pAudioTagLen = 0;
            pVideoTagLen = 0;
            pAudioTagPos = 0;
            pVideoTagPos = 0;
            prevAVC_Header = false;
            prevAAC_Header = false;
            AVC_HeaderWritten = false;
            AAC_HeaderWritten = false;
        }

        public static string NormalizePath(string path)
        {
            string[] inSegs = Regex.Split(path, @"(?<!\/)\/(?!\/)");
            List<string> outSegs = new List<string>();
            foreach (string seg in inSegs)
            {
                if (seg == "" || seg == ".")
                    continue;
                if (seg == "..")
                    outSegs.RemoveAt(outSegs.Count - 1);
                else
                    outSegs.Add(seg);
            }
            string outPath = string.Join("/", outSegs.ToArray());
            if (path.StartsWith("/")) outPath = "/" + outPath;
            if (path.EndsWith("/")) outPath += "/";
            return outPath;
        }

        private static long ReadInt(int size, ref byte[] bytesData, long pos)
        {
            long res = 0;
            int n = size / 8;
            for (byte i = 0; i < n; i++)
                res |= bytesData[pos + i] << (n - 1 - i) * 8;
            return res;
        }

        private static byte ReadByte(ref byte[] bytesData, long pos) => bytesData[pos];

        private static uint ReadInt24(ref byte[] bytesData, long pos) => (uint)ReadInt(24, ref bytesData, pos);

        private static uint ReadInt32(ref byte[] bytesData, long pos) => (uint)ReadInt(32, ref bytesData, pos);

        private static long ReadInt64(ref byte[] bytesData, long pos) => ReadInt(64, ref bytesData, pos);

        private static string GetString(XmlNode xmlObject) => xmlObject.InnerText.Trim();

        private static bool IsHttpUrl(string url) => (url.Length > 4) && (url.ToLower().Substring(0, 4) == "http");

        private static bool IsRtmpUrl(string url) => Regex.IsMatch(url, @"^rtm(p|pe|pt|pte|ps|pts|fp):", RegexOptions.IgnoreCase);

        private static void ReadBoxHeader(ref byte[] bytesData, ref long pos, ref string boxType, ref long boxSize)
        {
            boxSize = ReadInt32(ref bytesData, pos);
            boxType = ReadStringBytes(ref bytesData, (int)pos + 4, 4);
            if (boxSize == 1)
            {
                boxSize = ReadInt64(ref bytesData, pos + 8) - 16;
                pos += 16;
            }
            else
            {
                boxSize -= 8;
                pos += 8;
            }
        }

        private static string ReadStringBytes(ref byte[] bytesData, int pos, int len) => string.Join("", bytesData.Skip(pos).Take(len).Select(b => (char)b));

        private static string ReadString(ref byte[] bytesData, ref long pos)
        {
            var res = string.Join("", bytesData.Skip((int) pos).TakeWhile(b => b != 0).Select(b => (char)b));
            pos += res.Length + 1;
            return res;
        }

        private static void WriteByte(ref byte[] bytesData, long pos, byte byteValue) => bytesData[pos] = byteValue;

        private static void WriteInt24(ref byte[] bytesData, long pos, long intValue)
        {
            bytesData[pos + 0] = (byte) ((intValue & 0xFF0000) >> 16);
            bytesData[pos + 1] = (byte) ((intValue & 0xFF00) >> 8);
            bytesData[pos + 2] = (byte) (intValue & 0xFF);
        }

        private static void WriteInt32(ref byte[] bytesData, long pos, long intValue)
        {
            bytesData[pos + 0] = (byte) ((intValue & 0xFF000000) >> 24);
            bytesData[pos + 1] = (byte) ((intValue & 0xFF0000) >> 16);
            bytesData[pos + 2] = (byte) ((intValue & 0xFF00) >> 8);
            bytesData[pos + 3] = (byte) (intValue & 0xFF);
        }

        private static void WriteBoxSize(ref byte[] bytesData, long pos, string type, long size)
        {
            string realtype = Encoding.ASCII.GetString(bytesData, (int) pos - 4, 4);
            if (realtype == type)
            {
                WriteInt32(ref bytesData, pos - 8, size);
            }
            else
            {
                WriteInt32(ref bytesData, pos - 8, 0);
                WriteInt32(ref bytesData, pos - 4, size);
            }
        }

        private static void ByteBlockCopy(ref byte[] bytesData1, long pos1, ref byte[] bytesData2, long pos2, long len)
        {
            int len1 = bytesData1.Length;
            int len2 = bytesData2.Length;
            for (int i = 0; i < len; i++)
            {
                if ((pos1 >= len1) || (pos2 >= len2)) break;
                bytesData1[pos1++] = bytesData2[pos2++];
            }
        }

        private static string GetNodeProperty(XmlNode node, string propertyName, string defaultvalue = "")
        {
            foreach (string name in propertyName.Split('|'))
                foreach (XmlAttribute attr in node.Attributes)
                    if (name.ToLower() == attr.Name.ToLower())
                        return attr.Value;
            return defaultvalue;
        }

        private string ExtractBaseUrl(string dataUrl)
        {
            string baseUrl = dataUrl;
            if (this.baseUrl != "")
                baseUrl = this.baseUrl;
            else
            {
                if (baseUrl.IndexOf("?") > 0)
                    baseUrl = baseUrl.Substring(0, baseUrl.IndexOf("?"));
                int i = baseUrl.LastIndexOf("/");
                if (i >= 0)
                    baseUrl = baseUrl.Substring(0, baseUrl.LastIndexOf("/"));
                else
                    baseUrl = "";
            }
            return baseUrl;
        }

        private void WriteFlvTimestamp(ref byte[] frag, long fragPos, long packetTS)
        {
            WriteInt24(ref frag, fragPos + 4, packetTS & 0x00FFFFFF);
            WriteByte(ref frag, fragPos + 7, (byte) ((packetTS & 0xFF000000) >> 24));
        }

        private int FindFragmentInTabe(int needle)
        {
            return fragTable.FindIndex(m => m.firstFragment == needle);
        }

        private void CheckRequestRerutnCode(int statusCode, string statusMsg)
        {
            switch (statusCode)
            {
                case 403:
                    HdsDump.Quit("<c:Red>ACCESS DENIED! Unable to download manifest. (Request status: <c:Magenta>" + statusMsg + "</c>)");
                    break;

                case 404:
                    HdsDump.Quit("<c:Red>Manifest file not found! (Request status: <c:Magenta>" + statusMsg + "</c>)");
                    break;

                default:
                    if (statusCode != 200)
                        HdsDump.Quit("<c:Red>Unable to download manifest (Request status: <c:Magenta>" + statusMsg + "</c>)");
                    break;
            }
        }

        private static bool AttrExist(XmlNode node, string name) => 
            node != null && GetNodeProperty(node, name, "<no>") != "<no>";

        private XmlElement GetManifest(ref string manifestUrl)
        {
            string sDomain = "";
            HTTP cc = new HTTP();
            int statusCode = cc.get(manifestUrl);
            CheckRequestRerutnCode(statusCode, cc.Status);

            if (HdsDump.RegExMatch(@"<r>\s*?<to>(.*?)</to>", cc.responseText, out sDomain))
            {
                if (HdsDump.RegExMatch(@"^.*?://.*?/.*?/(.*)", manifestUrl, out manifestUrl))
                {
                    manifestUrl = sDomain + manifestUrl;
                    statusCode = cc.get(manifestUrl);
                    CheckRequestRerutnCode(statusCode, cc.Status);
                }
            }

            string xmlText = cc.responseText;
            if (xmlText.IndexOf("</") < 0)
                HdsDump.Quit("<c:Red>Error loading manifest: <c:Green>" + manifestUrl);
            XmlDocument xmldoc = new XmlDocument();
            try
            {
                xmldoc.LoadXml(xmlText);
            }
            catch
            {
                if (Regex.IsMatch(xmlText, @"<html.*?<body", RegexOptions.Singleline))
                {
                    HdsDump.Quit("<c:Red>Error loading manifest. Url redirected to html page. Check the manifest url.");
                }
                else
                {
                    HdsDump.Quit("<c:Red>Error loading manifest. It's no valid xml file.");
                }
            }
            nsMgr = new XmlNamespaceManager(xmldoc.NameTable);
            nsMgr.AddNamespace("ns", xmldoc.DocumentElement.NamespaceURI);
            return xmldoc.DocumentElement;
        }

        // Get manifest and parse - extract medias info and select quality
        private void ParseManifest(string manifestUrl)
        {

            string defaultQuality = "";

            HdsDump.Message("Processing manifest info....");
            XmlElement xml = GetManifest(ref manifestUrl);

            XmlNode node = xml.SelectSingleNode("/ns:manifest/ns:baseURL", nsMgr);
            string baseUrl = node?.InnerText?.Trim() ?? ExtractBaseUrl(manifestUrl);

            if (baseUrl == "" && !IsHttpUrl(manifestUrl))
                HdsDump.Quit("<c:Red>Not found <c:Magenta>baseURL</c> value in manifest or in parameter <c:White>--urlbase</c>.");

            XmlNodeList nodes = xml.SelectNodes("/ns:manifest/ns:media[@*]", nsMgr);
            Dictionary<string, Manifest> manifests = new Dictionary<string, Manifest>();
            int countBitrate = 0;
            bool readChildManifests = nodes.Count > 0 ? AttrExist(nodes[0], "href") : false;
            if (readChildManifests)
            {
                foreach (XmlNode ManifestNode in nodes)
                {
                    if (!AttrExist(ManifestNode, "bitrate")) countBitrate++;
                    Manifest manifest = new Manifest();
                    manifest.bitrate = GetNodeProperty(ManifestNode, "bitrate", countBitrate.ToString());
                    manifest.url = NormalizePath(baseUrl + "/" + GetNodeProperty(ManifestNode, "href"));
                    manifest.xml = GetManifest(ref manifest.url);
                    manifests[manifest.bitrate] = manifest;
                }
            }
            else
            {
                Manifest manifest = new Manifest();
                manifest.bitrate = "0";
                manifest.url = manifestUrl;
                manifest.xml = xml;
                manifests[manifest.bitrate] = manifest;
                defaultQuality = manifest.bitrate;
            }
            countBitrate = 0;
            foreach (KeyValuePair<string, Manifest> pair in manifests)
            {
                Manifest manifest = pair.Value;
                string sBitrate = "";

                // Extract baseUrl from manifest url
                node = manifest.xml.SelectSingleNode("/ns:manifest/ns:baseURL", nsMgr);
                baseUrl = node?.InnerText?.Trim() ?? ExtractBaseUrl(manifest.url);

                XmlNodeList MediaNodes = manifest.xml.SelectNodes("/ns:manifest/ns:media", nsMgr);
                foreach (XmlNode stream in MediaNodes)
                {
                    if (AttrExist(stream, "bitrate"))
                        sBitrate = GetNodeProperty(stream, "bitrate");
                    else if (int.Parse(manifest.bitrate) > 0)
                        sBitrate = manifest.bitrate;
                    else
                        sBitrate = countBitrate++.ToString();

                    while (media.ContainsKey(sBitrate))
                        sBitrate = (int.Parse(sBitrate) + 1).ToString();

                    Media mediaEntry = new Media();
                    mediaEntry.baseUrl = baseUrl;
                    mediaEntry.url = GetNodeProperty(stream, "url");

                    if (IsRtmpUrl(mediaEntry.baseUrl) || IsRtmpUrl(mediaEntry.url))
                        HdsDump.Quit("<c:Red>Provided manifest is not a valid HDS manifest. (Media url is <c:Magenta>rtmp</c>?)");

                    if (AttrExist(stream, "bootstrapInfoId"))
                        node = manifest.xml.SelectSingleNode($"/ns:manifest/ns:bootstrapInfo[@id='{GetNodeProperty(stream, "bootstrapInfoId")}']", nsMgr);
                    else
                        node = manifest.xml.SelectSingleNode("/ns:manifest/ns:bootstrapInfo", nsMgr);
                    if (node != null)
                    {
                        if (AttrExist(node, "url"))
                        {
                            mediaEntry.bootstrapUrl = NormalizePath(mediaEntry.baseUrl + "/" + GetNodeProperty(node, "url"));
                            HTTP cc = new HTTP();
                            if (cc.get(mediaEntry.bootstrapUrl) != 200)
                                HdsDump.Quit("<c:Red>Failed to download bootstrap info. (Request status: <c:Magenta>" +
                                             cc.Status + "</c>)\n\r<c:DarkCyan>bootstrapUrl: <c:DarkRed>" + mediaEntry.bootstrapUrl);
                            mediaEntry.bootstrap = cc.ResponseData;
                        }
                        else
                            mediaEntry.bootstrap = Convert.FromBase64String(node.InnerText.Trim());
                    }

                    node = manifest.xml.SelectSingleNode($"/ns:manifest/ns:media[@url='{mediaEntry.url}']/ns:metadata", nsMgr);
                    mediaEntry.metadata = node != null ? Convert.FromBase64String(node.InnerText.Trim()) : null;
                    media[sBitrate] = mediaEntry;
                }
            }

            // Available qualities
            if (media.Count < 1)
                HdsDump.Quit("<c:Red>No media entry found");

            HdsDump.DebugLog("Manifest Entries:\n");
            HdsDump.DebugLog($" {"Bitrate",-8}{"URL"}");
            string sBitrates = " ";
            foreach (KeyValuePair<string, Media> pair in media)
            {
                sBitrates += pair.Key + " ";
                HdsDump.DebugLog($" {pair.Key,-8}{pair.Value.url}");
            }

            HdsDump.DebugLog("");
            // Sort quality keys - from high to low
            string[] keys = new string[media.Keys.Count];
            media.Keys.CopyTo(keys, 0);
            Array.Sort(keys, (a,b) =>
            {
                int x = 0;
                int y = 0;
                if (int.TryParse(a, out x) && int.TryParse(b, out y))
                    return x - y;
                return a.CompareTo(b);
            });
            string sQuality = defaultQuality;
            // Quality selection
            if (media.ContainsKey(quality))
            {
                sQuality = quality;
            }
            else
            {
                quality = quality.ToLower();
                switch (quality)
                {
                    case "low":
                        quality = keys[keys.Length - 1]; // last
                        break;
                    case "medium":
                        quality = keys[keys.Length/2];
                        break;
                    default:
                        quality = keys[0]; // first
                        break;
                }
                int iQuality = Convert.ToInt32(quality);
                while (iQuality >= 0)
                {
                    if (media.ContainsKey(iQuality.ToString()))
                        break;
                    iQuality--;
                }
                sQuality = iQuality.ToString();
            }
            selectedMedia = media[sQuality];
            int n = sBitrates.IndexOf(sQuality);
            sBitrates = sBitrates.Replace(" " + sQuality + " ", " <c:Cyan>" + sQuality + "</c> ");
            HdsDump.Message("Quality Selection:");
            HdsDump.Message("Available:" + sBitrates);
            HdsDump.Message("Selected : <c:Cyan>" + sQuality.PadLeft(n + sQuality.Length - 1));
            this.baseUrl = selectedMedia.baseUrl;
            if (!string.IsNullOrEmpty(selectedMedia.bootstrapUrl))
            {
                bootstrapUrl = selectedMedia.bootstrapUrl;
                UpdateBootstrapInfo(bootstrapUrl);
            }
            else
            {
                long pos = 0;
                long boxSize = 0;
                string boxType = "";
                ReadBoxHeader(ref selectedMedia.bootstrap, ref pos, ref boxType, ref boxSize);
                if (boxType == "abst")
                    ParseBootstrapBox(ref selectedMedia.bootstrap, pos);
                else
                    HdsDump.Quit("<c:Red>Failed to parse bootstrap info.");
            }

            if (fragsPerSeg == 0)
                fragsPerSeg = fragCount;
        }

        private void UpdateBootstrapInfo(string bootstrapUrl)
        {
            int fragNum = fragCount;
            int retries = 0;
            HTTP cc = new HTTP();
            cc.Headers.Add("Cache-Control: no-cache");
            cc.Headers.Add("Pragma: no-cache");
            while ((fragNum == fragCount) && (retries < 30))
            {
                long bootstrapPos = 0;
                long boxSize = 0;
                string boxType = "";
                HdsDump.DebugLog("Updating bootstrap info, Available fragments: " + fragCount);
                if (cc.get(bootstrapUrl) != 200)
                    HdsDump.Quit("<c:Red>Failed to refresh bootstrap info");
                ReadBoxHeader(ref cc.ResponseData, ref bootstrapPos, ref boxType, ref boxSize);
                if (boxType == "abst")
                    ParseBootstrapBox(ref cc.ResponseData, bootstrapPos);
                else
                    HdsDump.Quit("<c:Red>Failed to parse bootstrap info");

                HdsDump.DebugLog("Update complete, Available fragments: " + fragCount);
                if (fragNum == fragCount)
                {
                    retries++;
                    HdsDump.Message($"{"<c:DarkCyan>Updating bootstrap info, Retries: " + retries,-79}\r");
                    Thread.Sleep(2000); // 2 sec
                }
            }
        }

        private void ParseBootstrapBox(ref byte[] bootstrapInfo, long pos)
        {

            byte version = ReadByte(ref bootstrapInfo, pos);
            int flags = (int) ReadInt24(ref bootstrapInfo, pos + 1);
            int bootstrapVersion = (int) ReadInt32(ref bootstrapInfo, pos + 4);
            byte b = ReadByte(ref bootstrapInfo, pos + 8);
            int profile = (b & 0xC0) >> 6;
            int update = (b & 0x10) >> 4;
            if ((b & 0x20) >> 5 > 0)
            {
                live = true;
                this.metadata = false;
            }
            if (update == 0)
            {
                segTable.Clear();
                fragTable.Clear();
            }
            int timescale = (int) ReadInt32(ref bootstrapInfo, pos + 9);
            long currentMediaTime = ReadInt64(ref bootstrapInfo, 13);
            long smpteTimeCodeOffset = ReadInt64(ref bootstrapInfo, 21);
            pos += 29;
            string movieIdentifier = ReadString(ref bootstrapInfo, ref pos);
            byte serverEntryCount = ReadByte(ref bootstrapInfo, pos++);
            for (int i = 0; i < serverEntryCount; i++)
                _serverEntryTable.Add(ReadString(ref bootstrapInfo, ref pos));
            byte qualityEntryCount = ReadByte(ref bootstrapInfo, pos++);
            for (int i = 0; i < qualityEntryCount; i++)
                _qualityEntryTable.Add(ReadString(ref bootstrapInfo, ref pos));
            string drmData = ReadString(ref bootstrapInfo, ref pos);
            string metadata = ReadString(ref bootstrapInfo, ref pos);
            byte segRunTableCount = ReadByte(ref bootstrapInfo, pos++);

            long boxSize = 0;
            string boxType = "";
            HdsDump.DebugLog("Segment Tables:");
            for (int i = 0; i < segRunTableCount; i++)
            {
                HdsDump.DebugLog($"\nTable {i + 1}:");
                ReadBoxHeader(ref bootstrapInfo, ref pos, ref boxType, ref boxSize);
                if (boxType == "asrt")
                    ParseAsrtBox(ref bootstrapInfo, pos);
                pos += boxSize;
            }
            byte fragRunTableCount = ReadByte(ref bootstrapInfo, pos++);
            HdsDump.DebugLog("Fragment Tables:");
            for (int i = 0; i < fragRunTableCount; i++)
            {
                HdsDump.DebugLog($"\nTable {i + 1}:");
                ReadBoxHeader(ref bootstrapInfo, ref pos, ref boxType, ref boxSize);
                if (boxType == "afrt")
                    ParseAfrtBox(ref bootstrapInfo, pos);
                pos += (int) boxSize;
            }
            ParseSegAndFragTable();

        }

        private void ParseAsrtBox(ref byte[] asrt, long pos)
        {

            byte version = ReadByte(ref asrt, pos);
            int flags = (int) ReadInt24(ref asrt, pos + 1);
            int qualityEntryCount = ReadByte(ref asrt, pos + 4);

            segTable.Clear();
            pos += 5;
            for (int i = 0; i < qualityEntryCount; i++)
            {
                _qualitySegmentUrlModifiers.Add(ReadString(ref asrt, ref pos));
            }
            int segCount = (int) ReadInt32(ref asrt, pos);
            pos += 4;
            HdsDump.DebugLog($"{"Segment Entries"}:\n\n {"Number",-8}{"Fragments",-10}");
            for (int i = 0; i < segCount; i++)
            {
                int firstSegment = (int) ReadInt32(ref asrt, pos);
                Segment segEntry = new Segment();
                segEntry.firstSegment = firstSegment;
                segEntry.fragmentsPerSegment = (int) ReadInt32(ref asrt, pos + 4);
                if ((segEntry.fragmentsPerSegment & 0x80000000) > 0)
                    segEntry.fragmentsPerSegment = 0;
                pos += 8;
                segTable.Add(segEntry);
                HdsDump.DebugLog($" {segEntry.firstSegment,-8}{segEntry.fragmentsPerSegment,-10}");
            }
            HdsDump.DebugLog("");
        }

        private void ParseAfrtBox(ref byte[] afrt, long pos)
        {
            fragTable.Clear();

            int version = ReadByte(ref afrt, pos);
            int flags = (int) ReadInt24(ref afrt, pos + 1);
            int timescale = (int) ReadInt32(ref afrt, pos + 4);
            int qualityEntryCount = ReadByte(ref afrt, pos + 8);

            pos += 9;
            for (int i = 0; i < qualityEntryCount; i++)
                _qualitySegmentUrlModifiers.Add(ReadString(ref afrt, ref pos));

            int fragEntries = (int) ReadInt32(ref afrt, pos);
            pos += 4;
            HdsDump.DebugLog($" {"Number",-8}{"Timestamp",-16}{"Duration",-16}{"Discontinuity",-16}");
            for (int i = 0; i < fragEntries; i++)
            {
                Fragment fragEntry = new Fragment
                {
                    firstFragment = (int)ReadInt32(ref afrt, pos),
                    firstFragmentTimestamp = ReadInt64(ref afrt, pos + 4),
                    fragmentDuration = (int) ReadInt32(ref afrt, pos + 12),
                    discontinuityIndicator = 0
                };
                pos += 16;
                if (fragEntry.fragmentDuration == 0)
                    fragEntry.discontinuityIndicator = ReadByte(ref afrt, pos++);
                fragTable.Add(fragEntry);
                HdsDump.DebugLog($"{fragEntry.firstFragment,-8}{fragEntry.firstFragmentTimestamp,-16}{fragEntry.fragmentDuration,-16}{fragEntry.discontinuityIndicator,-16}");

                if (fragEntry.fragmentDuration != 0 && fromTimestamp > fragEntry.firstFragmentTimestamp)
                    start = fragEntry.firstFragment + (int)((fromTimestamp - fragEntry.firstFragmentTimestamp) / fragEntry.fragmentDuration);
            }
            HdsDump.DebugLog("");
        }

        private void ParseSegAndFragTable()
        {
            if (segTable.Count == 0 || fragTable.Count == 0)
                return;
            Segment firstSegment = segTable[0];
            Segment lastSegment = segTable[segTable.Count - 1];
            Fragment firstFragment = fragTable[0];
            Fragment lastFragment = fragTable[fragTable.Count - 1];

            // Check if live stream is still live
            if (lastFragment.fragmentDuration == 0 && lastFragment.discontinuityIndicator == 0)
            {
                live = false;
                if (fragTable.Count > 0)
                    fragTable.RemoveAt(fragTable.Count - 1);
                if (fragTable.Count > 0)
                    lastFragment = fragTable[fragTable.Count - 1];
            }

            // Count total fragments by adding all entries in compactly coded segment table
            bool invalidFragCount = false;
            Segment prev = segTable[0];
            fragCount = prev.fragmentsPerSegment;
            for (int i = 0; i < segTable.Count; i++)
            {
                Segment current = segTable[i];
                fragCount += (current.firstSegment - prev.firstSegment - 1)*prev.fragmentsPerSegment;
                fragCount += current.fragmentsPerSegment;
                prev = current;
            }
            if ((fragCount & 0x80000000) == 0)
                fragCount += firstFragment.firstFragment - 1;
            if ((fragCount & 0x80000000) != 0)
            {
                fragCount = 0;
                invalidFragCount = true;
            }
            if (fragCount < lastFragment.firstFragment)
                fragCount = lastFragment.firstFragment;
            HdsDump.DebugLog("fragCount: " + fragCount);

            // Determine starting segment and fragment
            if (segStart < 0)
            {
                if (live)
                    segStart = lastSegment.firstSegment;
                else
                    segStart = firstSegment.firstSegment;
                if (segStart < 1)
                    segStart = 1;
            }
            if (fragStart < 0)
            {
                if (live && !invalidFragCount)
                    fragStart = fragCount - 2;
                else
                    fragStart = firstFragment.firstFragment - 1;
                if (fragStart < 0)
                    fragStart = 0;
            }
            HdsDump.DebugLog("segStart : " + segStart);
            HdsDump.DebugLog("fragStart: " + fragStart);
        }

        private void StartNewThread2DownloadFragment()
        {
            if (fragNum < 1) return;
            threadsRun++;
            for (int i = fragNum - 1; i < fragCount; i++)
            {
                if (fragmentsComplete - fragNum > 5) break;
                if (!Fragments2Download[i].running)
                {
                    Fragments2Download[i].running = true;
                    HTTP cc = new HTTP(!HdsDump.fproxy);
                    if (cc.get(Fragments2Download[i].url) != 200)
                    {
                        Fragments2Download[i].running = false;
                        Fragments2Download[i].ready = false;
                        HdsDump.DebugLog("Error download fragment " + (i + 1) + " in thread. Status: " + cc.Status);
                    }
                    else
                    {
                        Fragments2Download[i].data = cc.ResponseData;
                        Fragments2Download[i].ready = true;
                        fragmentsComplete++;
                    }
                    break;
                }
                ;
            }
            threadsRun--;
        }

        private void ThreadDownload()
        {
            while (fragmentsComplete < fragCount)
            {
                if (fragCount - fragmentsComplete < threads) threads = fragCount - fragmentsComplete;
                if (threadsRun < threads)
                {
                    Thread t = new Thread(StartNewThread2DownloadFragment) {IsBackground = true};
                    t.Start();
                }
                Thread.Sleep(300);
            }
        }

        public string GetFragmentUrl(int segNum, int fragNum)
        {
            string fragUrl = fragUrlTemplate;
            fragUrl = fragUrl.Replace("<FRAGURL>", this.fragUrl);
            fragUrl = fragUrl.Replace("<SEGNUM>", segNum.ToString());
            fragUrl = fragUrl.Replace("<FRAGNUM>", fragNum.ToString());
            return fragUrl + auth;
        }

        public int GetSegmentFromFragment(int fragN)
        {
            if (segTable.Count == 0 || fragTable.Count == 0)
                return 1;
            Segment firstSegment = segTable[0];
            Segment lastSegment = segTable[segTable.Count - 1];
            Fragment firstFragment = fragTable[0];
            Fragment lastFragment = fragTable[fragTable.Count - 1];

            if (segTable.Count == 1)
                return firstSegment.firstSegment;
            Segment seg, prev = firstSegment;
            int end, start = firstFragment.firstFragment;
            for (int i = firstSegment.firstSegment; i <= lastSegment.firstSegment; i++)
            {
                if (segTable.Count >= i - 1)
                    seg = segTable[i];
                else
                    seg = prev;
                end = start + seg.fragmentsPerSegment;
                if ((fragN >= start) && (fragN < end))
                    return i;
                prev = seg;
                start = end;
            }
            return lastSegment.firstSegment;
        }

        public void CheckLastTSExistingFile()
        {
            if (!File.Exists(outFile))
                return;
            using (FileStream fs = new FileStream(outFile, FileMode.Open))
            {
                if (fs.Length > 600)
                {
                    // WHY
                    //fs.Position = fs.Length - 4;
                    //fs.ReadByte();
                    fs.Seek(-3, SeekOrigin.End);
                    int blockLength = (fs.ReadByte() << 16) + (fs.ReadByte() << 8) + fs.ReadByte();
                    if (fs.Length - blockLength > 600)
                    {
                        fs.Position = fs.Length - blockLength;
                        fromTimestamp = (fs.ReadByte() << 16) + (fs.ReadByte() << 8) + fs.ReadByte();
                        FLVHeaderWritten = true;
                        FLVContinue = true;
                        HdsDump.DebugLog("Continue downloading with exiting file from timestamp: " + fromTimestamp);
                    }
                }
            }
        }
        
        public IEnumerable<DownloadedFragmentArgs> DownloadFragments()
        {
            HTTP cc = new HTTP(!HdsDump.fproxy);
            ParseManifest(manifestUrl);

            segNum = segStart;
            fragNum = fragStart;
            if (start > 0)
            {
                segNum = GetSegmentFromFragment(start);
                fragNum = start - 1;
                segStart = segNum;
                fragStart = fragNum;
            }
            string remaining = "";
            string sDuration = "";
            int downloaded = 0;
            filesize = 0;
            bool usedThreads = threads > 1;
            int retCode;
            byte[] fragmentData = new byte[0];
            lastFrag = fragNum;
            if (fragNum >= fragCount)
                HdsDump.Quit("<c:Red>Already downloaded.");

            fragUrl = IsHttpUrl(selectedMedia.url) ? selectedMedia.url : NormalizePath(baseUrl + "/" + selectedMedia.url);

            fragmentsComplete = fragNum;
            HdsDump.DebugLog("Downloading Fragments:");
            InitDecoder();
            DateTime startTime = DateTime.Now;
            if (usedThreads)
            {
                Fragments2Download = new Fragment2Dwnld[fragCount];
                int curSegNum, curFragNum;
                for (int i = 0; i < fragCount; i++)
                {
                    curFragNum = i + 1;
                    curSegNum = GetSegmentFromFragment(curFragNum);
                    Fragments2Download[i].url = GetFragmentUrl(curSegNum, i + 1);
                    Fragments2Download[i].ready = curFragNum < fragNum; // if start > 0 skip 
                    Fragments2Download[i].running = false;
                }
                Thread MainThread = new Thread(ThreadDownload);
                MainThread.IsBackground = true;
                MainThread.Start();
            }
            // --------------- MAIN LOOP DOWNLOADING FRAGMENTS ----------------
            int fragsToDownload = fragCount - fragNum;
            while (fragNum < fragCount && !Exit)
            {
                fragNum++;
                segNum = GetSegmentFromFragment(fragNum);
                
                int ts = (int) Math.Round(currentTS / 1000.0);
                sDuration = $"<c:DarkCyan>Current timestamp: </c>{ts/3600:00}:{ts/60%60:00}:{ts%60:00} ";
                
                TimeSpan timeRemaining = TimeSpan.FromTicks(DateTime.Now.Subtract(startTime).Ticks * (fragsToDownload - (downloaded + 1)) / (downloaded + 1));
                yield return new DownloadedFragmentArgs(fragNum, fragCount, TimeSpan.FromSeconds(ts), timeRemaining);

                remaining = $"<c:DarkCyan>Time remaining: </c>{timeRemaining.Hours:00}<c:Cyan>:</c>{timeRemaining.Minutes:00}<c:Cyan>:</c>{timeRemaining.Seconds:00}";
                HdsDump.Message($"{"Downloading <c:White>" + fragNum + "</c>/" + fragCount + " fragments",-46} {sDuration}{remaining}\r");
                int fragIndex = FindFragmentInTabe(fragNum);
                if (fragIndex >= 0)
                {
                    discontinuity = fragTable[fragIndex].discontinuityIndicator;
                }
                else
                {
                    // search closest
                    for (int i = 0; i < fragTable.Count; i++)
                    {
                        if (fragTable[i].firstFragment < fragNum) continue;
                        discontinuity = fragTable[i].discontinuityIndicator;
                        break;
                    }
                }
                if (discontinuity != 0)
                {
                    HdsDump.DebugLog("Skipping fragment " + fragNum + " due to discontinuity, Type: " + discontinuity);
                    continue;
                }

                if (usedThreads)
                {
                    // use threads
                    DateTime DataTimeOut = DateTime.Now.AddSeconds(200);
                    while (!Fragments2Download[fragNum - 1].ready)
                    {
                        Thread.Sleep(2000);
                        if (DateTime.Now > DataTimeOut) break;
                    }
                    if (!Fragments2Download[fragNum - 1].ready)
                    {
                        HdsDump.Quit("<c:Red>Timeout downloading fragment " + fragNum + " ".PadLeft(38));
                    }
                    fragmentData = Fragments2Download[fragNum - 1].data;
                    HdsDump.DebugLog("threads fragment loaded: " + Fragments2Download[fragNum - 1].url);
                }
                else
                {
                    HdsDump.DebugLog("Fragment Url: " + GetFragmentUrl(segNum, fragNum));
                    retCode = cc.get(GetFragmentUrl(segNum, fragNum));
                    if (retCode != 200)
                    {
                        if ((retCode == 403) && !string.IsNullOrEmpty(HTTP.Proxy) && !HdsDump.fproxy)
                        {
                            string msg = "<c:Red>Access denied for downloading fragment <c:White>" + fragNum +
                                         "</c>. (Request status: <c:Magenta>" + cc.Status + "</c>)";
                            msg += "\nTry switch <c:Green>--fproxy</c>.";
                            HdsDump.Quit(msg);
                        }
                        else
                            HdsDump.Quit("<c:Red>Failed to download fragment <c:White>" + fragNum +
                                         "</c>. (Request status: <c:Magenta>" + cc.Status + "</c>)");
                    }
                    else
                        fragmentData = cc.ResponseData;
                }

                WriteFragment(ref fragmentData, fragNum);

                downloaded++;
                HdsDump.DebugLog($"Downloaded: segment={segNum} fragment={fragNum}/{fragCount} lenght: {fragmentData.Length}");
                fragmentData = null;
                if (usedThreads)
                    Fragments2Download[fragNum - 1].data = null;
                if (duration > 0 && currentDuration >= duration || filesize > 0 && currentFilesize >= filesize) 
                    break;
            }
            sDuration = $"\n<c:DarkCyan>Downloaded duration: </c>{currentDuration/3600:00}:{currentDuration/60%60:00}:{currentDuration%60:00} ";
            HdsDump.Message(sDuration);
            HdsDump.DebugLog("\nAll fragments downloaded successfully.");
        }

        private static byte[] ConvertHexStringToByteArray(string hexString)
        {
            byte[] res = new byte[hexString.Length/2];
            for (int i = 0; i < res.Length; i++)
                res[i] = byte.Parse(hexString.Substring(i * 2, 2), NumberStyles.HexNumber);
            return res;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern SafeFileHandle CreateFile
        (
            string pipeName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplate
        );

        private void Write2File(ref byte[] data, FileMode fileMode = FileMode.Append, long pos = 0, long datalen = 0)
        {
            if (datalen == 0 || datalen > data.Length - pos)
                datalen = data.Length - pos;
            try
            {
                if (pipeWriter == null)
                {
                    pipeStream?.Close();
                    pipeHandle?.Close();
                    pipeStream = new FileStream(outFile, fileMode);
                    pipeWriter = new BinaryWriter(pipeStream);
                }
                pipeWriter.Write(data, (int) pos, (int) datalen);
                pipeWriter.Flush();

                currentFilesize += datalen;
            }
            catch (Exception e)
            {
                HdsDump.DebugLog("Error while writing to file! Message: " + e.Message);
                HdsDump.DebugLog("Exception: " + e);
                HdsDump.Quit("<c:Red>Error while writing to file! <c:DarkCyan>Message: <c:Magenta>" + e.Message);
            }
        }

        private void WriteFlvHeader(bool audio = true, bool video = true)
        {
            filesize = 0;
            byte[] flvHeader = ConvertHexStringToByteArray("464c5601050000000900000000");
            if (audio && !video)
                flvHeader[4] = 0x04;
            else if (video && !audio)
                flvHeader[4] = 0x01;

            Write2File(ref flvHeader, FileMode.Create);
            if (metadata)
                WriteMetadata();

            FLVHeaderWritten = true;
        }

        private void WriteMetadata()
        {
            if (selectedMedia.metadata?.Length > 0)
            {
                int mediaMetadataSize = selectedMedia.metadata.Length;
                byte[] metadata = new byte[tagHeaderLen + mediaMetadataSize + 4];
                WriteByte(ref metadata, 0, Constants.SCRIPT_DATA);
                WriteInt24(ref metadata, 1, mediaMetadataSize);
                WriteInt24(ref metadata, 4, 0);
                WriteInt32(ref metadata, 7, 0);
                ByteBlockCopy(ref metadata, tagHeaderLen, ref selectedMedia.metadata, 0, mediaMetadataSize);
                WriteByte(ref metadata, tagHeaderLen + mediaMetadataSize - 1, 0x09);
                WriteInt32(ref metadata, tagHeaderLen + mediaMetadataSize, tagHeaderLen + mediaMetadataSize);
                Write2File(ref metadata);
            }
        }

        private void WriteFragment(ref byte[] data, int fragNum)
        {
            if (data == null)
                return;
            if (!FLVHeaderWritten)
            {
                InitDecoder();
                DecodeFragment(ref data, true);
                WriteFlvHeader(audio, video);
                if (metadata)
                    WriteMetadata();
                InitDecoder();
            }
            DecodeFragment(ref data);
        }

        private bool VerifyFragment(ref byte[] frag)
        {
            string boxType = "";
            long boxSize = 0;
            long fragPos = 0;

            /* Some moronic servers add wrong boxSize in header causing fragment verification *
           * to fail so we have to fix the boxSize before processing the fragment.          */
            while (fragPos < frag.Length)
            {
                ReadBoxHeader(ref frag, ref fragPos, ref boxType, ref boxSize);
                if (boxType == "mdat")
                {
                    if (fragPos + boxSize > frag.Length)
                    {
                        boxSize = frag.Length - fragPos;
                        WriteBoxSize(ref frag, fragPos, boxType, boxSize);
                    }
                    return true;
                }
                fragPos += boxSize;
            }
            return false;
        }

        private void DecodeFragment(ref byte[] frag, bool notWrite = false)
        {
            if (frag == null)
                return;
            string boxType = "";
            long boxSize = 0;
            long fragLen = frag.Length;
            long fragPos = 0;
            long packetTS = 0;
            long lastTS = 0;
            long fixedTS = 0;
            int AAC_PacketType = 0;
            int AVC_PacketType = 0;

            if (!VerifyFragment(ref frag))
            {
                HdsDump.Message("<c:Red>Skipping failed fragment " + fragNum + " ".PadLeft(48));
                return;
            }

            while (fragPos < fragLen)
            {
                ReadBoxHeader(ref frag, ref fragPos, ref boxType, ref boxSize);
                if (boxType == "mdat") break;
                fragPos += boxSize;
            }
            HdsDump.DebugLog($"Fragment {fragNum}:\n");
            HdsDump.DebugLog(string.Format(format + "{4,-16}", "Type", "CurrentTS", "PreviousTS", "Size", "Position"));
            while (fragPos < fragLen)
            {
                int packetType = ReadByte(ref frag, fragPos);
                int packetSize = (int) ReadInt24(ref frag, fragPos + 1);
                packetTS = ReadInt24(ref frag, fragPos + 4);
                packetTS = (uint) packetTS | (uint) (ReadByte(ref frag, fragPos + 7) << 24);

                if ((packetTS & 0x80000000) == 0)
                    packetTS &= 0x7FFFFFFF;
                long totalTagLen = tagHeaderLen + packetSize + prevTagSize;

                // Try to fix the odd timestamps and make them zero based
                currentTS = packetTS;
                lastTS = prevVideoTS >= prevAudioTS ? prevVideoTS : prevAudioTS;
                fixedTS = lastTS + Constants.FRAMEFIX_STEP;
                if (baseTS == Constants.INVALID_TIMESTAMP && (packetType == Constants.AUDIO || packetType == Constants.VIDEO))
                    baseTS = packetTS;
                if (baseTS > 1000 && packetTS >= baseTS)
                    packetTS -= baseTS;

                if (lastTS != Constants.INVALID_TIMESTAMP)
                {
                    long timeShift = packetTS - lastTS;
                    if (timeShift > fixWindow)
                    {
                        HdsDump.DebugLog($"Timestamp gap detected: PacketTS={packetTS} LastTS={lastTS} Timeshift={timeShift}");
                        baseTS += timeShift - Constants.FRAMEFIX_STEP;
                        packetTS = fixedTS;
                    }
                    else
                    {
                        lastTS = packetType == Constants.VIDEO ? prevVideoTS : prevAudioTS;
                        if (packetTS < lastTS - fixWindow)
                        {
                            if (negTS != Constants.INVALID_TIMESTAMP && packetTS + negTS < lastTS - fixWindow)
                                negTS = Constants.INVALID_TIMESTAMP;
                            if (negTS == Constants.INVALID_TIMESTAMP)
                            {
                                negTS = (int) (fixedTS - packetTS);
                                HdsDump.DebugLog($"Negative timestamp detected: PacketTS={packetTS} LastTS={lastTS} NegativeTS={negTS}");
                                packetTS = fixedTS;
                            }
                            else
                            {
                                if (packetTS + negTS <= lastTS + fixWindow)
                                    packetTS += negTS;
                                else
                                {
                                    negTS = (int) (fixedTS - packetTS);
                                    HdsDump.DebugLog($"Negative timestamp override: PacketTS={packetTS} LastTS={lastTS} NegativeTS={negTS}");
                                    packetTS = fixedTS;
                                }
                            }
                        }
                    }
                }
                if (packetTS != currentTS)
                    WriteFlvTimestamp(ref frag, fragPos, packetTS);

                switch (packetType)
                {
                    case Constants.AUDIO:
                        if (packetTS > prevAudioTS - fixWindow)
                        {
                            int FrameInfo = ReadByte(ref frag, fragPos + tagHeaderLen);
                            int CodecID = (FrameInfo & 0xF0) >> 4;
                            if (CodecID == Constants.CODEC_ID_AAC)
                            {
                                AAC_PacketType = ReadByte(ref frag, fragPos + tagHeaderLen + 1);
                                if (AAC_PacketType == Constants.AAC_SEQUENCE_HEADER)
                                {
                                    if (AAC_HeaderWritten)
                                    {
                                        HdsDump.DebugLog("Skipping AAC sequence header");
                                        HdsDump.DebugLog(string.Format(format, "AUDIO", packetTS, prevAudioTS,
                                            packetSize));
                                        break;
                                    }
                                    HdsDump.DebugLog("Writing AAC sequence header");
                                    AAC_HeaderWritten = true;
                                }
                                else if (!AAC_HeaderWritten)
                                {
                                    HdsDump.DebugLog("Discarding audio packet received before AAC sequence header");
                                    HdsDump.DebugLog(string.Format(format, "AUDIO", packetTS, prevAudioTS, packetSize));
                                    break;
                                }
                            }
                            if (packetSize > 0)
                            {
                                // Check for packets with non-monotonic audio timestamps and fix them
                                if (CodecID != Constants.CODEC_ID_AAC && (AAC_PacketType == Constants.AAC_SEQUENCE_HEADER || prevAAC_Header))
                                {
                                    if (prevAudioTS != Constants.INVALID_TIMESTAMP && packetTS <= prevAudioTS)
                                    {
                                        HdsDump.DebugLog("Fixing audio timestamp");
                                        HdsDump.DebugLog(string.Format(format, "AUDIO", packetTS, prevAudioTS, packetSize));
                                        packetTS += Constants.FRAMEFIX_STEP / 5 + (prevAudioTS - packetTS);
                                        WriteFlvTimestamp(ref frag, fragPos, packetTS);
                                    }
                                }
                                prevAAC_Header = CodecID == Constants.CODEC_ID_AAC && AAC_PacketType == Constants.AAC_SEQUENCE_HEADER;

                                if (!notWrite && (currentTS > fromTimestamp || !FLVContinue))
                                    Write2File(ref frag, FileMode.Append, fragPos, totalTagLen);

                                HdsDump.DebugLog(string.Format(format, "AUDIO", packetTS, prevAudioTS, packetSize));
                                prevAudioTS = packetTS;
                                pAudioTagLen = totalTagLen;
                            }
                            else
                            {
                                HdsDump.DebugLog("Skipping small sized audio packet");
                                HdsDump.DebugLog(string.Format(format, "AUDIO", packetTS, prevAudioTS, packetSize));
                            }
                        }
                        else
                        {
                            HdsDump.DebugLog("Skipping audio packet in fragment fragNum");
                            HdsDump.DebugLog(string.Format(format, "AUDIO", packetTS, prevAudioTS, packetSize));
                        }
                        if (!audio) audio = true;
                        break;

                    case Constants.VIDEO:
                        if (packetTS > prevVideoTS - fixWindow)
                        {
                            int FrameInfo = ReadByte(ref frag, fragPos + tagHeaderLen);
                            int FrameType = (FrameInfo & 0xF0) >> 4;
                            int CodecID = FrameInfo & 0x0F;
                            if (FrameType == Constants.FRAME_TYPE_INFO)
                            {
                                HdsDump.DebugLog("Skipping video info frame");
                                HdsDump.DebugLog(string.Format(format, "VIDEO", packetTS, prevVideoTS, packetSize));
                                break;
                            }
                            if (CodecID == Constants.CODEC_ID_AVC)
                            {
                                AVC_PacketType = ReadByte(ref frag, fragPos + tagHeaderLen + 1);
                                if (AVC_PacketType == Constants.AVC_SEQUENCE_HEADER)
                                {
                                    if (AVC_HeaderWritten)
                                    {
                                        HdsDump.DebugLog("Skipping AVC sequence header");
                                        HdsDump.DebugLog(string.Format(format, "VIDEO", packetTS, prevVideoTS,
                                            packetSize));
                                        break;
                                    }
                                    HdsDump.DebugLog("Writing AVC sequence header");
                                    AVC_HeaderWritten = true;
                                }
                                else if (!AVC_HeaderWritten)
                                {
                                    HdsDump.DebugLog("Discarding video packet received before AVC sequence header");
                                    HdsDump.DebugLog(string.Format(format, "VIDEO", packetTS, prevVideoTS, packetSize));
                                    break;
                                }
                            }
                            if (packetSize > 0)
                            {
                                if (HdsDump.debug)
                                {
                                    long pts = packetTS;
                                    if ((CodecID == Constants.CODEC_ID_AVC) && (AVC_PacketType == Constants.AVC_NALU))
                                    {
                                        long cts = ReadInt24(ref frag, fragPos + tagHeaderLen + 2);
                                        cts = (cts + 0xff800000) ^ 0xff800000;
                                        pts = packetTS + cts;
                                        if (cts != 0)
                                            HdsDump.DebugLog($"DTS: {packetTS} CTS: {cts} PTS: {pts}");
                                    }
                                }

                                // Check for packets with non-monotonic video timestamps and fix them
                                if (
                                    !((CodecID == Constants.CODEC_ID_AVC) &&
                                      ((AVC_PacketType == Constants.AVC_SEQUENCE_HEADER) ||
                                       (AVC_PacketType == Constants.AVC_SEQUENCE_END) || prevAVC_Header)))
                                {
                                    if (prevVideoTS != Constants.INVALID_TIMESTAMP && packetTS <= prevVideoTS)
                                    {
                                        HdsDump.DebugLog("Fixing video timestamp");
                                        HdsDump.DebugLog(string.Format(format, "VIDEO", packetTS, prevVideoTS, packetSize));
                                        packetTS += Constants.FRAMEFIX_STEP/5 + (prevVideoTS - packetTS);
                                        WriteFlvTimestamp(ref frag, fragPos, packetTS);
                                    }
                                }
                                if ((CodecID == Constants.CODEC_ID_AVC) &&
                                    (AVC_PacketType == Constants.AVC_SEQUENCE_HEADER))
                                    prevAVC_Header = true;
                                else
                                    prevAVC_Header = false;

                                if (!notWrite && (currentTS > fromTimestamp || !FLVContinue))
                                        Write2File(ref frag, FileMode.Append, fragPos, totalTagLen);

                                HdsDump.DebugLog(string.Format(format, "VIDEO", packetTS, prevVideoTS, packetSize));
                                prevVideoTS = packetTS;
                                pVideoTagLen = totalTagLen;
                            }
                            else
                            {
                                HdsDump.DebugLog("Skipping small sized video packet");
                                HdsDump.DebugLog(string.Format(format, "VIDEO", packetTS, prevVideoTS, packetSize));
                            }
                        }
                        else
                        {
                            HdsDump.DebugLog("Skipping video packet in fragment fragNum");
                            HdsDump.DebugLog(string.Format(format, "VIDEO", packetTS, prevVideoTS, packetSize));
                        }
                        if (!video)
                            video = true;
                        break;

                    case Constants.SCRIPT_DATA:
                        break;

                    default:
                        if ((packetType == 10) || (packetType == 11))
                            HdsDump.Quit(
                                "<c:Red>This stream is encrypted with <c:Magenta>Akamai DRM</c>. Decryption of such streams isn't currently possible with this program. Not yet.");
                        else if ((packetType == 40) || (packetType == 41))
                            HdsDump.Quit(
                                "<c:Red>This stream is encrypted with <c:Magenta>FlashAccess DRM</c>. Decryption of such streams isn't currently possible with this program. Not yet.");
                        else
                            HdsDump.Quit("<c:Red>Unknown packet type <c:Magenta>" + packetType +
                                         "</c> encountered! Encrypted fragments can't be recovered. I'm so sorry.");
                        break;
                }
                fragPos += totalTagLen;
            }
            currentDuration = (int) Math.Round(packetTS / 1000.0);
        }

        private struct Segment
        {
            public int firstSegment;
            public int fragmentsPerSegment;
        }

        private struct Fragment
        {
            public int firstFragment;
            public long firstFragmentTimestamp;
            public int fragmentDuration;
            public int discontinuityIndicator;
        }

        private struct Manifest
        {
            public string bitrate;
            public string url;
            public XmlElement xml;
        }

        private struct Media
        {
            public string baseUrl;
            public string url;
            public string bootstrapUrl;
            public byte[] bootstrap;
            public byte[] metadata;
        }

        private struct Fragment2Dwnld
        {
            public string url;
            public byte[] data;
            public bool running;
            public bool ready;
        }
    }

    public static class HttpWebResponseExt
    {
        public static HttpWebResponse GetResponseNoException(HttpWebRequest req)
        {
            try
            {
                return (HttpWebResponse) req.GetResponse();
            }
            catch (WebException we)
            {
                HdsDump.DebugLog("Error downloading the link: " + req.RequestUri + "\r\nException: " + we.Message);
                var resp = we.Response as HttpWebResponse;
                if (resp == null)
                    HdsDump.Quit("<c:Red>" + we.Message + " (Request status: <c:Magenta>" + we.Status + "</c>)");
                //throw;
                return resp;
            }
        }
    }
}