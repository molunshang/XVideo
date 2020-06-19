using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;

namespace AsyncNet
{
    class Options
    {
        [Option('s', "start", Required = false, Default = "starts.txt", HelpText = "start page url file path.")]
        public string StartPagePath { get; set; }

        [Option('r', "record", Required = false, Default = "visited.txt",
            HelpText = "visited url record file path.")]
        public string VisitedUrlPath { get; set; }
        [Option('b', "black", Required = false, Default = "blacks.txt",
            HelpText = "the black urls file path.")]
        public string BlackUrlPath { get; set; }

        [Option('d', "dest", Required = false, Default = "video", HelpText = "video save path.")]
        public string VideoStorePath { get; set; }

        [Option('t', "temp", Required = false, Default = "", HelpText = "temp file save path.")]
        public string TempPath { get; set; }

        [Option('p', "parallel", Required = false, Default = 8, HelpText = "video download parallel size.")]
        public int ParallelSize { get; set; }

        [Option('S', "start-detail", Required = false, Default = "items.txt", HelpText = "start item url file path..")]
        public string StartItemPath { get; set; }

        [Option('D', "time", Required = false, Default = 90, HelpText = "video download require max time.")]
        public int DownloadTime { get; set; }
        [Option("depth", Required = false, Default = 6, HelpText = "crawl url depth.min=0,max=9")]
        public int Depth { get; set; }
        [Option("deep", Required = false, Default = false, HelpText = "use dfs first")]
        public bool Deep { get; set; }
    }

    class Link
    {
        public string Url { get; set; }
        public byte Depth { get; set; }
    }

