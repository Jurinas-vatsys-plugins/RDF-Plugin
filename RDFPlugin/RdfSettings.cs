using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using System.Xml.Linq;

namespace vatSys.RDF
{
    internal enum OffscreenMode
    {
        None,
        /// <summary>Line from the display centre towards the aircraft (original RDF behaviour).</summary>
        LineFromCentre,
        /// <summary>Short pointer at the display edge.</summary>
        EdgePointer
    }

    internal sealed class RdfSettings
    {
        public const string FileName = "RDFPlugin.xml";

        public bool Enabled = true;
        public Color Colour = Color.White;
        public Color ConcurrentColour = Color.Red;
        public int Radius = 40;
        public float LineWidth = 2f;
        public OffscreenMode Offscreen = OffscreenMode.LineFromCentre;
        public bool OffscreenLabel = true;
        /// <summary>Key/mouse button that, while held, shows the last transmitting aircraft.</summary>
        public Keys HoldKey = Keys.None;
        /// <summary>How long (ms) the recall key must be held before the last transmission is shown.</summary>
        public int RecallDelayMs = 400;
        /// <summary>Seconds the last transmission stays highlighted after it ends (0 = only while transmitting).</summary>
        public double PersistSeconds = 0;
        /// <summary>Also recolour the track symbol and label of transmitting aircraft.</summary>
        public bool HighlightTrack = false;
        public Color TrackColour = Color.White;
        /// <summary>Show the LAST TX line (last received callsigns and frequencies) at the bottom of the VSCS window.</summary>
        public bool ShowLastTx = true;
        /// <summary>Simulated VHF radio sound (filter, noise, crackle) on received AFV voice. Set from the vatSys Audio Settings window.</summary>
        public bool VhfEffects = true;

        public string LoadedFrom { get; private set; }

        public RdfSettings Clone() => (RdfSettings)MemberwiseClone();

        /// <summary>Settings file next to the plugin DLL (works for both bin\Plugins and Profile\Plugins).</summary>
        private static string PrimaryPath => PluginFolderPath(FileName);

        /// <summary>Used when the plugin folder is not writable.</summary>
        private static string FallbackPath => DocumentsPath(FileName);

        internal static string PluginFolderPath(string fileName) =>
            Path.Combine(Path.GetDirectoryName(typeof(RdfSettings).Assembly.Location) ?? ".", fileName);

        internal static string DocumentsPath(string fileName) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "vatSys Files", fileName);

        public static RdfSettings Load()
        {
            var s = new RdfSettings();
            foreach (var path in new[] { PrimaryPath, FallbackPath })
            {
                if (!File.Exists(path))
                    continue;
                try
                {
                    s.Read(XDocument.Load(path).Root);
                    s.LoadedFrom = path;
                    return s;
                }
                catch (Exception)
                {
                    // fall through to defaults / next file
                }
            }
            s.LoadedFrom = PrimaryPath;
            return s;
        }

        public void Save()
        {
            var doc = new XDocument(Write());
            try
            {
                doc.Save(PrimaryPath);
                LoadedFrom = PrimaryPath;
            }
            catch (Exception) when (TrySaveFallback(doc))
            {
            }
        }

        private bool TrySaveFallback(XDocument doc)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FallbackPath));
                doc.Save(FallbackPath);
                LoadedFrom = FallbackPath;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private XElement Write() => new XElement("RDF",
            new XElement(nameof(Enabled), Enabled),
            new XElement(nameof(Colour), ToHex(Colour)),
            new XElement(nameof(ConcurrentColour), ToHex(ConcurrentColour)),
            new XElement(nameof(Radius), Radius),
            new XElement(nameof(LineWidth), LineWidth.ToString(CultureInfo.InvariantCulture)),
            new XElement(nameof(Offscreen), Offscreen),
            new XElement(nameof(OffscreenLabel), OffscreenLabel),
            new XElement(nameof(HoldKey), HoldKey),
            new XElement(nameof(RecallDelayMs), RecallDelayMs),
            new XElement(nameof(PersistSeconds), PersistSeconds.ToString(CultureInfo.InvariantCulture)),
            new XElement(nameof(HighlightTrack), HighlightTrack),
            new XElement(nameof(TrackColour), ToHex(TrackColour)),
            new XElement(nameof(ShowLastTx), ShowLastTx),
            new XElement(nameof(VhfEffects), VhfEffects));

        private void Read(XElement root)
        {
            if (root == null)
                return;
            string V(string n) => root.Element(n)?.Value?.Trim();

            if (bool.TryParse(V(nameof(Enabled)), out var b)) Enabled = b;
            Colour = ParseColour(V(nameof(Colour)), Colour);
            ConcurrentColour = ParseColour(V(nameof(ConcurrentColour)), ConcurrentColour);
            if (int.TryParse(V(nameof(Radius)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) && r > 0) Radius = r;
            if (float.TryParse(V(nameof(LineWidth)), NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w > 0) LineWidth = w;
            if (Enum.TryParse(V(nameof(Offscreen)), true, out OffscreenMode o)) Offscreen = o;
            if (bool.TryParse(V(nameof(OffscreenLabel)), out b)) OffscreenLabel = b;
            if (Enum.TryParse(V(nameof(HoldKey)), true, out Keys k)) HoldKey = k;
            if (int.TryParse(V(nameof(RecallDelayMs)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) && d >= 0) RecallDelayMs = Math.Min(d, 5000);
            if (double.TryParse(V(nameof(PersistSeconds)), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && p >= 0) PersistSeconds = p;
            if (bool.TryParse(V(nameof(HighlightTrack)), out b)) HighlightTrack = b;
            TrackColour = ParseColour(V(nameof(TrackColour)), TrackColour);
            if (bool.TryParse(V(nameof(ShowLastTx)), out b)) ShowLastTx = b;
            if (bool.TryParse(V(nameof(VhfEffects)), out b)) VhfEffects = b;
        }

        public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        /// <summary>Accepts "#RRGGBB" or "R:G:B" (the EuroScope RDF format).</summary>
        public static Color ParseColour(string value, Color fallback)
        {
            if (string.IsNullOrEmpty(value))
                return fallback;
            try
            {
                var parts = value.Split(':', ',');
                if (parts.Length == 3)
                    return Color.FromArgb(Clamp(parts[0]), Clamp(parts[1]), Clamp(parts[2]));
                var c = ColorTranslator.FromHtml(value);
                return Color.FromArgb(c.R, c.G, c.B);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static int Clamp(string s) => Math.Max(0, Math.Min(255, int.Parse(s.Trim(), CultureInfo.InvariantCulture)));
    }
}
