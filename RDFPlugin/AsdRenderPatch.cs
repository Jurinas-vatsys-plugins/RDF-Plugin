using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using vatsys;

namespace vatSys.RDF
{
    /// <summary>
    /// vatSys has no public drawing API for plugins, so the RDF graphics are drawn by a Harmony
    /// postfix on ASDControlDX.PaintTracksLabels. That runs on the ASD render thread, inside
    /// BeginDraw/EndDraw, with the device context transform centred on the display.
    /// Applies to every ASD window and the ASMGCS (ground) display.
    /// </summary>
    internal static class AsdRenderPatch
    {
        private const string HarmonyId = "vatsys.rdfplugin";
        private const float EdgeMargin = 12f;

        private static Func<RdfSettings> getSettings;
        private static Func<RdfSnapshot> getSnapshot;

        // ASDControlDX and RenderParams are internal to vatSys.exe, so they are accessed through compiled accessors.
        private static Func<object, DeviceContext1> dcRef;
        private static Func<object, TextFormat> fontRef;
        private static Func<object, Coordinate, object, Vector2> llToScreen;
        private static Func<object, Size2> clientSize;
        private static Func<SharpDX.DirectWrite.Factory> textFactory;

        private static readonly ConditionalWeakTable<DeviceContext1, SolidColorBrush> brushes =
            new ConditionalWeakTable<DeviceContext1, SolidColorBrush>();

        private static bool errorReported;

        public static void Install(Func<RdfSettings> settings, Func<RdfSnapshot> snapshot)
        {
            getSettings = settings;
            getSnapshot = snapshot;

            var vatsysAssembly = typeof(MMI).Assembly;
            var asd = vatsysAssembly.GetType("vatsys.ASDControlDX", throwOnError: true);
            var renderParamsType = vatsysAssembly.GetType("vatsys.RenderParams", throwOnError: true);

            dcRef = FieldGetter<DeviceContext1>(asd, "dc");
            fontRef = FieldGetter<TextFormat>(asd, "fontFormat");

            var factoryProp = AccessTools.Property(asd, "FactoryText")
                ?? throw new MissingMemberException(asd.FullName, "FactoryText");
            textFactory = Expression.Lambda<Func<SharpDX.DirectWrite.Factory>>(Expression.Property(null, factoryProp)).Compile();

            var convert = AccessTools.Method(asd, "ConvertLLToScreen", new[] { typeof(Coordinate), renderParamsType })
                ?? throw new MissingMethodException(asd.FullName, "ConvertLLToScreen(Coordinate, RenderParams)");
            var pAsd = Expression.Parameter(typeof(object));
            var pLL = Expression.Parameter(typeof(Coordinate));
            var pRp = Expression.Parameter(typeof(object));
            llToScreen = Expression.Lambda<Func<object, Coordinate, object, Vector2>>(
                Expression.Call(Expression.Convert(pAsd, asd), convert, pLL, Expression.Convert(pRp, renderParamsType)), pAsd, pLL, pRp).Compile();

            var sizeProp = AccessTools.Property(renderParamsType, "ClientSize")
                ?? throw new MissingMemberException(renderParamsType.FullName, "ClientSize");
            var pObj = Expression.Parameter(typeof(object));
            clientSize = Expression.Lambda<Func<object, Size2>>(
                Expression.Property(Expression.Convert(pObj, renderParamsType), sizeProp), pObj).Compile();

            var target = AccessTools.Method(asd, "PaintTracksLabels")
                ?? throw new MissingMethodException(asd.FullName, "PaintTracksLabels");

            var harmony = new Harmony(HarmonyId);
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(AsdRenderPatch).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
        }

        private static Func<object, T> FieldGetter<T>(Type type, string name)
        {
            var field = AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
            var p = Expression.Parameter(typeof(object));
            // Some fields (e.g. the VSCS fonts) are static; the instance argument is then ignored.
            var access = field.IsStatic ? Expression.Field(null, field) : Expression.Field(Expression.Convert(p, type), field);
            return Expression.Lambda<Func<object, T>>(access, p).Compile();
        }

