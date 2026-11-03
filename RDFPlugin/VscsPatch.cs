using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Windows.Forms;
using HarmonyLib;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using vatsys;
using RectangleF = SharpDX.RectangleF;

namespace vatSys.RDF
{
    /// <summary>
    /// Adds a LAST TX tile to the VSCS (voice) window, showing the callsign and frequency of the
    /// last station received - like "Last Received" / "Last Freq" in the AFV standalone client.
    /// The tile takes the grid slot after the last frequency button; when that column is full it
    /// opens a new column and the menu buttons and pages move one column to the right.
    /// </summary>
    internal static class VscsPatch
    {
        private const string HarmonyId = "vatsys.rdfplugin.vscs";

        private static Func<RdfSettings> getSettings;
        private static RdfState state;

        // VSCSControlDX is internal to vatSys.exe; members are accessed through compiled accessors.
        private static Func<object, int, int, RectangleF> getPositionRectangle;
        private static Action<object, RectangleF, RenderTarget, Brush, Brush, string, string, bool, bool, Brush> paintButton;
        private static Func<object, SolidColorBrush> buttonBrush;
        private static Func<object, SolidColorBrush> receivingBrush;
        private static Func<object, SolidColorBrush> textBrush;
        private static Func<object, TextFormat> fontSmall;
        private static Func<object, TextFormat> fontVerySmall;

        private static readonly List<WeakReference<Control>> controls = new List<WeakReference<Control>>();

        private static bool errorReported;

        public static void Install(Func<RdfSettings> settings, RdfState rdfState)
        {
            getSettings = settings;
            state = rdfState;

            var vscs = typeof(MMI).Assembly.GetType("vatsys.VSCSControlDX", throwOnError: true);

            var pos = AccessTools.Method(vscs, "GetPositionRectangleF", new[] { typeof(int), typeof(int) })
                ?? throw new MissingMethodException(vscs.FullName, "GetPositionRectangleF");
            var pInst = Expression.Parameter(typeof(object));
            var pCol = Expression.Parameter(typeof(int));
            var pRow = Expression.Parameter(typeof(int));
            getPositionRectangle = Expression.Lambda<Func<object, int, int, RectangleF>>(
                Expression.Call(Expression.Convert(pInst, vscs), pos, pCol, pRow), pInst, pCol, pRow).Compile();

            var paintArgs = new[] { typeof(RectangleF), typeof(RenderTarget), typeof(Brush), typeof(Brush), typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(Brush) };
            var paint = AccessTools.Method(vscs, "PaintButton", paintArgs)
                ?? throw new MissingMethodException(vscs.FullName, "PaintButton");
            var ps = new List<ParameterExpression> { Expression.Parameter(typeof(object)) };
            foreach (var t in paintArgs)
                ps.Add(Expression.Parameter(t));
            paintButton = Expression.Lambda<Action<object, RectangleF, RenderTarget, Brush, Brush, string, string, bool, bool, Brush>>(
                Expression.Call(Expression.Convert(ps[0], vscs), paint, ps.GetRange(1, paintArgs.Length)), ps).Compile();

            buttonBrush = FieldGetter<SolidColorBrush>(vscs, "buttonBrush");
            receivingBrush = FieldGetter<SolidColorBrush>(vscs, "freqRecvBrush");
            textBrush = FieldGetter<SolidColorBrush>(vscs, "buttonTextBrush");
            fontSmall = FieldGetter<TextFormat>(vscs, "fontSmall");
            fontVerySmall = FieldGetter<TextFormat>(vscs, "fontVerySmall");

            var target = AccessTools.Method(vscs, "PaintFreqButtons")
                ?? throw new MissingMethodException(vscs.FullName, "PaintFreqButtons");
            new Harmony(HarmonyId).Patch(target, postfix: new HarmonyMethod(typeof(VscsPatch).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic)));

            state.Changed += (s, e) => InvalidateAll();
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

