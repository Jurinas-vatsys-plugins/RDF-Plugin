using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using vatsys;

namespace vatSys.RDF
{
    /// <summary>
    /// Shows "LAST TX  CALLSIGN 124.400  CALLSIGN 128.000 ..." as one line of text at the bottom of every
    /// VSCS window, in the free strip next to the page scroll bar, so the frequency buttons are not touched.
    /// </summary>
    internal sealed class LastTxLineManager
    {
        private const string VscsWindowType = "vatsys.VSCSWindow";

        private readonly Func<RdfSettings> getSettings;
        private readonly RdfState state;
        private readonly List<LastTxLine> lines = new List<LastTxLine>();
        private readonly Timer attachTimer;

        public LastTxLineManager(Func<RdfSettings> getSettings, RdfState state)
        {
            this.getSettings = getSettings;
            this.state = state;

            // vatSys can open (or recreate) the VSCS window at any time; look for new ones once a second.
            attachTimer = new Timer { Interval = 1000 };
            attachTimer.Tick += (s, e) => AttachToWindows();
            attachTimer.Start();
            AttachToWindows();

            state.Changed += (s, e) => MMI.InvokeOnGUI(Refresh);
        }

        /// <summary>Updates the text of every LAST TX line. Must run on the GUI thread.</summary>
        public void Refresh()
        {
            lines.RemoveAll(l => l.IsDisposed);
            var settings = getSettings();
            foreach (var line in lines)
                line.Update(settings.ShowLastTx, state.Last, state.Current);
        }

        private void AttachToWindows()
        {
            try
            {
                foreach (Form form in Application.OpenForms)
                {
                    if (form.GetType().FullName != VscsWindowType || form.IsDisposed)
                        continue;
                    if (lines.Any(l => !l.IsDisposed && l.Parent == form))
                        continue;
                    var line = new LastTxLine(form);
                    lines.Add(line);
                    line.Update(getSettings().ShowLastTx, state.Last, state.Current);
                }
            }
            catch (InvalidOperationException)
            {
                // OpenForms changed while enumerating; try again on the next tick.
            }
        }
    }

    /// <summary>One line of text added to a VSCS window.</summary>
    internal sealed class LastTxLine : Control
    {
        private const string Title = "LAST TX";
        private const string ShortTitle = "TX";
        private const int Gap = 12;

        private readonly Form window;
        private readonly Control scrollBar;
        private LastTransmission last;
        private RdfSnapshot snapshot = RdfSnapshot.Empty;

        public LastTxLine(Form window)
        {
            this.window = window;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Font = MMI.eurofont_winsml;
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

            // The page scroll bar (VATSYSControls.ScrollBar) shares the bottom strip; use the space left of it when shown.
            scrollBar = window.Controls.Cast<Control>().FirstOrDefault(c => c is VATSYSControls.ScrollBar);
            if (scrollBar != null)
            {
                scrollBar.VisibleChanged += (s, e) => Relayout();
                scrollBar.LocationChanged += (s, e) => Relayout();
            }
            window.Resize += (s, e) => Relayout();

            window.Controls.Add(this);
            BringToFront();
            Relayout();
        }

        public void Update(bool show, LastTransmission lastTransmission, RdfSnapshot current)
        {
            Visible = show;
            last = lastTransmission;
            snapshot = current ?? RdfSnapshot.Empty;
            Invalidate();
        }

        private void Relayout()
        {
            if (IsDisposed || window.IsDisposed)
                return;
            int height = Math.Max(20, Font.Height + 4);
            int left = 3;
            int right = scrollBar != null && scrollBar.Visible ? scrollBar.Left - 4 : window.ClientSize.Width - 3;
            // The strip below the VSCS buttons is 28 px high; centre the line in it.
            int top = window.ClientSize.Height - 28 + (28 - height) / 2;
            Bounds = new Rectangle(left, top, Math.Max(0, right - left), height);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var back = Colours.GetColour(Colours.Identities.VSCSBackground);
            var text = Colours.GetColour(Colours.Identities.InteractiveText);
            var dim = Colours.GetColour(Colours.Identities.NonInteractiveText);
            var receiving = Colours.GetColour(Colours.Identities.VSCSReceivingFrequency);

            using (var b = new SolidBrush(back))
                g.FillRectangle(b, ClientRectangle);

            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            int width = ClientSize.Width;
            int height = ClientSize.Height;
            int Measure(string t) => TextRenderer.MeasureText(g, t, Font, Size.Empty, flags).Width;

            var segments = BuildSegments(last);
            var labels = segments.Select(sg => sg.Callsign + " " + sg.Frequency).ToList();
            var widths = labels.Select(Measure).ToList();
            int MoreWidth(int hidden) => hidden > 0 ? Gap + Measure("+" + hidden) : 0;

            // Use the short title when the first speaker (plus "+N" for the others) would not fit next to the long one.
            string title = Title;
            if (labels.Count > 0 && Measure(Title) + Gap + widths[0] + MoreWidth(labels.Count - 1) > width)
                title = ShortTitle;
            int x = Measure(title);
            TextRenderer.DrawText(g, title, Font, new Rectangle(0, 0, x, height), dim, flags);
            x += Gap;

            if (labels.Count == 0)
            {
                TextRenderer.DrawText(g, "-", Font, new Rectangle(x, 0, Math.Max(0, width - x), height), text, flags);
                return;
            }

            // Everything fits: draw all. Otherwise draw as many as fit while leaving room for "+N",
            // but always at least the first speaker (cut with an ellipsis if even that is too long).
            int shown = labels.Count;
            if (x + widths.Sum() + Gap * (labels.Count - 1) > width)
            {
                shown = 1;
                int cx = x + widths[0] + Gap;
                while (shown < labels.Count && cx + widths[shown] + MoreWidth(labels.Count - shown - 1) <= width)
                {
                    cx += widths[shown] + Gap;
                    shown++;
                }
            }

            int moreWidth = MoreWidth(labels.Count - shown);
            for (int i = 0; i < shown; i++)
            {
                int w = i == 0 ? Math.Min(widths[0], Math.Max(0, width - x - moreWidth)) : widths[i];
                // A station that is still talking is backed with the VSCS "receiving" colour, like its frequency button.
                if (snapshot.Live && snapshot.Callsigns.Contains(segments[i].Callsign))
                {
                    using (var b = new SolidBrush(receiving))
                        g.FillRectangle(b, x - 3, 1, w + 6, height - 2);
                }
                TextRenderer.DrawText(g, labels[i], Font, new Rectangle(x, 0, w, height), text, flags | TextFormatFlags.EndEllipsis);
                x += w + Gap;
            }
            if (shown < labels.Count)
                TextRenderer.DrawText(g, "+" + (labels.Count - shown), Font, new Rectangle(x, 0, Math.Max(0, width - x), height), text, flags);
        }

        private static List<(string Callsign, string Frequency)> BuildSegments(LastTransmission last)
        {
            var result = new List<(string, string)>();
            if (last == null)
                return result;
            foreach (var sp in last.Speakers)
                result.Add((sp.Callsign, FormatFrequency(sp.Frequency)));
            return result;
        }

        internal static string FormatFrequency(uint hz) =>
            hz == 0 ? "---.---" : (hz / 1000000.0).ToString("000.000", System.Globalization.CultureInfo.InvariantCulture);
    }
}