    public class Program
    {
        public static uint Convert(string url)
        {
            var match = Regex.Match(url, @"/video(\d+)/|/(\d+)/", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            return uint.Parse(
                string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[2].Value : match.Groups[1].Value);
        }

        private static bool deep = false;
        private static string _visitePath = "visited.txt";
        private static string _blackPath = "blacks.txt";
        private static string _startPath = "starts.txt";
        private static string _startPathHistory = _startPath + ".history";
        private static string _startItemPath = "items.txt";
        private static string _tempPath = "";
        private static string _storePath = "video";
        private static int _parallelSize = 8;
        private static int _time = 90 * 1000;
        private static byte _maxDepth = 6;

        const string baseHost = "https://www.xvideos.com";
        static async Task Main(string[] args)
        {
            var running = true;
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                running = false;
            };
            HttpEventListener.Init();
            Parser.Default.ParseArguments<Options>(args).WithParsed(option =>
            {
                _startPath = option.StartPagePath;
                _startPathHistory = _startPath + ".history";
                _visitePath = option.VisitedUrlPath;
                _time = option.DownloadTime * 1000;
                _startItemPath = option.StartItemPath;
                _tempPath = option.TempPath;
                _storePath = option.VideoStorePath;
                _parallelSize = option.ParallelSize;
                _blackPath = option.BlackUrlPath;
                _maxDepth = (byte)Math.Min(9, option.Depth);
                deep = option.Deep;
            }).WithNotParsed(err =>
            {
                running = false;
            });
            if (!running)
            {
                return;
            }
            var speedCounter = new Speed();
            var visitedUrls = File.Exists(_visitePath)
                ? File.ReadLines(_visitePath).Where(Utils.IsNotNullOrWhiteSpace).Select(Convert).ToHashSet()
                : new HashSet<uint>();
            var filter = new HashSet<string>();
            var starts = new Queue<string>();
            var pages = new Stack<string>();
            if (File.Exists(_blackPath))
            {
                foreach (var url in File.ReadLines(_blackPath).Where(Utils.IsNotNullOrWhiteSpace))
                {
                    filter.Add(url);
                }
            }
            if (File.Exists(_startPath))
            {
                var links = new LinkedList<string>();
                var dict = new Dictionary<string, LinkedListNode<string>>();
                foreach (var url in File.ReadLines(_startPath).Where(Utils.IsNotNullOrWhiteSpace))
                {
                    var tag = url.Substring(url.LastIndexOf('/') + 1);
                    if (filter.Contains(tag) || dict.ContainsKey(url))
                    {
                        continue;
                    }
                    starts.Enqueue(url + "/videos/best/0");
                    starts.Enqueue(url + "/favorites/0");
                    dict[url] = links.AddLast(url);
                }
                if (File.Exists(_startPathHistory))
                {
                    foreach (var url in File.ReadLines(_startPathHistory).Where(Utils.IsNotNullOrWhiteSpace))
                    {
                        if (dict.TryGetValue(url, out var node))
                        {
                            links.Remove(node);
                        }
                        dict[url] = links.AddLast(url);
                    }
                }
                File.Copy(_startPath, _startPath + ".bak", true);
                File.WriteAllLines(_startPath, links);
                dict.Clear();
            }
            var urls = new Queue<Link>();
            //未下载完视频处理
            if (!Directory.Exists(_tempPath))
            {
                Directory.CreateDirectory(_tempPath);
            }
            if (!Directory.Exists(_storePath))
            {
                Directory.CreateDirectory(_storePath);
            }
            var tempFiles = Directory.GetFiles(_tempPath, "*.ts");
            var tempIds = tempFiles.Select(f =>
            {
                var idMatch = Regex.Match(f, @"(\d+)-(\d+)\.ts");
                return idMatch.Success ? idMatch.Groups[1].Value : string.Empty;
            }).GroupBy(f => f).Where(g => !string.IsNullOrEmpty(g.Key)).OrderByDescending(g => g.Count());
            foreach (var tempId in tempIds)
            {
                urls.Enqueue(new Link() { Url = $"https://www.xvideos.com/video{tempId.Key}/_" });
            }
            //视频链接
            if (deep)
            {
                LoadVideoItems(visitedUrls, urls);
            }


            var socketHandler = new SocketsHttpHandler()
            {
                ConnectTimeout = TimeSpan.FromSeconds(15),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
            };

            var client = new HttpClient(new HttpResponseTimeoutHandler(socketHandler)
            {
                ResponseTimeout = TimeSpan.FromSeconds(15)
            }, true)
            {
                Timeout = TimeSpan.FromMinutes(10),
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
            var downPartQueue = new ConcurrentQueue<int>();
            var parts = new List<string>();
            var noVideoSize = 0;
            while (running)
            {
                while (running && urls.TryDequeue(out var item))
                {
                    if (item.Depth >= _maxDepth || item.Url.Contains("THUMBNUM"))
                    {
                        continue;
                    }
                    try
                    {
                        var url = item.Url;
                        var videoId = Convert(url);
                        if (visitedUrls.Contains(videoId))
                        {
                            continue;
                        }
                        if (!url.StartsWith("https://") && !url.StartsWith("http://"))
                        {
                            url = baseHost + url;
                        }
                        Console.WriteLine("start down {0}", url);
                        var res = await client.GetStringOrNullAsync(url);
                        if (string.IsNullOrEmpty(res.Item2))
                        {
                            if (res.Item1)
                            {
                                visitedUrls.Add(videoId);
                                File.AppendAllText(_visitePath, url + Environment.NewLine);
                            }
                            await Task.Delay(1000);
                            continue;
                        }
                        var html = res.Item2;
                        var tagFilter = Regex.Matches(html, @"/(tags|channels|model-channels|channels|amateur-channels|profiles)/([^""]+)", RegexOptions.Compiled).Any(m => m.Success && filter.Contains(m.Groups[2].Value));
                        if (tagFilter)
                        {
                            visitedUrls.Add(videoId);
                            File.AppendAllText(_visitePath, url + Environment.NewLine);
                            continue;
                        }

                        var models = Regex.Matches(html, @"""(/models/.+?)""", RegexOptions.Compiled)
                            .Union(Regex.Matches(html, @"""(\\/(model-channels|channels|amateur-channels)\\/.+?)""", RegexOptions.Compiled)).Where(m => m.Success).Select(m =>
                            {
                                var tag = Regex.Unescape(m.Groups[1].Value);
                                if (filter.Contains(tag.Substring(tag.LastIndexOf('/') + 1)))
                                {
                                    return string.Empty;
                                }
                                var link = baseHost + tag;
                                starts.Enqueue(link + "/videos/best/0");
                                starts.Enqueue(link + "/favorites/0");
                                return link;
                            }).Distinct();
                        File.AppendAllLines(_startPath, models);
                        var relates = Regex.Matches(html, @"""(\\/video\d+?\\/.+?)""", RegexOptions.Compiled).Where(m => m.Success).Select(r =>
                        {
                            var l = Regex.Unescape(r.Groups[1].Value);
                            var link = new Link() { Url = l, Depth = (byte)(item.Depth + 1) };
                            if (deep)
                            {
                                urls.Enqueue(link);
                            }
                            return $"{link.Depth}|{link.Url}";
                        });
                        await File.AppendAllLinesAsync(_startItemPath, relates);

                        var match = Regex.Match(html, @"'(https://.+\.xvideos-cdn\.com/.+/hls\.m3u8.*)'");
                        if (!match.Success)
                        {
                            noVideoSize++;
                            if (noVideoSize >= 60)
                            {
                                running = false;
                            }
                            continue;
                        }
                        noVideoSize = 0;
                        var filePath = Guid.NewGuid().ToString();
                        var title = Regex.Match(html, "<title>(.+) - XVIDEOS.COM</title>");
                        if (title.Success)
                        {
                            filePath = title.Groups[1].Value.Trim();
                        }

                        var hls = match.Groups[1].Value;
                        Console.WriteLine(hls);
                        html = (await client.GetStringOrNullAsync(hls)).Item2;
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
                        html = (await client.GetStringOrNullAsync(downHls)).Item2;
                        if (string.IsNullOrEmpty(html))
                        {
                            continue;
                        }

                        reader = new StringReader(html);
                        parts.Clear();
                        while (reader.Peek() > -1)
                        {
                            var line = reader.ReadLine();
                            if (line.StartsWith("#EXTINF:"))
                            {
                                downPartQueue.Enqueue(parts.Count);
                                parts.Add(reader.ReadLine());
                            }
                        }

                        if (parts.Count <= 0)
                        {
                            continue;
                        }
                        var success = true;
                        var successSize = 0;
                        using (var commonCts = new CancellationTokenSource())
                        {
                            var downTasks = Enumerable.Range(0, _parallelSize).Select(async index =>
                              {
                                  var parallelStreams = new Stream[_parallelSize];
                                  var partFlags = new bool[_parallelSize];
                                  for (int i = 0; i < parallelStreams.Length; i++)
                                  {
                                      parallelStreams[i] = new MemoryStream();
                                  }
                                  while (downPartQueue.TryDequeue(out var local) && success)
                                  {
                                      var part = parts[local];
                                      var partUrl = baseUrl + part;
                                      Console.WriteLine("task-{3} download {0} {2}/{1}", part, parts.Count, local + 1, index);
                                      var partFile = Path.Combine(_tempPath, $"{videoId}-{local}.ts");
                                      using (var file = File.Open(partFile, FileMode.OpenOrCreate, FileAccess.Write))
                                      {
                                          if (file.Length > 0)
                                          {
                                              file.Seek(file.Length, SeekOrigin.Begin);
                                          }

                                          var length = await client.HeadContentLength(partUrl);
                                          if (length < 0)
                                          {
                                              downPartQueue.Enqueue(local);
                                              continue;
                                          }
                                          length -= (int)file.Position;
                                          if (length == 0)
                                          {
                                              parts[local] = partFile;
                                              Console.WriteLine("download {0} success", Interlocked.Increment(ref successSize));
                                              await Task.Delay(100);
                                              continue;
                                          }
                                          var flag = true;
                                          using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(commonCts.Token))
                                          {
                                              using (cancel.Token.Register(() =>
                                              {
                                                  if (downPartQueue.Count <= _parallelSize)
                                                  {
                                                      return;
                                                  }
                                                  commonCts.Cancel();
                                              }))
                                              {
                                                  cancel.CancelAfter(_time);
                                                  if (length <= 32 * 1024)
                                                  {
                                                      flag = await client.GetRangeContent(partUrl, file, (int)file.Position, null, cancel.Token);
                                                  }
                                                  else
                                                  {
                                                      var partLength = length / parallelStreams.Length;
                                                      var tasks = new List<Task>();

                                                      for (int i = 0, j = parallelStreams.Length - 1; i <= j; i++)
                                                      {
                                                          var pIndex = i;
                                                          var stream = parallelStreams[i];
                                                          stream.Position = 0;
                                                          stream.SetLength(0);
                                                          partFlags[i] = false;
                                                          int start = partLength * i + (int)file.Position, end = (i == j ? length + (int)file.Position : start + partLength) - 1;
                                                          tasks.Add(Task.Run(async () =>
                                                          {
                                                              while (flag)
                                                              {
                                                                  var _ = await client.GetRangeContent(partUrl, stream, start + (int)stream.Position, end, cancel.Token);
                                                                  if (_)
                                                                  {
                                                                      partFlags[pIndex] = true;
                                                                      return;
                                                                  }
                                                                  if (cancel.IsCancellationRequested)
                                                                  {
                                                                      flag = false;
                                                                      return;
                                                                  }
                                                                  await Task.Delay(100);
                                                              }
                                                          }));
                                                      }
                                                      await Task.WhenAll(tasks);

                                                      for (int i = 0; i < parallelStreams.Length; i++)
                                                      {
                                                          var output = parallelStreams[i];
                                                          if (partFlags[i] || (i == 0 && output.Length > 0))
                                                          {
                                                              output.Position = 0;
                                                              await output.CopyToAsync(file);
                                                          }
                                                          else
                                                          {
                                                              break;
                                                          }
                                                      }
                                                  }
                                              }
                                          }
                                          if (flag)
                                          {
                                              parts[local] = partFile;
                                              Console.WriteLine("download {0} success", Interlocked.Increment(ref successSize));
                                          }
                                          else if (downPartQueue.Count > _parallelSize)
                                          {
                                              success = false;
                                              Console.WriteLine("download {0} fail", part);
                                          }
                                          else
                                          {
                                              downPartQueue.Enqueue(local);
                                          }
                                      }
                                  }
                                  foreach (var stream in parallelStreams)
                                  {
                                      stream.Dispose();
                                  }
                              });

                            await Task.WhenAll(downTasks);
                        }

                        if (!success)
                        {
                            downPartQueue.Clear();
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
                        urls.Enqueue(item);
                    }
                }

                if (!pages.TryPop(out var page) && !starts.TryDequeue(out page))
                {
                    if (!deep)
                    {
                        LoadVideoItems(visitedUrls, urls);
                        if (urls.Count > 0)
                        {
                            continue;
                        }
                    }
                    break;
                }

                if (filter.Contains(page))
                {
                    continue;
                }
                Console.WriteLine(page);
                var parentUrl = page.Substring(0, page.LastIndexOf('/') + 1);
                var listHtml = (await client.GetStringOrNullAsync(page)).Item2;
                if (string.IsNullOrEmpty(listHtml))
                {
                    continue;
                }
                var pageMatches = Regex.Matches(listHtml, @"""#(\d+)""", RegexOptions.Compiled);
                foreach (var pageMatch in pageMatches.Select(m => m.Groups[1].Value).Distinct())
                {
                    pages.Push(parentUrl + pageMatch);
                }

                var matches = Regex.Matches(listHtml, @"href=""(/prof-video-click/.+?)""", RegexOptions.Compiled);
                var pageItems = matches.Where(m => m.Success)
                    .Select(m => m.Groups[1].Value)
                    .Distinct();
                foreach (var pageItem in pageItems)
                {
                    urls.Enqueue(new Link() { Url = pageItem });
                }
                File.AppendAllLines(_startItemPath, pageItems);
                filter.Add(page);
                var subIndex = page.IndexOf("/videos/best/");
                if (subIndex < 0)
                {
                    subIndex = page.IndexOf("/favorites/");
                }
                if (subIndex > -1)
                {
                    page = page.Substring(0, subIndex);
                }
                File.AppendAllLines(_startPathHistory, new[] { page });
            }

            Console.WriteLine("over");
        }

        private static void LoadVideoItems(HashSet<uint> visitedUrls, Queue<Link> urls)
        {
            if (!File.Exists(_startItemPath))
            {
                return;
            }
            foreach (var url in File.ReadLines(_startItemPath).Where(url => Utils.IsNotNullOrWhiteSpace(url) && !url.Contains("THUMBNUM")).Select(url =>
            {
                if (url.StartsWith(baseHost) || url.StartsWith('/'))
                {
                    return new Link() { Url = url };
                }
                return new Link() { Url = url.Substring(2), Depth = url[0].ToByte() };
            }).GroupBy(l => l.Url).Where(g =>
            {
                var id = Convert(g.Key);
                return !visitedUrls.Contains(id);
            }))
            {
                urls.Enqueue(url.OrderBy(it => it.Depth).First());
            }
            File.WriteAllLines(_startItemPath, urls.Select(url => url.Depth == 0 ? url.Url : $"{url.Depth}|{url.Url}"));
        }
    }
}