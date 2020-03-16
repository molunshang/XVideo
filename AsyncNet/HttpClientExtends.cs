using System;
using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncNet
{
    public static class HttpClientExtends
    {
        public static async Task<string> GetStringOrNullAsync(this HttpClient client, string url,
            Encoding encoding = null)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead))
                    {
                        if (!response.IsSuccessStatusCode || response.Content == null)
                        {
                            return string.Empty;
                        }

                        if (encoding == null)
                        {
                            return await response.Content.ReadAsStringAsync();
                        }

                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        return encoding.GetString(bytes);
                    }
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public static async ValueTask<int> HeadContentLength(this HttpClient client, string url)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Head, url))
                {
                    using (var response = await client.SendAsync(request))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            return (int)response.Content.Headers.ContentLength.GetValueOrDefault(0);
                        }

                        return -1;
                    }
                }
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public static async Task<bool> GetRangeContent(this HttpClient client, string url, Stream output,
            int? start, int? end, CancellationToken token)
        {
            try
            {
                int? length = null;
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (start.HasValue && end.HasValue)
                    {
                        request.Headers.Add("Range", $"bytes={start}-{end}");
                        length = end - start + 1;
                    }
                    else if (start.HasValue)
                    {
                        request.Headers.Add("Range", $"bytes={start}-");
                    }
                    else if (end.HasValue)
                    {
                        request.Headers.Add("Range", $"bytes=0-{end}");
                        length = end + 1;
                    }

                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            Console.WriteLine(response.ReasonPhrase);
                            return response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable;
                        }
                        using (var input = await response.Content.ReadAsStreamAsync())
                        {
                            var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
                            try
                            {
                                while ((!length.HasValue) || length.Value > 0)
                                {
                                    using (var read = CancellationTokenSource.CreateLinkedTokenSource(token))
                                    {
                                        read.CancelAfter(15000);
                                        var num = await input.ReadAsync(buffer, 0, buffer.Length, read.Token);
                                        if (num <= 0)
                                        {
                                            break;
                                        }

                                        await output.WriteAsync(buffer, 0, num);
                                        if (length.HasValue)
                                        {
                                            length -= num;
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                ArrayPool<byte>.Shared.Return(buffer);
                            }

                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}