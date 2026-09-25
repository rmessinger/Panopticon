using System.Collections.Generic;
using NLog;
using NLog.Targets;

namespace Panopticon.Core;

[Target("Panopticon")]
public sealed class InternalLogger : TargetWithLayout
{
    public struct Message
    {
        public string Content { get; set; }
        public LogLevel Level { get; set; }
    }

    public List<Message> Messages { get; set; }

    public InternalLogger()
    {
        Messages = new List<Message>();
    }

    protected override void Write(LogEventInfo logEvent)
    {
        string logMessage = this.Layout.Render(logEvent);

        Messages.Add(new Message()
        {
            Content = logMessage,
            Level = logEvent.Level
        });
    }
}
