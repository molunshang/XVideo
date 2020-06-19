using NLog;
using System;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncNet
{

    public class HttpResponseTimeoutHandler : DelegatingHandler
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(1);
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public TimeSpan ResponseTimeout { get; set; }

        public HttpResponseTimeoutHandler(HttpMessageHandler handler) : base(handler)
        {
            ResponseTimeout = DefaultTimeout;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return await Task.FromCanceled<HttpResponseMessage>(cancellationToken).ConfigureAwait(false);
            }
            CancellationTokenSource cts;
            if (cancellationToken.CanBeCanceled)
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }
            else
            {
                cts = new CancellationTokenSource();
            }
            using (cts)
            {
                cts.CancelAfter(ResponseTimeout);
                cts.Token.Register(() => { logger.Debug("read response time out"); });
                return await base.SendAsync(request, cts.Token).ConfigureAwait(false);
            }
        }
    }
}