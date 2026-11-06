using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using vatsys;
using VATSYSControls;

namespace vatSys.RDF
{
    /// <summary>
    /// RDF settings, built from vatSys' own window and control classes (BaseForm, GenericButton,
    /// TextLabel, TextField, DropDownBox) so it looks and behaves like a native vatSys window.
    /// </summary>
    internal sealed class SettingsWindow : BaseForm
    {
        private static readonly string[] OffscreenNames = { "NONE", "LINE FROM CENTRE", "EDGE POINTER" };

        private RdfSettings settings;
        private readonly Action<RdfSettings> apply;

        private readonly VsToggle enabled = new VsToggle();
        private readonly GenericButton colour = ColourButton();
        private readonly GenericButton concurrentColour = ColourButton();
        private readonly TextField radius = NumberField();
        private readonly TextField lineWidth = NumberField();
        private readonly DropDownBox offscreen = new DropDownBox();
        private readonly VsToggle offscreenLabel = new VsToggle();
        private readonly KeyCaptureButton holdKey = new KeyCaptureButton();
        private readonly TextField persist = NumberField();
        private readonly TextField recallDelay = NumberField();
        private readonly VsToggle highlightTrack = new VsToggle();
        private readonly GenericButton trackColour = ColourButton();
        private readonly VsToggle showLastTx = new VsToggle();

        public SettingsWindow(RdfSettings settings, bool drawingAvailable, Action<RdfSettings> apply)
        {
            this.settings = settings;
            this.apply = apply;

            Name = "RDFSettingsWindow";
            Text = "RDF SETTINGS";
            HasCloseButton = true;
            HasMinimizeButton = false;
            HasMaximizeButton = false;
            MiddleClickClose = false;
            Resizeable = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;

            offscreen.Items = new ObservableCollection<string>(OffscreenNames);
            offscreen.BackColor = Colours.GetColour(Colours.Identities.WindowBackground);
            offscreen.ForeColor = Colours.GetColour(Colours.Identities.InteractiveText);
            offscreen.FocusColor = Colours.GetColour(Colours.Identities.HighlightedText);
            offscreen.Font = MMI.eurofont_winsml;
            offscreen.Size = new Size(190, 28);

            var grid = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8),
                Location = Point.Empty,
                BackColor = BackColor
            };

