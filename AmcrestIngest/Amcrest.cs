using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AmcrestApi
{
    public class Session
    {
        private string _host;
        private string _user;
        private string _password;

        private string? _realm;
        private string? _nonce;
        private string? _qop;
        private string? _cnonce;
        private DateTime _cnonceDate;
        private int _nc;

        private FileFindingApi? fileFindingApi = null;
        public FileFindingApi FileFinding
        {
            get
            {
                if (fileFindingApi == null)
                    fileFindingApi = new(this);
                return fileFindingApi;
            }
        }

        private static string CalculateMd5Hash(string input)
        {
            using MD5 md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.ASCII.GetBytes(input));
            StringBuilder sb = new();
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static bool TryGrabHeaderVar(string varName, string header, out string? headerVar)
        {
            Match matchHeader = new Regex(@$"{varName}=""([^""]*)""").Match(header);
            headerVar = matchHeader.Success ? matchHeader.Groups[1].Value : null;
            return headerVar != null;
        }

        private string GetDigestHeader(string dir)
        {
            _nc++;
            var ha1 = CalculateMd5Hash(string.Format("{0}:{1}:{2}", _user, _realm, _password));
            var ha2 = CalculateMd5Hash(string.Format("{0}:{1}", "GET", dir));
            var digestResponse =
                CalculateMd5Hash(string.Format("{0}:{1}:{2:00000000}:{3}:{4}:{5}", ha1, _nonce, _nc, _cnonce, _qop, ha2));
            return string.Format("Digest username=\"{0}\", realm=\"{1}\", nonce=\"{2}\", uri=\"{3}\", " +
                "algorithm=MD5, response=\"{4}\", qop={5}, nc={6:00000000}, cnonce=\"{7}\"",
                _user, _realm, _nonce, dir, digestResponse, _qop, _nc, _cnonce);
        }

        public async Task<HttpResponseMessage> GetDigestResponse(string dir)
        {
            Uri uri = new(_host + dir);

            using HttpClient client = new();

            HttpRequestMessage request = new(HttpMethod.Get, uri);

            if (!string.IsNullOrEmpty(_cnonce) && DateTime.Now.Subtract(_cnonceDate).TotalHours < 1.0)
                request.Headers.Add("Authorization", GetDigestHeader(dir));

            HttpResponseMessage? response = null;
            try
            {
                response = await client.SendAsync(request);
                request?.Dispose();
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                if (response == null || ex.StatusCode != HttpStatusCode.Unauthorized)
                    throw;
                HttpHeaderValueCollection<AuthenticationHeaderValue> wwwAuthenticateHeader = response.Headers.WwwAuthenticate;
                bool getRealm = true, getNonce = true, getQop = true;
                foreach (AuthenticationHeaderValue auth in wwwAuthenticateHeader)
                {
                    if (auth.Parameter == null)
                        continue;
                    if (getRealm && TryGrabHeaderVar("realm", auth.Parameter, out _realm))
                        getRealm = false;
                    if (getNonce && TryGrabHeaderVar("nonce", auth.Parameter, out _nonce))
                        getNonce = false;
                    if (getQop && TryGrabHeaderVar("qop", auth.Parameter, out _qop))
                        getQop = false;
                }

                _nc = 0;
                _cnonce = new Random().Next(123400, 9999999).ToString();
                _cnonceDate = DateTime.Now;

                request = new(HttpMethod.Get, uri);
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(GetDigestHeader(dir));
                response?.Dispose();
                response = await client.SendAsync(request);
            }
            request?.Dispose();
            return response;
        }

        public Session(string host, string user, string password)
        {
            _host = host;
            _user = user;
            _password = password;
        }

        public class FileFindingApi
        {
            private readonly Session _session;

            public class MediaFinder
            {
                public string ObjectId { get; }
                private MediaFinder(string objectId) =>
                    ObjectId = objectId;
                public static MediaFinder? Parse(string response)
                {
                    string[] parts = response.Split('=');
                    return parts.Length == 2 ? new MediaFinder(parts[1].Trim()) : null;
                }
                public override string ToString() =>
                    ObjectId;
            }

            public struct QueryStatus
            {
                public bool Status { get; }
                private QueryStatus(bool status) =>
                    Status = status;
                public static QueryStatus Parse(string response) =>
                    response.Trim().ToLower() == "ok" ? new QueryStatus(true) : new QueryStatus(false);
                public static implicit operator bool(QueryStatus startFindResult) =>
                    startFindResult.Status;
                public override string ToString() =>
                    Status ? "OK" : "Error";
            }

            public class Flags
            {
                public static readonly Flags Cutout = new("Cutout");
                public static readonly Flags Event = new("Event");
                public static readonly Flags Manual = new("Manual");
                public static readonly Flags Marker = new("Marker");
                public static readonly Flags Mosaic = new("Mosaic");
                public static readonly Flags Timing = new("Timing");
                private readonly string _flag;
                private Flags(string flag) =>
                    _flag = flag;
                public override string ToString() =>
                    _flag;
                public static bool TryParse(string input, out Flags? flag)
                {
                    if (input == Cutout._flag)
                        flag = Cutout;
                    else if (input == Event._flag)
                        flag = Event;
                    else if (input == Manual._flag)
                        flag = Manual;
                    else if (input == Marker._flag)
                        flag = Marker;
                    else if (input == Mosaic._flag)
                        flag = Mosaic;
                    else if (input == Timing._flag)
                        flag = Timing;
                    else
                        flag = null;
                    return flag != null;
                }
            }

            public class Events
            {
                public static readonly Events AlarmLocal = new("AlarmLocal");
                public static readonly Events VideoBlind = new("VideoBlind");
                public static readonly Events VideoLoss = new("VideoLoss");
                public static readonly Events VideoMotion = new("VideoMotion");
                public static readonly Events Traffic = new("Traffic");
                private readonly string _event;
                private Events(string @event) =>
                    _event = @event;
                public override string ToString() =>
                    _event;
                public static bool TryParse(string input, out Events? @event)
                {
                    if (input == AlarmLocal._event)
                        @event = AlarmLocal;
                    else if (input == VideoBlind._event)
                        @event = VideoBlind;
                    else if (input == VideoLoss._event)
                        @event = VideoLoss;
                    else if (input == VideoMotion._event)
                        @event = VideoMotion;
                    else if (input == Traffic._event)
                        @event = Traffic;
                    else
                        @event = null;
                    return @event != null;
                }
            }

            public struct Types
            {
                public static readonly Types Dav = new("dav");
                public static readonly Types Jpg = new("jpg");
                public static readonly Types Mp4 = new("mp4");
                private readonly string _type;
                private Types(string type) =>
                    _type = type;
                public override string ToString() =>
                    _type;
                public static Types? TryParse(string input)
                {
                    if (input == Dav._type)
                        return Dav;
                    else if (input == Jpg._type)
                        return Jpg;
                    else if (input == Mp4._type)
                        return Mp4;
                    return null;
                }
            }

            public class TrafficCar
            {
                public string? PlateColor { get; set; }
                public string? PlateNumber { get; set; }
                public string? PlateType { get; set; }
                public string? Speed { get; set; }
                public string? VehicleColor { get; set; }
            }

            public class Summary
            {
                public TrafficCar? TrafficCar { get; set; }
            }

            public class QueryItem
            {
                public string? Channel { get; set; }
                public string? Cluster { get; set; }
                public string? CutLength { get; set; }
                public string? Disk { get; set; }
                public string? Duration { get; set; }
                public DateTime EndTime { get; set; }
                internal Dictionary<int, Events>? _events;
                public IReadOnlyDictionary<int, Events>? Events => _events;
                public string? FilePath { get; set; }
                internal Dictionary<int, Flags>? _flags;
                public IReadOnlyDictionary<int, Flags>? Flags => _flags;
                public string? Length { get; set; }
                public string? Partition { get; set; }
                public string? PicIndex { get; set; }
                public string? Repeat { get; set; }
                public DateTime StartTime { get; set; }
                public Summary? Summary { get; set; }
                public Types? Type { get; set; }
                public string? VideoStream { get; set; }
                public string? WorkDir { get; set; }
                public string? WorkDirSN { get; set; }
            }

            public struct DisposeResult
            {
                public bool Status { get; }
                private DisposeResult(bool status) =>
                    Status = status;
                public static DisposeResult Parse(string response) =>
                    response.Trim().ToLower() == "ok" ? new DisposeResult(true) : new DisposeResult(false);
                public static implicit operator bool(DisposeResult startFindResult) =>
                    startFindResult.Status;
            }

            public async Task<MediaFinder?> CreateMediaFinder()
            {
                HttpResponseMessage response = await _session.GetDigestResponse("/cgi-bin/mediaFileFind.cgi?action=factory.create");
                MediaFinder? finder = MediaFinder.Parse(await response.Content.ReadAsStringAsync());
                response?.Dispose();
                return finder;
            }

            public async Task<DisposeResult> DisposeMediaFinder(MediaFinder mediaFinder)
            {
                HttpResponseMessage response = await _session.GetDigestResponse($"/cgi-bin/mediaFileFind.cgi?action=destroy&object={mediaFinder}");
                DisposeResult disposeResult = DisposeResult.Parse(await response.Content.ReadAsStringAsync());
                response?.Dispose();
                return disposeResult;
            }

            public async Task<QueryStatus> SetQuery(MediaFinder mediaFinder, int channel, DateTime startTime,
                DateTime endTime, string[]? dir = null, Types[]? types = null, Flags[]? flags = null, Events[]? events = null)
            {
                StringBuilder sb = new();
                sb.Append($"/cgi-bin/mediaFileFind.cgi?action=findFile");
                sb.Append($"&object={mediaFinder}");
                sb.Append($"&condition.Channel={channel}");

                if (dir != null)
                    for (int i = 0; i < dir.Length; i++)
                        sb.Append($"&conditon.Dir[{i}]=\"{dir[i]}\"");
                if (types != null)
                    for (int i = 0; i < types.Length; i++)
                        sb.Append($"&conditon.Types[{i}]={types[i]}");
                if (flags != null)
                    for (int i = 0; i < flags.Length; i++)
                        sb.Append($"&conditon.Flags[{i}]={flags[i]}");
                if (events != null)
                    for (int i = 0; i < events.Length; i++)
                        sb.Append($"&conditon.Event[{i}]={events[i]}");

                sb.Append($"&condition.StartTime={startTime:yyyy-MM-dd\\%20HH:mm:ss}");
                sb.Append($"&condition.EndTime={endTime:yyyy-MM-dd\\%20HH:mm:ss}");

                HttpResponseMessage response = await _session.GetDigestResponse(sb.ToString());
                QueryStatus status = QueryStatus.Parse(await response.Content.ReadAsStringAsync());
                response?.Dispose();
                return status;
            }

            public async Task<IReadOnlyCollection<QueryItem>> GetQueryItems(MediaFinder mediaFinder, int count = 1)
            {
                if (count < 1)
                    count = 1;
                if (count > 100)
                    count = 100;

                HttpResponseMessage response = await _session.GetDigestResponse($"/cgi-bin/mediaFileFind.cgi?action=findNextFile&object={mediaFinder}&count={count}");
                string result = await response.Content.ReadAsStringAsync();
                response?.Dispose();

                int indexFirstLine = result.IndexOf('\n');
                if (indexFirstLine > 0 && int.TryParse(result[6..indexFirstLine], out int found))
                {
                    QueryItem[] queryItems = new QueryItem[found];

                    for (int i = indexFirstLine + 1, start = i; i < result.Length; i++)
                    {
                        if (result[i] == '\n')
                        {
                            int equalIndex = result.IndexOf('=', start, i - start);
                            string left = result[start..equalIndex];
                            string right = result[(equalIndex + 1)..i].Trim();

                            string[] leftParts = left.Split('.');

                            int indexStart = leftParts[0].IndexOf('[') + 1;
                            int indexEnd = leftParts[0].IndexOf(']');
                            if (int.TryParse(left[indexStart..indexEnd], out int itemIndex))
                            {
                                queryItems[itemIndex] ??= new();

                                QueryItem currentItem = queryItems[itemIndex];

                                switch (leftParts[1])
                                {
                                    case "Channel":
                                        {
                                            currentItem.Channel = right;
                                            break;
                                        }
                                    case "Cluster":
                                        {
                                            currentItem.Cluster = right;
                                            break;
                                        }
                                    case "CutLength":
                                        {
                                            currentItem.CutLength = right;
                                            break;
                                        }
                                    case "Disk":
                                        {
                                            currentItem.Disk = right;
                                            break;
                                        }
                                    case "Duration":
                                        {
                                            currentItem.Duration = right;
                                            break;
                                        }
                                    case "EndTime":
                                        {
                                            currentItem.EndTime = DateTime.Parse(right);
                                            break;
                                        }
                                    case "FilePath":
                                        {
                                            currentItem.FilePath = right;
                                            break;
                                        }
                                    case "Length":
                                        {
                                            currentItem.Length = right;
                                            break;
                                        }
                                    case "Partition":
                                        {
                                            currentItem.Partition = right;
                                            break;
                                        }
                                    case "PicIndex":
                                        {
                                            currentItem.PicIndex = right;
                                            break;
                                        }
                                    case "Repeat":
                                        {
                                            currentItem.Repeat = right;
                                            break;
                                        }
                                    case "StartTime":
                                        {
                                            currentItem.StartTime = DateTime.Parse(right);
                                            break;
                                        }
                                    case "Summary":
                                        {
                                            if (leftParts.Length < 3)
                                                break;

                                            switch (leftParts[2]) // using a switch in case of extensions in the future
                                            {
                                                case "TrafficCar":
                                                    {
                                                        currentItem.Summary ??= new();

                                                        if (leftParts.Length < 4)
                                                            break;
                                                        switch (leftParts[3])
                                                        {
                                                            case "PlateColor":
                                                                {
                                                                    currentItem.Summary.TrafficCar ??= new();
                                                                    currentItem.Summary.TrafficCar.PlateColor = right;
                                                                    break;
                                                                }
                                                            case "PlateNumber":
                                                                {
                                                                    currentItem.Summary.TrafficCar ??= new();
                                                                    currentItem.Summary.TrafficCar.PlateNumber = right;
                                                                    break;
                                                                }
                                                            case "PlateType":
                                                                {
                                                                    currentItem.Summary.TrafficCar ??= new();
                                                                    currentItem.Summary.TrafficCar.PlateType = right;
                                                                    break;
                                                                }
                                                            case "Speed":
                                                                {
                                                                    currentItem.Summary.TrafficCar ??= new();
                                                                    currentItem.Summary.TrafficCar.Speed = right;
                                                                    break;
                                                                }
                                                            case "VehicleColor":
                                                                {
                                                                    currentItem.Summary.TrafficCar ??= new();
                                                                    currentItem.Summary.TrafficCar.VehicleColor = right;
                                                                    break;
                                                                }
                                                            default:
                                                                {
                                                                    Console.WriteLine($"New Summary.TrafficCar property: {leftParts[3]}");
                                                                    break;
                                                                }
                                                        }
                                                        break;
                                                    }
                                                default:
                                                    {
                                                        Console.WriteLine($"New Summary property: {leftParts[2]}");
                                                        break;
                                                    }
                                            }
                                            break;
                                        }
                                    case "Type":
                                        {
                                            currentItem.Type = Types.TryParse(right);
                                            break;
                                        }
                                    case "VideoStream":
                                        {
                                            currentItem.VideoStream = right;
                                            break;
                                        }
                                    case "WorkDir":
                                        {
                                            currentItem.WorkDir = right;
                                            break;
                                        }
                                    case "WorkDirSN":
                                        {
                                            currentItem.WorkDirSN = right;
                                            break;
                                        }
                                    default:
                                        {
                                            int secondaryIndexStart = leftParts[1].IndexOf('[') + 1;
                                            int secondaryIndexEnd = leftParts[1].IndexOf(']');

                                            if (int.TryParse(leftParts[1][secondaryIndexStart..secondaryIndexEnd], out int secondaryIndex))
                                            {
                                                if (leftParts[1].StartsWith("Events["))
                                                {
                                                    if (Events.TryParse(right, out Events? @event) && @event != null)
                                                    {
                                                        currentItem._events ??= new();
                                                        currentItem._events.TryAdd(secondaryIndex, @event);
                                                    }
                                                }
                                                else if (leftParts[1].StartsWith("Flags["))
                                                {
                                                    if (Flags.TryParse(right, out Flags? flag) && flag != null)
                                                    {
                                                        currentItem._flags ??= new();
                                                        currentItem._flags.TryAdd(secondaryIndex, flag);
                                                    }
                                                }
                                            }
                                            break;
                                        }
                                }
                            }
                            start = ++i;
                        }
                    }
                    return queryItems;
                }
                return Array.Empty<QueryItem>();
            }

            public FileFindingApi(Session session) =>
                _session = session;
        }

        public async Task DownloadMedia(string folderPath, FileFindingApi.QueryItem item)
        {
            if (item.FilePath != null && item.FilePath.Trim() != "")
            {
                string savePath = Path.GetFullPath(folderPath);
                savePath = Path.Combine(savePath, item.StartTime.ToString("yyyy"));
                savePath = Path.Combine(savePath, item.StartTime.ToString("MM"));
                savePath = Path.Combine(savePath, item.StartTime.ToString("dd"));
                savePath = Path.Combine(savePath, item.StartTime.ToString("HH ❨h tt❩", CultureInfo.InvariantCulture));

                if (!Directory.Exists(savePath))
                    Directory.CreateDirectory(savePath);

                string ext = Path.GetExtension(item.FilePath);

                string fileName = $"{item.StartTime:yyyy-MM-dd hh∶mm∶ss tt} ❯ {item.EndTime:yyyy-MM-dd hh∶mm∶ss tt}{ext}";

                savePath = Path.Combine(savePath, fileName);

                if (File.Exists(savePath))
                    return;

                Console.WriteLine($"Saving: {fileName}");
                HttpResponseMessage response = await GetDigestResponse($"/cgi-bin/RPC_Loadfile{item.FilePath}");
                byte[] test = await response.Content.ReadAsByteArrayAsync();
                response?.Dispose();
                await File.WriteAllBytesAsync(savePath, test);
            }
        }

    }
}