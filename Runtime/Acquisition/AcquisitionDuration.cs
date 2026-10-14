using System;

namespace Pludibase
{
    /// <summary>Monotonic elapsed seconds mapped to a UTC anchor, with at most 60 seconds per event.</summary>
    internal sealed class AcquisitionDuration
    {
        private DateTime startedAt;
        private double seconds;
        private readonly Action<DateTime, DateTime, double> emit;

        internal AcquisitionDuration(DateTime start, Action<DateTime, DateTime, double> emit)
        {
            startedAt = start.ToUniversalTime();
            this.emit = emit;
        }

        internal void Add(double elapsed)
        {
            if (double.IsNaN(elapsed) || double.IsInfinity(elapsed) || elapsed < 0) throw new ArgumentOutOfRangeException("elapsed");
            while (elapsed > 0)
            {
                double limit = Math.Min(60, (startedAt.Date.AddDays(1) - startedAt).TotalSeconds);
                double part = Math.Min(elapsed, Math.Max(0, limit - seconds));
                seconds += part;
                elapsed -= part;
                if (seconds >= limit - 0.00000005) Flush();
                else break;
            }
        }

        internal void Flush()
        {
            if (seconds <= 0) return;
            DateTime end = startedAt.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
            // Reset after durable enqueue succeeds; a storage error must not discard an interval.
            emit(startedAt, end, seconds);
            startedAt = end;
            seconds = 0;
        }
    }
}
