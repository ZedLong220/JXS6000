using System.Collections.Generic;
using System.Text;

namespace SmallProject.Serials.Slcan
{
    public readonly record struct SlcanReceiveEvent(bool IsAcknowledgement, bool Accepted, string? Frame);

    public sealed class SlcanReceiveDecoder
    {
        private readonly StringBuilder frameBuffer = new StringBuilder();

        public IReadOnlyList<SlcanReceiveEvent> Append(string input)
        {
            var events = new List<SlcanReceiveEvent>();
            foreach (char value in input)
            {
                if (value == '\a')
                {
                    events.Add(new SlcanReceiveEvent(true, false, null));
                }
                else if (value == '\r')
                {
                    if (frameBuffer.Length == 0)
                        events.Add(new SlcanReceiveEvent(true, true, null));
                    else
                    {
                        events.Add(new SlcanReceiveEvent(false, false, frameBuffer.ToString()));
                        frameBuffer.Clear();
                    }
                }
                else if (value != '\n')
                {
                    frameBuffer.Append(value);
                }
            }
            return events;
        }

        public void Reset() => frameBuffer.Clear();
    }
}
