using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using vatsys;

namespace vatSys.RDF
{
    /// <summary>Immutable view of what should be highlighted right now. Read from the render thread.</summary>
    internal sealed class RdfSnapshot
    {
        public static readonly RdfSnapshot Empty = new RdfSnapshot(new HashSet<string>(StringComparer.OrdinalIgnoreCase), false, false);

        public RdfSnapshot(HashSet<string> callsigns, bool concurrent, bool live)
        {
            Callsigns = callsigns;
            Concurrent = concurrent;
            Live = live;
        }

        /// <summary>Callsigns to highlight.</summary>
        public HashSet<string> Callsigns { get; }

        /// <summary>More than one station in the highlighted set (simultaneous transmissions).</summary>
        public bool Concurrent { get; }

        /// <summary>True while someone is actually transmitting, false when showing the last transmission.</summary>
        public bool Live { get; }
    }

    /// <summary>The most recent transmission that started on a received frequency (AFV "Last Received").</summary>
    internal sealed class LastTransmission
    {
        public LastTransmission(IReadOnlyList<string> callsigns, uint frequency, DateTime startedUtc)
        {
            Callsigns = callsigns;
            Frequency = frequency;
            StartedUtc = startedUtc;
        }

        public IReadOnlyList<string> Callsigns { get; }

        /// <summary>Frequency in Hz, 0 when unknown (test mode).</summary>
        public uint Frequency { get; }

        public DateTime StartedUtc { get; }
    }

    /// <summary>
    /// Polls vatSys' built-in AFV client (VSCS frequencies) for receiving callsigns and
    /// keeps track of the current and the most recent transmitting stations.
    /// </summary>
    internal sealed class RdfState : IDisposable
    {
        private const int PollIntervalMs = 50;

        private readonly Func<RdfSettings> getSettings;
        private readonly System.Threading.Timer timer;
        private readonly object sync = new object();
        private readonly int processId = Process.GetCurrentProcess().Id;

        private HashSet<string> lastActive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> previous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DateTime previousEndedUtc = DateTime.MinValue;

        private string testCallsign;
        private DateTime testUntilUtc;

        private volatile RdfSnapshot current = RdfSnapshot.Empty;
        private volatile LastTransmission lastTransmission;

        public RdfState(Func<RdfSettings> getSettings)
        {
            this.getSettings = getSettings;
            timer = new System.Threading.Timer(_ => Poll(), null, PollIntervalMs, PollIntervalMs);
        }

        public RdfSnapshot Current => current;

        public LastTransmission Last => lastTransmission;

        /// <summary>Raised (on a worker thread) when the highlight set or the last transmission changes.</summary>
        public event EventHandler Changed;

        /// <summary>Highlights the given callsign for a few seconds, as if it was transmitting.</summary>
        public void Test(string callsign, TimeSpan duration)
        {
            lock (sync)
            {
                testCallsign = callsign;
                testUntilUtc = DateTime.UtcNow + duration;
            }
        }

        private void Poll()
        {
            if (!Monitor.TryEnter(sync))
                return;
            try
            {
                var settings = getSettings();
                var now = DateTime.UtcNow;
                var receiving = GetReceivingCallsigns();

                if (testCallsign != null)
                {
                    if (now < testUntilUtc)
                    {
                        if (!receiving.ContainsKey(testCallsign))
                            receiving[testCallsign] = 0;
                    }
                    else
                        testCallsign = null;
                }

                var active = new HashSet<string>(receiving.Keys, StringComparer.OrdinalIgnoreCase);

                // Same rule as the AFV client: a station that starts transmitting becomes the last received station.
                bool lastChanged = false;
                var started = receiving.Where(kv => !lastActive.Contains(kv.Key)).ToList();
                if (started.Count > 0)
                {
                    lastTransmission = new LastTransmission(started.Select(kv => kv.Key).ToArray(), started[0].Value, now);
                    lastChanged = true;
                }

                RdfSnapshot next;
                if (active.Count > 0)
                {
                    previous = active;
                    previousEndedUtc = DateTime.MaxValue;
                    next = new RdfSnapshot(active, active.Count > 1, live: true);
                }
                else
                {
                    if (lastActive.Count > 0)
                        previousEndedUtc = now;

                    bool show = previous.Count > 0 &&
                        (IsHoldKeyDown(settings.HoldKey) ||
                         (settings.PersistSeconds > 0 && (now - previousEndedUtc).TotalSeconds < settings.PersistSeconds));

                    next = show ? new RdfSnapshot(previous, previous.Count > 1, live: false) : RdfSnapshot.Empty;
                }
                lastActive = active;

                var old = current;
                bool snapshotChanged = !SameAs(old, next);
                if (snapshotChanged)
                    current = next;
                if (snapshotChanged || lastChanged)
                    Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // Never let the poller die; vatSys may be (re)building its frequency list.
            }
            finally
            {
                Monitor.Exit(sync);
            }
        }

        /// <summary>Callsign -> frequency (Hz) of every station currently heard on an RX-enabled frequency.</summary>
        private static Dictionary<string, uint> GetReceivingCallsigns()
        {
            var set = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            var frequencies = Audio.VSCSFrequencies;
            if (frequencies == null)
                return set;

            foreach (var f in frequencies.ToArray())
            {
                if (f == null || !f.Receive)
                    continue;
                var callsigns = f.ReceivingCallsigns;
                if (callsigns == null)
                    continue;
                foreach (var cs in callsigns.ToArray())
                {
                    if (!string.IsNullOrWhiteSpace(cs) && !set.ContainsKey(cs.Trim()))
                        set.Add(cs.Trim(), f.Frequency);
                }
            }
            return set;
        }

        private static bool SameAs(RdfSnapshot a, RdfSnapshot b) =>
            a.Live == b.Live && a.Concurrent == b.Concurrent && a.Callsigns.SetEquals(b.Callsigns);

        private bool IsHoldKeyDown(Keys key)
        {
            if (key == Keys.None)
                return false;
            if ((GetAsyncKeyState((int)key) & 0x8000) == 0)
                return false;
            // Only react while vatSys is the foreground application.
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            return pid == processId;
        }

        public void Dispose() => timer.Dispose();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
    }
}