        /// <summary>Repaint the VSCS windows (they only repaint on demand, unlike the ASD).</summary>
        public static void InvalidateAll()
        {
            Control[] alive;
            lock (controls)
            {
                controls.RemoveAll(w => !w.TryGetTarget(out var c) || c.IsDisposed);
                alive = new Control[controls.Count];
                for (int i = 0; i < controls.Count; i++)
                    controls[i].TryGetTarget(out alive[i]);
            }
            foreach (var c in alive)
            {
                try
                {
                    if (c != null && c.IsHandleCreated && !c.IsDisposed)
                        c.BeginInvoke((System.Windows.Forms.MethodInvoker)c.Invalidate);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        private static void Register(Control control)
        {
            lock (controls)
            {
                foreach (var w in controls)
                {
                    if (w.TryGetTarget(out var c) && ReferenceEquals(c, control))
                        return;
                }
                controls.Add(new WeakReference<Control>(control));
            }
        }

        // Original: private int PaintFreqButtons(RenderTarget dc) - returns the column of the last frequency button.
        private static void Postfix(object __instance, RenderTarget dc, ref int __result)
        {
            try
            {
                if (!(__instance is Control control))
                    return;
                Register(control);

                if (!getSettings().ShowLastTx || dc == null)
                    return;

                // Find the slot vatSys would use for one more frequency button.
                int count = Audio.VSCSFrequencies?.Count ?? 0;
                float bottom = control.ClientRectangle.Bottom;
                int col = 0, row = 0;
                for (int i = 0; i < count; i++)
                {
                    if (getPositionRectangle(__instance, col, row++).Bottom > bottom)
                    {
                        col++;
                        row = 1;
                    }
                }
                var rect = getPositionRectangle(__instance, col, row);
                if (rect.Bottom > bottom && row > 0)
                {
                    col++;
                    rect = getPositionRectangle(__instance, col, 0);
                }

                PaintTile(__instance, dc, rect);
                __result = Math.Max(__result, col);
            }
            catch (Exception ex)
            {
                if (!errorReported)
                {
                    errorReported = true;
                    Errors.Add(new Exception("RDF plugin LAST TX tile failed: " + ex.Message, ex), RdfPlugin.PluginName);
                }
            }
        }

        private static void PaintTile(object vscs, RenderTarget dc, RectangleF rect)
        {
            var last = state.Last;
            var snapshot = state.Current;
            bool live = false;
            if (last != null && snapshot.Live)
            {
                foreach (var cs in last.Callsigns)
                {
                    if (snapshot.Callsigns.Contains(cs))
                    {
                        live = true;
                        break;
                    }
                }
            }

            var background = live ? receivingBrush(vscs) : buttonBrush(vscs);
            // Let vatSys draw the button body/bevel so the tile matches the other VSCS buttons.
            paintButton(vscs, rect, dc, background, background, "", "", false, false, null);

            var brush = textBrush(vscs);
            var small = fontSmall(vscs);
            var verySmall = fontVerySmall(vscs);
            if (brush == null || small == null || verySmall == null)
                return;

            float x = rect.X + 1f, w = rect.Width - 2f;
            float h1 = rect.Height * 0.3f, h2 = rect.Height * 0.4f;
            dc.DrawText("LAST TX", verySmall, new RectangleF(x, rect.Y + 1f, w, h1), brush, DrawTextOptions.Clip);
            dc.DrawText(FormatCallsigns(last), small, new RectangleF(x, rect.Y + h1, w, h2), brush, DrawTextOptions.Clip);
            dc.DrawText(FormatFrequency(last), verySmall, new RectangleF(x, rect.Y + h1 + h2, w, rect.Height - h1 - h2 - 1f), brush, DrawTextOptions.Clip);
        }

        private static string FormatCallsigns(LastTransmission last)
        {
            if (last == null || last.Callsigns.Count == 0)
                return "-";
            var first = last.Callsigns[0];
            return last.Callsigns.Count == 1 ? first : first + " +" + (last.Callsigns.Count - 1);
        }

        private static string FormatFrequency(LastTransmission last)
        {
            if (last == null || last.Frequency == 0)
                return "---.---";
            return (last.Frequency / 1000000.0).ToString("000.000", CultureInfo.InvariantCulture);
        }

        private static Func<object, T> FieldGetter<T>(Type type, string name)
        {
            var field = AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
            var p = Expression.Parameter(typeof(object));
            // Some fields (e.g. the VSCS fonts) are static; the instance argument is then ignored.
            var access = field.IsStatic ? Expression.Field(null, field) : Expression.Field(Expression.Convert(p, type), field);
            return Expression.Lambda<Func<object, T>>(access, p).Compile();
        }
    }
}