            void Heading(string text)
            {
                var l = Label(text, interactive: false);
                l.Margin = new Padding(3, grid.Controls.Count == 0 ? 3 : 12, 3, 3);
                grid.Controls.Add(l);
                grid.SetColumnSpan(l, 2);
            }
            void Row(string label, Control c)
            {
                grid.Controls.Add(Label(label, interactive: false));
                c.Margin = new Padding(3);
                grid.Controls.Add(c);
            }
            void ToggleRow(VsToggle toggle, string label)
            {
                // Toggle with its (clickable) caption right next to it, like vatSys' display settings.
                toggle.Margin = new Padding(0, 3, 3, 3);
                var l = Label(label, interactive: true);
                l.Margin = new Padding(3, 8, 3, 3);
                l.Click += (s, e) => toggle.Checked = !toggle.Checked;
                var row = new FlowLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    WrapContents = false,
                    Margin = new Padding(3, 0, 3, 0),
                    BackColor = BackColor
                };
                row.Controls.Add(toggle);
                row.Controls.Add(l);
                grid.Controls.Add(row);
                grid.SetColumnSpan(row, 2);
            }

            Heading("RADIO DIRECTION FINDER");
            ToggleRow(enabled, "HIGHLIGHT TRANSMITTING AIRCRAFT");
            Row("COLOUR", colour);
            Row("SIMULTANEOUS TX", concurrentColour);
            Row("RING SIZE (RADIUS PX)", radius);
            Row("LINE WIDTH (PX)", lineWidth);
            Row("AIRCRAFT OFF DISPLAY", offscreen);
            ToggleRow(offscreenLabel, "CALLSIGN AT DISPLAY EDGE");

            Heading("LAST TRANSMISSION");
            // Recall is off by default (no key); CLEAR returns to that.
            var holdKeyClear = Button("CLEAR");
            holdKeyClear.Click += (s, e) => holdKey.Key = Keys.None;
            var holdKeyRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                BackColor = BackColor
            };
            holdKey.Margin = new Padding(0, 0, 3, 0);
            holdKeyClear.Margin = new Padding(0);
            holdKeyRow.Controls.AddRange(new Control[] { holdKey, holdKeyClear });
            Row("HOLD TO RECALL", holdKeyRow);
            Row("RECALL HOLD DELAY (MS)", recallDelay);
            Row("KEEP HIGHLIGHT (S)", persist);
            ToggleRow(showLastTx, "LAST TX LINE IN VSCS");

            Heading("TRACK");
            ToggleRow(highlightTrack, "RECOLOUR TRACK AND LABEL");
            Row("TRACK COLOUR", trackColour);

            if (!drawingAvailable)
            {
                var warn = Label("ASD DRAWING HOOK UNAVAILABLE - ONLY TRACK RECOLOURING WORKS", interactive: false);
                warn.ForeColor = Colours.GetColour(Colours.Identities.Warning);
                grid.Controls.Add(warn);
                grid.SetColumnSpan(warn, 2);
            }

            var file = Label(settings.LoadedFrom, interactive: false);
            file.Font = MMI.eurofont_winverysml;
            file.ForeColor = Colours.GetColour(Colours.Identities.NonInteractiveText);
            file.MaximumSize = new Size(420, 0);
            file.Margin = new Padding(3, 12, 3, 3);
            grid.Controls.Add(file);
            grid.SetColumnSpan(file, 2);

            var save = Button("SAVE");
            var applyButton = Button("APPLY");
            var cancel = Button("CANCEL");
            save.Click += (s, e) => { Commit(); Close(); };
            applyButton.Click += (s, e) => Commit();
            cancel.Click += (s, e) => Close();
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 10, 0, 0),
                BackColor = BackColor
            };
            buttons.Controls.AddRange(new Control[] { cancel, applyButton, save });
            grid.Controls.Add(buttons);
            grid.SetColumnSpan(buttons, 2);

            Controls.Add(grid);
            ClientSize = grid.PreferredSize;

            foreach (var b in new[] { colour, concurrentColour, trackColour })
                b.Click += PickColour;

            LoadValues();
        }

        private static TextLabel Label(string text, bool interactive) => new TextLabel
        {
            Text = text,
            AutoSize = true,
            HasBorder = false,
            InteractiveText = interactive,
            Font = MMI.eurofont_winsml,
            Anchor = AnchorStyles.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(3, 8, 10, 3)
        };

        private static GenericButton Button(string text) => new GenericButton
        {
            Text = text,
            Font = MMI.eurofont_winsml,
            Size = new Size(90, 30),
            Margin = new Padding(3)
        };

        private static GenericButton ColourButton() => new GenericButton
        {
            Text = "",
            Size = new Size(90, 26)
        };

        private static TextField NumberField() => new TextField
        {
            Font = MMI.eurofont_winsml,
            Width = 90,
            TextAlign = HorizontalAlignment.Center,
            BackColor = Colours.GetColour(Colours.Identities.WindowBackground),
            ForeColor = Colours.GetColour(Colours.Identities.InteractiveText)
        };

        private void LoadValues()
        {
            enabled.Checked = settings.Enabled;
            colour.BackColor = settings.Colour;
            concurrentColour.BackColor = settings.ConcurrentColour;
            trackColour.BackColor = settings.TrackColour;
            radius.Text = settings.Radius.ToString(CultureInfo.InvariantCulture);
            lineWidth.Text = settings.LineWidth.ToString("0.#", CultureInfo.InvariantCulture);
            offscreen.SelectedIndex = (int)settings.Offscreen;
            offscreenLabel.Checked = settings.OffscreenLabel;
            holdKey.Key = settings.HoldKey;
            recallDelay.Text = settings.RecallDelayMs.ToString(CultureInfo.InvariantCulture);
            persist.Text = settings.PersistSeconds.ToString("0.#", CultureInfo.InvariantCulture);
            highlightTrack.Checked = settings.HighlightTrack;
            showLastTx.Checked = settings.ShowLastTx;
        }

        private void Commit()
        {
            var s = settings.Clone();
            s.Enabled = enabled.Checked;
            s.Colour = colour.BackColor;
            s.ConcurrentColour = concurrentColour.BackColor;
            s.TrackColour = trackColour.BackColor;
            s.Radius = (int)Math.Round(ReadNumber(radius, s.Radius, 5, 500));
            s.LineWidth = (float)ReadNumber(lineWidth, s.LineWidth, 1, 10);
            if (offscreen.SelectedIndex >= 0)
                s.Offscreen = (OffscreenMode)offscreen.SelectedIndex;
            s.OffscreenLabel = offscreenLabel.Checked;
            s.HoldKey = holdKey.Key;
            s.RecallDelayMs = (int)Math.Round(ReadNumber(recallDelay, s.RecallDelayMs, 0, 5000));
            s.PersistSeconds = ReadNumber(persist, s.PersistSeconds, 0, 60);
            s.HighlightTrack = highlightTrack.Checked;
            s.ShowLastTx = showLastTx.Checked;
            apply(s);
            settings = s.Clone();
            LoadValues();
        }

        /// <summary>Parses a number field; invalid input is flagged and the previous value kept.</summary>
        private static double ReadNumber(TextField field, double current, double min, double max)
        {
            var text = field.Text.Trim().Replace(',', '.');
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                field.ErrorMode(false);
                return Math.Max(min, Math.Min(max, v));
            }
            field.ErrorMode(true);
            return current;
        }

        private void PickColour(object sender, EventArgs e)
        {
            var button = (GenericButton)sender;
            using (var dlg = new ColorDialog { Color = button.BackColor, FullOpen = true })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    button.BackColor = dlg.Color;
                    button.Invalidate();
                }
            }
        }

        /// <summary>Same look as vatSys' internal ToggleButton: raised when off, sunken and shaded when on.</summary>
        private sealed class VsToggle : CheckBox
        {
            public VsToggle()
            {
                Appearance = Appearance.Button;
                Size = new Size(26, 26);
                Font = MMI.eurofont_sml;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnCheckedChanged(EventArgs e)
            {
                base.OnCheckedChanged(e);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs pe)
            {
                var back = Checked ? Colours.GetColour(Colours.Identities.WindowButtonDepressed) : Colours.GetColour(Colours.Identities.WindowBackground);
                using (var brush = new SolidBrush(back))
                    pe.Graphics.FillRectangle(brush, ClientRectangle);
                if (Checked)
                {
                    // Tick mark in the interactive text colour.
                    using (var pen = new Pen(Colours.GetColour(Colours.Identities.InteractiveText), 2f))
                    {
                        pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        var r = ClientRectangle;
                        pe.Graphics.DrawLines(pen, new[]
                        {
                            new PointF(r.Width * 0.25f, r.Height * 0.5f),
                            new PointF(r.Width * 0.43f, r.Height * 0.7f),
                            new PointF(r.Width * 0.75f, r.Height * 0.3f)
                        });
                        pe.Graphics.SmoothingMode = SmoothingMode.Default;
                    }
                }
                ControlPaint.DrawBorder3D(pe.Graphics, ClientRectangle, Checked ? Border3DStyle.Sunken : Border3DStyle.Raised);
            }
        }

        /// <summary>
        /// Click, then press a key or mouse button (middle / X1 / X2). Esc, Backspace or Delete clears it.
        /// </summary>
        private sealed class KeyCaptureButton : GenericButton
        {
            private Keys key;
            private bool learning;

            public KeyCaptureButton()
            {
                Font = MMI.eurofont_winsml;
                Size = new Size(190, 30);
            }

            public Keys Key
            {
                get => key;
                set
                {
                    key = value;
                    learning = false;
                    Text = Format(value);
                }
            }

            protected override void OnClick(EventArgs e)
            {
                base.OnClick(e);
                if (learning)
                    return;
                learning = true;
                Text = "PRESS KEY / BUTTON";
                Focus();
            }

            protected override bool IsInputKey(Keys keyData) => learning || base.IsInputKey(keyData);

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (learning)
                {
                    e.Handled = e.SuppressKeyPress = true;
                    var code = e.KeyCode;
                    if (code == Keys.Escape || code == Keys.Back || code == Keys.Delete)
                        Key = Keys.None;
                    else
                        Key = code;
                    return;
                }
                base.OnKeyDown(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (learning)
                {
                    switch (e.Button)
                    {
                        case MouseButtons.Middle: Key = Keys.MButton; return;
                        case MouseButtons.XButton1: Key = Keys.XButton1; return;
                        case MouseButtons.XButton2: Key = Keys.XButton2; return;
                        case MouseButtons.Right: Key = Keys.RButton; return;
                    }
                }
                base.OnMouseDown(e);
            }

            protected override void OnLostFocus(EventArgs e)
            {
                base.OnLostFocus(e);
                if (learning)
                    Key = key;
            }

            private static string Format(Keys k)
            {
                switch (k)
                {
                    case Keys.None: return "NONE";
                    case Keys.MButton: return "MIDDLE BUTTON";
                    case Keys.RButton: return "RIGHT BUTTON";
                    case Keys.XButton1: return "MOUSE X1";
                    case Keys.XButton2: return "MOUSE X2";
                    case Keys.ControlKey: return "CTRL";
                    case Keys.ShiftKey: return "SHIFT";
                    case Keys.Menu: return "ALT";
                    default: return k.ToString().ToUpperInvariant();
                }
            }
        }
    }
}
