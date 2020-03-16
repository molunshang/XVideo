using NLog;
using System.Diagnostics.Tracing;

namespace AsyncNet
{
    public class HttpEventListener : EventListener
    {
        const string _eventSource = "Microsoft-System-Net-Http";
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            base.OnEventSourceCreated(eventSource);
            if (eventSource.Name == _eventSource)
            {
                EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.All);
            }
        }
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            base.OnEventWritten(eventData);
            logger.Debug("Event:{0},Payload:{1}", eventData.EventName, string.Join(',', eventData.Payload));
        }
        private static HttpEventListener eventListener;
        public static void Init()
        {
            if (eventListener == null)
            {
                eventListener = new HttpEventListener();
            }
        }
    }
}