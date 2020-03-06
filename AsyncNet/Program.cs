using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;

namespace AsyncNet
{
    class Options
    {
        [Option('s', "start", Required = false, Default = @"E:\test\starts.txt", HelpText = "start url file path.")]
        public string StartUrlPath { get; set; }

        [Option('r', "record", Required = false, Default = @"E:\test\visited.txt",
            HelpText = "visited url record file path.")]
        public string VisitedUrlPath { get; set; }

        [Option('d', "dest", Required = false, Default = @"E:\test\video", HelpText = "video save path.")]
        public string VideoStorePath { get; set; }

        [Option('t', "temp", Required = false, Default = @"E:\test\", HelpText = "temp file save path.")]
        public string TempPath { get; set; }

        [Option('p', "parallel", Required = false, Default = 8, HelpText = "video download parallel size.")]
        public int ParallelSize { get; set; }

        [Option('S', "speed", Required = false, Default = 102400, HelpText = "video download require min size.")]
        public int DownloadSpeed { get; set; }

        [Option('D', "time", Required = false, Default = 90, HelpText = "video download require max time.")]
        public int DownloadTime { get; set; }
    }

    class Program
    {
        static uint Convert(string url)
        {
            var match = Regex.Match(url, @"/video(\d+)/|/(\d+)/", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            return uint.Parse(
                string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[2].Value : match.Groups[1].Value);
        }

        private static string _visitePath = @"E:\test\visited.txt";
        private static string _startPath = @"E:\test\starts.txt";
        private static string _tempPath = @"E:\test\";
        private static string _storePath = @"E:\test\video";
        private static int _parallelSize = 8;
        private static int _time = 90;
        private static int _minSpeed = 100 * 1024;

        static async Task Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args).WithParsed(option =>
            {
                _startPath = option.StartUrlPath;
                _visitePath = option.VisitedUrlPath;
                _time = option.DownloadTime;
                _minSpeed = option.DownloadSpeed;
                _tempPath = option.TempPath;
                _storePath = option.VideoStorePath;
                _parallelSize = option.ParallelSize;
            });
            var speedCounter = new Speed();
            var visitedUrls = File.Exists(_visitePath)
                ? File.ReadLines(_visitePath).Select(Convert).ToHashSet()
                : new HashSet<uint>();
            var visitedPage = new HashSet<string>();
            var pages = new Queue<string>();
            if (File.Exists(_startPath))
            {
                foreach (var url in File.ReadLines(_startPath).Distinct())
                {
                    pages.Enqueue(url + "/videos/best/0");
                }
            }

            var client = new HttpClient(new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
            {
                Timeout = TimeSpan.FromSeconds(15),
                DefaultRequestHeaders =
                {
                    {
                        "User-Agent",
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/79.0.3945.130 Safari/537.36"
                    },
                    {
                        "Accept",
                        "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.9"
                    },
                    {"Accept-Encoding", "gzip, deflate, br"},
                    {"Connection", "keep-alive"}
                }
            };

            while (pages.TryDequeue(out var page))
            {
                if (visitedPage.Contains(page))
                {
                    continue;
                }

                var parentUrl = page.Substring(0, page.LastIndexOf('/') + 1);
                var listHtml = await client.GetStringOrNullAsync(page);
                var pageMatches = Regex.Matches(listHtml, @"""#(\d+)""", RegexOptions.Compiled);
                foreach (var pageMatch in pageMatches.Select(m => m.Groups[1].Value).Distinct())
                {
                    pages.Enqueue(parentUrl + pageMatch);
                }

                var matches = Regex.Matches(listHtml, @"href=""(/prof-video-click/.+?)""", RegexOptions.Compiled);
                var urls = new Queue<string>(matches.Where(m => m.Success)
                    .Select(m => "https://www.xvideos.com" + m.Groups[1].Value)
                    .Distinct());
                while (urls.TryDequeue(out var url))
                {
                    try
                    {
                        var videoId = Convert(url);
                        if (visitedUrls.Contains(videoId))
                        {
                            continue;
                        }

                        Console.WriteLine("start down {0}", url);
                        var html = await client.GetStringOrNullAsync(url);
                        if (string.IsNullOrEmpty(html))
                        {
                            continue;
                        }

                        var models = Regex.Matches(html, @"""(/models/.+?)""", RegexOptions.Compiled)
                            .Where(m => m.Success).Select(m =>
                            {
                                var link = "https://www.xvideos.com" + m.Groups[1].Value;
                                pages.Enqueue(link + "/videos/best/0");
                                return link;
                            });
                        File.AppendAllLines(_startPath, models);
                        var match = Regex.Match(html, @"'(https://.+\.xvideos-cdn\.com/.+/hls\.m3u8.*)'");
                        if (!match.Success)
                        {
                            continue;
                        }

                        var filePath = Guid.NewGuid().ToString();
                        var title = Regex.Match(html, "<title>(.+) - XVIDEOS.COM</title>");
                        if (title.Success)
                        {
                            filePath = title.Groups[1].Value.Trim();
                        }

                        var hls = match.Groups[1].Value;
                        Console.WriteLine(hls);
                        html = await client.GetStringOrNullAsync(hls);
                        if (string.IsNullOrEmpty(html))
                        {
                            continue;
                        }

                        var hlsDic = new Dictionary<int, string>();
                        var reader = new StringReader(html);
                        while (reader.Peek() > -1)
                        {
                            var line = reader.ReadLine();
                            if (line.StartsWith("#EXT-X-STREAM-INF"))
                            {
                                var np = Regex.Match(line, @"(\d+)p");
                                hlsDic.Add(int.Parse(np.Groups[1].Value), reader.ReadLine());
                            }
                        }

                        var baseUrl = hls.Substring(0, hls.LastIndexOf('/') + 1);
                        var downHls = baseUrl + hlsDic.Where(kv => kv.Key < 1080).OrderByDescending(kv => kv.Key)
                                          .First().Value;
                        Console.WriteLine(downHls);
                        html = await client.GetStringOrNullAsync(downHls);
                        if (string.IsNullOrEmpty(html))
                        {
                            continue;
                        }

                        reader = new StringReader(html);
                        var parts = new List<string>();
                        while (reader.Peek() > -1)
                        {
                            var line = reader.ReadLine();
                            if (line.StartsWith("#EXTINF:"))
                            {
                                parts.Add(reader.ReadLine());
                            }
                        }

                        if (parts.Count <= 0)
                        {
                            continue;
                        }

                        int num = -1,
                            successSize = 0,
                            parallelIndex = parts.Count - _parallelSize - 1,
                            earylReturnSize = parts.Count / 2 + 1;
                        var flag = true;
                        var result = Enumerable.Range(0, _parallelSize).Select(async index =>
                        {
                            int local, limitTime = _time;
                            while (flag && (local = Interlocked.Increment(ref num)) < parts.Count)
                            {
                                var part = parts[num];
                                var partUrl = baseUrl + part;
                                Console.WriteLine("task-{0} download {1} {3}/{2}", index, part, parts.Count, local);
                                var partFile = Path.Combine(_tempPath, $"{videoId}-{local}.ts");
                                using (var file = File.Open(partFile, FileMode.OpenOrCreate, FileAccess.Write))
                                {
                                    if (file.Length > 0)
                                    {
                                        file.Seek(file.Length, SeekOrigin.Begin);
                                    }

                                    var partParallel = Math.Min(local - parallelIndex, 3) + 1;
                                    if (partParallel > 1)
                                    {
                                        var length = await client.HeadContentLength(partUrl);
                                        length -= (int) file.Position;
                                        if (length == 0)
                                        {
                                            parts[local] = partFile;
                                            Interlocked.Increment(ref successSize);
                                            continue;
                                        }

                                        if (length > 64 * 1024)
                                        {
                                            var partLength = length / partParallel;
                                            var tasks = new List<Task>();
                                            var outputs = new List<Stream>();
                                            for (int i = 0, j = partParallel - 1; i <= j; i++)
                                            {
                                                var stream = new MemoryStream();
                                                outputs.Add(stream);
                                                int start = partLength * i + (int) file.Position,
                                                    end = (i == j ? length + (int) file.Position : start + partLength) -
                                                          1;
                                                tasks.Add(Task.Run(async () =>
                                                {
                                                    while (true)
                                                    {
                                                        var res = await client.GetRangeContent(partUrl, stream,
                                                            start + (int) stream.Position,
                                                            end, CancellationToken.None);
                                                        if (res)
                                                        {
                                                            break;
                                                        }
                                                    }
                                                }));
                                            }

                                            await Task.WhenAll(tasks);
                                            foreach (var output in outputs)
                                            {
                                                using (output)
                                                {
                                                    output.Position = 0;
                                                    await output.CopyToAsync(file);
                                                }
                                            }

                                            parts[local] = partFile;
                                            Interlocked.Increment(ref successSize);
                                            continue;
                                        }
                                    }

                                    var startTime = DateTime.Now;
                                    var hasSlow = false;
                                    do
                                    {
                                        try
                                        {
                                            using (var request = new HttpRequestMessage(HttpMethod.Get, partUrl))
                                            {
                                                if (file.Position > 0)
                                                {
                                                    request.Headers.Add("Range", $"bytes={file.Position}-");
                                                }

                                                using (var response = await client.SendAsync(request,
                                                    HttpCompletionOption.ResponseHeadersRead))
                                                {
                                                    if (!response.IsSuccessStatusCode || response.Content == null)
                                                    {
                                                        if (response.StatusCode ==
                                                            HttpStatusCode.RequestedRangeNotSatisfiable)
                                                        {
                                                            parts[local] = partFile;
                                                            Interlocked.Increment(ref successSize);
                                                            break;
                                                        }

                                                        Console.WriteLine($"task-{index} get file fail,break");
                                                        flag = false;
                                                        return;
                                                    }

                                                    if (limitTime == _time)
                                                    {
                                                        var contentHeaders = response.Content.Headers;
                                                        if (contentHeaders.ContentLength.HasValue)
                                                        {
                                                            limitTime = (int) contentHeaders.ContentLength.Value /
                                                                        (1024 * 20) + 10;
                                                        }
                                                    }

                                                    var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
                                                    try
                                                    {
                                                        using (var stream =
                                                            await response.Content.ReadAsStreamAsync())
                                                        {
                                                            while (flag)
                                                            {
                                                                using (var single =
                                                                    new CancellationTokenSource(10000))
                                                                {
                                                                    var readNum =
                                                                        await stream.ReadAsync(buffer, 0,
                                                                            buffer.Length,
                                                                            single.Token);
                                                                    speedCounter.Add(readNum);
                                                                    if (readNum <= 0)
                                                                    {
                                                                        break;
                                                                    }

                                                                    await file.WriteAsync(buffer, 0, readNum);
                                                                }
                                                            }
                                                        }
                                                    }
                                                    finally
                                                    {
                                                        ArrayPool<byte>.Shared.Return(buffer);
                                                    }
                                                }
                                            }

                                            parts[local] = partFile;
                                            Interlocked.Increment(ref successSize);
                                            break;
                                        }
                                        catch (Exception)
                                        {
                                            // ignored
                                        }

                                        if (successSize >= earylReturnSize)
                                        {
                                            continue;
                                        }

                                        var speed = speedCounter.Get();
                                        Console.WriteLine("speed {0:F}", speed / 1024d);
                                        var time = (DateTime.Now - startTime).TotalSeconds;
                                        if (time > limitTime && hasSlow && speed < _minSpeed)
                                        {
                                            Console.WriteLine($"task-{index} downland file slow,break");
                                            parts[local] = null;
                                            flag = false;
                                            return;
                                        }

                                        if (speed < _minSpeed)
                                        {
                                            hasSlow = true;
                                        }
                                    } while (flag);
                                }
                            }
                        });
                        await Task.WhenAll(result);
                        if (!flag)
                        {
                            continue;
                        }

                        var path = Path.Combine(_storePath, $"{filePath}_{videoId}.ts");
                        while (path.Length >= 255)
                        {
                            filePath = filePath.Substring(0, 50);
                            path = Path.Combine(_storePath, $"{filePath}_{videoId}.ts");
                        }

                        using (var video = File.Create(path))
                        {
                            foreach (var part in parts)
                            {
                                if (string.IsNullOrEmpty(part) || !File.Exists(part))
                                {
                                    continue;
                                }

                                using (var fileReader = File.OpenRead(part))
                                {
                                    fileReader.CopyTo(video);
                                }
                            }
                        }

                        parts.ForEach(File.Delete);
                        Console.WriteLine("success downland {0}", videoId);
                        visitedUrls.Add(videoId);
                        File.AppendAllText(_visitePath, url + Environment.NewLine);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        urls.Enqueue(url);
                    }
                }

                visitedPage.Add(page);
            }

            Console.WriteLine("over");
        }


        private static void AsyncConnectionTest()
        {
            var connect = new AsyncConnection(IPAddress.Parse("127.0.0.1"), 10010);
            connect.Connect();
            var tasks = new Task[1];
            var rand = new Random();
            for (var i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Factory.StartNew(() =>
                {
                    for (int j = 0; j < 1000; j++)
                    {
                        var msg = "ping " + Thread.CurrentThread.ManagedThreadId + "-" + j + "\n";
                        connect.Send(msg);
                        Console.WriteLine(msg);
                    }
                });
                Thread.Sleep(rand.Next(1, 1000));
            }

            Task.WaitAll(tasks);
//            connect.Send(File.ReadAllText(@"E:\PDF库\dotnet-inject.txt", Encoding.UTF8));
            Console.ReadKey();
            Console.WriteLine("send over");
            Console.ReadKey();
            Console.WriteLine("key");
            Console.ReadKey();
        }

        public string LongestCommonPrefix(string[] strs)
        {
            if (strs == null || strs.Length <= 0)
            {
                return "";
            }

            var commonStr = strs[0];
            for (var i = 1; i < strs.Length; i++)
            {
                var str = strs[i];
                var j = 0;
                for (; j < Math.Min(str.Length, commonStr.Length); j++)
                {
                    if (str[j] == commonStr[j])
                        continue;
                    if (j == 0)
                    {
                        return "";
                    }

                    break;
                }

                commonStr = str.Substring(0, j);
            }

            return commonStr;
        }

        public static bool IsValid(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length % 2 != 0)
            {
                return false;
            }

            var dic = new Dictionary<char, char> {{')', '('}, {']', '['}, {'}', '{'}};
            var stack = new Stack<char>();
            for (var i = 0; i < s.Length; i++)
            {
                var ch = s[i];
                if (dic.TryGetValue(ch, out var cmp))
                {
                    if (stack.Count <= 0)
                    {
                        return false;
                    }

                    if (cmp != stack.Pop())
                    {
                        return false;
                    }
                }
                else
                {
                    stack.Push(ch);
                }
            }

            return true;
        }

        public static int SingleNumber(int[] nums)
        {
            var num = 0;
            for (var i = 0; i < nums.Length; i++)
            {
                num = nums[i] ^ num;
            }

            return num;
        }

        public class ListNode
        {
            public int val;
            public ListNode next;

            public ListNode(int x)
            {
                val = x;
            }
        }

        public ListNode MergeTwoLists(ListNode l1, ListNode l2)
        {
            ListNode head = null, newNode = null;
            if (l1 != null && l2 != null)
            {
                while (l1 != null && l2 != null)
                {
                    ListNode next;
                    if (l1.val <= l2.val)
                    {
                        next = new ListNode(l1.val);
                        l1 = l1.next;
                    }
                    else
                    {
                        next = new ListNode(l2.val);
                        l2 = l2.next;
                    }

                    if (newNode != null)
                    {
                        newNode.next = next;
                    }
                    else
                    {
                        head = next;
                    }

                    newNode = next;
                }
            }


            while (l1 != null)
            {
                if (newNode == null)
                {
                    head = newNode = new ListNode(l1.val);
                }
                else
                {
                    newNode.next = new ListNode(l1.val);
                    newNode = newNode.next;
                }

                l1 = l1.next;
            }

            while (l2 != null)
            {
                if (newNode == null)
                {
                    head = newNode = new ListNode(l2.val);
                }
                else
                {
                    newNode.next = new ListNode(l2.val);
                    newNode = newNode.next;
                }

                l2 = l2.next;
            }

            return head;
        }

        public static int RemoveDuplicates(int[] nums)
        {
            var j = 0;
            for (int i = 0; i < nums.Length - 1; i++)
            {
                if (nums[i] != nums[i + 1])
                {
                    nums[j] = nums[i];
                    nums[++j] = nums[i + 1];
                }
            }

            return j + 1;
        }

        public int RemoveElement(int[] nums, int val)
        {
            var j = 0;
            for (int i = 0; i < nums.Length; i++)
            {
                if (nums[i] != val)
                {
                    nums[j++] = nums[i];
                }
            }

            return j;
        }

        public int StrStr(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(needle))
            {
                return 0;
            }

            if (needle.Length - haystack.Length > 0)
            {
                return -1;
            }

            for (int i = 0, len = haystack.Length - needle.Length; i <= len; i++)
            {
                if (haystack[i] != needle[0])
                    continue;
                var found = true;
                for (var j = 1; j < needle.Length; j++)
                {
                    if (needle[j] != haystack[i + j])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}