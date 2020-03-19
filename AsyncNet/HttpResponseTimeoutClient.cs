using NLog;
using System;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncNet
{
    public class HttpResponseTimeoutClient : HttpClient
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(1);
        private static readonly FieldInfo CtsField = typeof(CancellationToken).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public TimeSpan ResponseTimeout { get; set; }
        public HttpResponseTimeoutClient()
        {
            ResponseTimeout = DefaultTimeout;
        }

        public HttpResponseTimeoutClient(HttpMessageHandler handler) : base(handler)
        {
            ResponseTimeout = DefaultTimeout;
        }


        public override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ResponseTimeout >= base.Timeout)
            {
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return await Task.FromCanceled<HttpResponseMessage>(cancellationToken).ConfigureAwait(false);
            }
            if (cancellationToken.CanBeCanceled)
            {
                TimerCallback timerCallback = obj =>
                  {
                      logger.Debug("read response time out");
                      using ((IDisposable)obj)
                      {
                          lock (obj)
                          {
                              var cts = (CancellationTokenSource)CtsField.GetValue(cancellationToken);
                              if (cts.IsCancellationRequested)
                              {
                                  return;
                              }
                              cts.Cancel();
                          }
                      }
                  };
                using (var connectTimer = new Timer(timerCallback))
                {
                    connectTimer.Change(ResponseTimeout, System.Threading.Timeout.InfiniteTimeSpan);
                    return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
            }
            using (var cts = new CancellationTokenSource(ResponseTimeout))
            {
                cts.Token.Register(() => { logger.Debug("read response time out"); });
                return await base.SendAsync(request, cts.Token).ConfigureAwait(false);
            }
        }
    }
}