        public static void Uninstall()
        {
            try
            {
                new Harmony(HarmonyId).UnpatchAll(HarmonyId);
            }
            catch (Exception)
            {
            }
        }

        // Parameter names must match the original method: PaintTracksLabels(RenderParams renderParams, IList<Track> tracks, ...)
        private static void Postfix(object __instance, object renderParams, IList<Track> tracks)
        {
            try
            {
                var settings = getSettings();
                if (!settings.Enabled)
                    return;
                var snapshot = getSnapshot();
                if (snapshot.Callsigns.Count == 0 || tracks == null || tracks.Count == 0)
                    return;

                var dc = dcRef(__instance);
                if (dc == null || dc.IsDisposed)
                    return;

                Draw(__instance, dc, renderParams, tracks, settings, snapshot);
            }
            catch (Exception ex)
            {
                if (!errorReported)
                {
                    errorReported = true;
                    Errors.Add(new Exception("RDF plugin drawing failed: " + ex.Message, ex), RdfPlugin.PluginName);
                }
            }
        }

        private static void Draw(object asd, DeviceContext1 dc, object renderParams, IList<Track> tracks, RdfSettings settings, RdfSnapshot snapshot)
        {
            var targets = FindTracks(tracks, snapshot.Callsigns);
            if (targets.Count == 0)
                return;

            var colour = snapshot.Concurrent ? settings.ConcurrentColour : settings.Colour;
            if (!brushes.TryGetValue(dc, out var brush) || brush.IsDisposed)
            {
                brushes.Remove(dc);
                brush = new SolidColorBrush(dc, ToRaw(colour));
                brushes.Add(dc, brush);
            }
            brush.Color = ToRaw(colour);

            var size = clientSize(renderParams);
            float halfW = size.Width / 2f, halfH = size.Height / 2f;
            float lineWidth = settings.LineWidth;
            float radius = settings.Radius;

            var oldMode = dc.AntialiasMode;
            dc.AntialiasMode = AntialiasMode.PerPrimitive;
            var placedLabels = new List<RectangleF>();
            try
            {
                foreach (var kv in targets)
                {
                    var p = llToScreen(asd, kv.Value.GetLocation(), renderParams);
                    if (float.IsNaN(p.X) || float.IsNaN(p.Y))
                        continue;

                    if (Math.Abs(p.X) <= halfW && Math.Abs(p.Y) <= halfH)
                    {
                        dc.DrawEllipse(new Ellipse(ToRaw(p), radius, radius), brush, lineWidth);
                    }
                    else if (settings.Offscreen != OffscreenMode.None)
                    {
                        DrawOffscreen(dc, brush, fontRef(asd), p, halfW, halfH, kv.Key, settings, placedLabels);
                    }
                }
            }
            finally
            {
                dc.AntialiasMode = oldMode;
            }
        }

        /// <summary>One track per callsign; a radar track wins over a flight plan track.</summary>
        private static Dictionary<string, Track> FindTracks(IList<Track> tracks, HashSet<string> callsigns)
        {
            var result = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tracks)
            {
                if (t == null)
                    continue;
                var cs = RdfPlugin.GetCallsign(t);
                if (cs == null || !callsigns.Contains(cs))
                    continue;
                if (!result.TryGetValue(cs, out var existing) || (existing.GetRadarTrack() == null && t.GetRadarTrack() != null))
                    result[cs] = t;
            }
            return result;
        }

