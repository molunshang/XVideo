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
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            return (int) response.Content.Headers.ContentLength.GetValueOrDefault(0);
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
            int start, int end, CancellationToken token)
        {
            try
            {
                if (start > end || end <= 0)
                {
                    return false;
                }

                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Add("Range", start <= 0 ? $"bytes=0-{end}" : $"bytes={start}-{end}");

                    using (var response =
                        await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            Console.WriteLine(response.ReasonPhrase);
                            return false;
                        }

                        using (var input = await response.Content.ReadAsStreamAsync())
                        {
                            var length = end - start;
                            var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(length, 32 * 1024));
                            try
                            {
                                while (length > 0)
                                {
                                    using (var singleToken = new CancellationTokenSource(10000))
                                    {
                                        using (var readToken = token == CancellationToken.None
                                            ? singleToken
                                            : CancellationTokenSource.CreateLinkedTokenSource(token,
                                                singleToken.Token))
                                        {
                                            var num = await input.ReadAsync(buffer, 0, buffer.Length,
                                                readToken.Token);
                                            if (num <= 0)
                                            {
                                                break;
                                            }

                                            await output.WriteAsync(buffer, 0, num);
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