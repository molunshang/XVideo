using System;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncNet
{
    public class HttpConnetctTimeoutClient : HttpClient
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(1);
        private static readonly FieldInfo CtsField = typeof(CancellationToken).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic);

        public TimeSpan ConnectTimeout { get; set; }
        public HttpConnetctTimeoutClient()
        {
            ConnectTimeout = DefaultTimeout;
        }

        public HttpConnetctTimeoutClient(HttpMessageHandler handler) : base(handler)
        {
            ConnectTimeout = DefaultTimeout;
        }


        public override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ConnectTimeout <= base.Timeout)
            {
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            if (cancellationToken.IsCancellationRequested)
            {
                throw new TaskCanceledException("then token has been canceled", null, cancellationToken);
            }
            TimerCallback timerCallback;
            var uri = request.RequestUri.PathAndQuery;
            var complete = false;
            if (cancellationToken.CanBeCanceled)
            {
                var flag = 0;
                timerCallback = obj =>
                 {
                     using ((IDisposable)obj)
                     {
                         if (complete)
                         {
                             return;
                         }
                         if (Interlocked.CompareExchange(ref flag, 1, 0) != 0)
                         {
                             return;
                         }
                         var cts = (CancellationTokenSource)CtsField.GetValue(cancellationToken);
                         if (cts.IsCancellationRequested)
                         {
                             return;
                         }
                         cts.Cancel();
                     }
                 };
            }
            else
            {
                var cts = new CancellationTokenSource();
                timerCallback = obj =>
                {
                    using ((IDisposable)obj)
                    {
                        using (cts)
                        {
                            if (complete)
                            {
                                return;
                            }
                            cts.Cancel();
                        }
                    }
                };
                cancellationToken = cts.Token;
            }

            using (var connectTimer = new Timer(timerCallback))
            {
                connectTimer.Change(ConnectTimeout, System.Threading.Timeout.InfiniteTimeSpan);
                var result = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                complete = true;
                return result;
            }
        }
    }
}