        private static void DrawOffscreen(DeviceContext1 dc, SolidColorBrush brush, TextFormat font, Vector2 p, float halfW, float halfH, string callsign, RdfSettings settings, List<RectangleF> placedLabels)
        {
            // Point where the centre->aircraft line meets the (inset) display edge.
            float ex = halfW - EdgeMargin, ey = halfH - EdgeMargin;
            float scale = Math.Min(p.X == 0 ? float.MaxValue : ex / Math.Abs(p.X), p.Y == 0 ? float.MaxValue : ey / Math.Abs(p.Y));
            var edge = new Vector2(p.X * scale, p.Y * scale);
            var dir = Vector2.Normalize(edge);
            if (float.IsNaN(dir.X))
                return;

            var start = settings.Offscreen == OffscreenMode.LineFromCentre ? Vector2.Zero : edge - dir * Math.Max(60f, settings.Radius * 1.5f);
            dc.DrawLine(ToRaw(start), ToRaw(edge), brush, settings.LineWidth);

            // Arrow head
            var normal = new Vector2(-dir.Y, dir.X);
            const float head = 10f;
            dc.DrawLine(ToRaw(edge), ToRaw(edge - dir * head + normal * head * 0.6f), brush, settings.LineWidth);
            dc.DrawLine(ToRaw(edge), ToRaw(edge - dir * head - normal * head * 0.6f), brush, settings.LineWidth);

            if (!settings.OffscreenLabel || font == null || font.IsDisposed)
                return;

            using (var layout = new TextLayout(textFactory(), callsign, font, 400f, 100f))
            {
                float w = layout.Metrics.WidthIncludingTrailingWhitespace + 4f;
                float h = layout.Metrics.Height;
                // Place the text just inside the edge, offset towards the centre, and keep it on screen.
                var anchor = edge - dir * (head + 6f);
                float x = anchor.X - (dir.X > 0.3f ? w : dir.X < -0.3f ? 0f : w / 2f);
                float y = anchor.Y - (dir.Y > 0.3f ? h : dir.Y < -0.3f ? 0f : h / 2f);
                x = Math.Max(-halfW + 2f, Math.Min(halfW - w - 2f, x));
                y = Math.Max(-halfH + 2f, Math.Min(halfH - h - 2f, y));

                // Several aircraft in a similar direction: slide this label along the edge so it does not cover another.
                bool onSideEdge = Math.Abs(edge.X) >= ex - 1f;
                var placed = PlaceLabel(new RectangleF(x, y, w, h), placedLabels, onSideEdge,
                    new RectangleF(-halfW + 2f, -halfH + 2f, halfW * 2f - 4f, halfH * 2f - 4f));
                placedLabels.Add(placed);
                x = placed.X;
                y = placed.Y;
                // vatSys' font format is vertically centred, so compensate for the layout's top offset.
                dc.DrawTextLayout(new SharpDX.Mathematics.Interop.RawVector2(x - layout.Metrics.Left, y - layout.Metrics.Top), layout, brush);
            }
        }

        /// <summary>
        /// Returns the position closest to <paramref name="wanted"/> that does not overlap an already placed label,
        /// moving vertically (labels on the left/right edge) or horizontally (top/bottom edge) and staying in bounds.
        /// </summary>
        internal static RectangleF PlaceLabel(RectangleF wanted, IList<RectangleF> placed, bool moveVertically, RectangleF bounds)
        {
            float step = (moveVertically ? wanted.Height : wanted.Width) + 2f;
            for (int i = 0; i <= 2 * (placed.Count + 1); i++)
            {
                // Offsets 0, +1, -1, +2, -2 ... steps.
                int k = (i + 1) / 2 * (i % 2 == 1 ? 1 : -1);
                var candidate = wanted;
                if (moveVertically)
                    candidate.Y = Math.Max(bounds.Top, Math.Min(bounds.Bottom - wanted.Height, wanted.Y + k * step));
                else
                    candidate.X = Math.Max(bounds.Left, Math.Min(bounds.Right - wanted.Width, wanted.X + k * step));

                bool free = true;
                foreach (var r in placed)
                {
                    if (r.Intersects(candidate))
                    {
                        free = false;
                        break;
                    }
                }
                if (free)
                    return candidate;
            }
            return wanted;
        }

        private static SharpDX.Mathematics.Interop.RawVector2 ToRaw(Vector2 v) => new SharpDX.Mathematics.Interop.RawVector2(v.X, v.Y);

        private static SharpDX.Mathematics.Interop.RawColor4 ToRaw(System.Drawing.Color c) =>
            new SharpDX.Mathematics.Interop.RawColor4(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
    }
}
