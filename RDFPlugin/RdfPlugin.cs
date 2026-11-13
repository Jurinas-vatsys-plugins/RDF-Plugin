using System;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using vatsys;
using vatsys.Plugin;

namespace vatSys.RDF
{
    /// <summary>
    /// Radio Direction Finder for vatSys. Port of the EuroScope RDF plugin
    /// (https://github.com/chembergj/RDF) using vatSys' built-in AFV client as the data source.
    /// </summary>
    [Export(typeof(IPlugin))]
    public sealed class RdfPlugin : ILabelPlugin
    {
        internal const string PluginName = "RDF";

        /// <summary>Optional custom label item; add &lt;Item Type="RDF" .../&gt; to Labels.xml to use it.</summary>
        private const string LabelItemType = "RDF";

        private static readonly TimeSpan TestDuration = TimeSpan.FromSeconds(5);

        private volatile RdfSettings settings;
        private readonly RdfState state;
        private readonly bool drawingAvailable;

        private ToolStripMenuItem enabledMenuItem;
        private ToolStripMenuItem lastTxMenuItem;
        private LastTxLineManager lastTxLines;
        private ToolStripMenuItem ringSizeMenuItem;

        /// <summary>Quick ring (circle radius) presets in pixels; any other value can be typed in the settings window.</summary>
        private static readonly int[] RingSizePresets = { 20, 30, 40, 50, 60, 80, 100 };
        private SettingsWindow settingsWindow;

        public string Name => PluginName;

        public RdfPlugin()
        {
            EmbeddedAssemblies.Register();

            settings = RdfSettings.Load();
            state = new RdfState(() => settings);

            try
            {
                InstallRenderPatch();
                drawingAvailable = true;
            }
            catch (Exception ex)
            {
                // Keep the plugin usable through the track colour highlight.
                Errors.Add(new Exception("RDF plugin could not hook the ASD renderer (" + ex.Message + "). Circles/lines are unavailable; track colour highlighting is used instead.", ex), PluginName);
                if (!settings.HighlightTrack)
                {
                    var s = settings.Clone();
                    s.HighlightTrack = true;
                    settings = s;
                }
            }

            try
            {
                lastTxLines = new LastTxLineManager(() => settings, state);
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("RDF plugin could not add the LAST TX line to the VSCS window (" + ex.Message + ").", ex), PluginName);
            }

            try
            {
                InstallVhfEffectsPatch();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("RDF plugin could not add the VHF effects option to Audio Settings (" + ex.Message + ").", ex), PluginName);
            }

            try
            {
                AddMenuItems();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("RDF plugin menu registration failed: " + ex.Message, ex), PluginName);
            }
        }

        // Kept separate so Harmony types are only touched after the embedded assembly resolver is registered.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void InstallRenderPatch() => AsdRenderPatch.Install(() => settings, () => state.Current);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void InstallVhfEffectsPatch() => VhfEffectsPatch.Install(() => settings.VhfEffects, SetVhfEffects);

        private void SetVhfEffects(bool enabled)
        {
            var updated = settings.Clone();
            updated.VhfEffects = enabled;
            ApplySettings(updated);
            VhfEffectsPatch.Apply();
        }

        internal static string GetCallsign(Track track)
        {
            var rt = track.GetRadarTrack();
            var cs = rt?.ActualAircraft?.Callsign;
            if (!string.IsNullOrEmpty(cs))
                return cs;
            return track.GetFDR()?.Callsign;
        }

        private bool IsHighlighted(Track track)
        {
            var snapshot = state.Current;
            if (snapshot.Callsigns.Count == 0 || track == null)
                return false;
            var cs = GetCallsign(track);
            return cs != null && snapshot.Callsigns.Contains(cs);
        }

        #region Menu

        private void AddMenuItems()
        {
            var root = new ToolStripMenuItem("RDF");

            enabledMenuItem = new ToolStripMenuItem("Enabled") { Checked = settings.Enabled };
            enabledMenuItem.Click += (s, e) =>
            {
                var updated = settings.Clone();
                updated.Enabled = !updated.Enabled;
                ApplySettings(updated);
            };

            lastTxMenuItem = new ToolStripMenuItem("Show LAST TX in VSCS") { Checked = settings.ShowLastTx };
            lastTxMenuItem.Click += (s, e) =>
            {
                var updated = settings.Clone();
                updated.ShowLastTx = !updated.ShowLastTx;
                ApplySettings(updated);
            };

            ringSizeMenuItem = new ToolStripMenuItem("Ring size");
            foreach (var px in RingSizePresets)
            {
                var item = new ToolStripMenuItem(px + " px") { Tag = px };
                item.Click += (s, e) =>
                {
                    var updated = settings.Clone();
                    updated.Radius = (int)((ToolStripMenuItem)s).Tag;
                    ApplySettings(updated);
                };
                ringSizeMenuItem.DropDownItems.Add(item);
            }
            UpdateRingSizeMenu();

            var settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += (s, e) => MMI.InvokeOnGUI(ShowSettingsWindow);

            root.DropDownItems.Add(enabledMenuItem);
            root.DropDownItems.Add(lastTxMenuItem);
            root.DropDownItems.Add(ringSizeMenuItem);
            root.DropDownItems.Add(settingsItem);

            // Test mode is hidden unless RDFPlugin.json contains { "EnableTest": true }.
            if (DeveloperOptions.Load().EnableTest)
            {
                var testItem = new ToolStripMenuItem("Test on selected track");
                testItem.Click += (s, e) => TestSelectedTrack();
                root.DropDownItems.Add(new ToolStripSeparator());
                root.DropDownItems.Add(testItem);
            }

            MMI.AddCustomMenuItem(new CustomToolStripMenuItem(CustomToolStripMenuItemWindowType.Main, CustomToolStripMenuItemCategory.Settings, root));
        }

        /// <summary>Ticks the preset matching the current radius and shows the value in the menu text.</summary>
        private void UpdateRingSizeMenu()
        {
            if (ringSizeMenuItem == null)
                return;
            ringSizeMenuItem.Text = "Ring size (" + settings.Radius + " px)";
            foreach (ToolStripMenuItem item in ringSizeMenuItem.DropDownItems)
                item.Checked = (int)item.Tag == settings.Radius;
        }

        private void TestSelectedTrack()
        {
            var track = MMI.SelectedTrack;
            var cs = track != null ? GetCallsign(track) : null;
            if (cs == null)
            {
                MessageBox.Show("Select a track first, then choose RDF > Test on selected track.", "RDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            state.Test(cs, TestDuration);
        }

        private void ShowSettingsWindow()
        {
            if (settingsWindow != null && !settingsWindow.IsDisposed)
            {
                settingsWindow.Activate();
                return;
            }
            // VhfEffects is changed in the vatSys Audio Settings window, not here; keep its current value.
            settingsWindow = new SettingsWindow(settings.Clone(), drawingAvailable, s =>
            {
                s.VhfEffects = settings.VhfEffects;
                ApplySettings(s);
            });
            // Owned by the vatSys main window so it stays on top of it, like other vatSys windows.
            var owner = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
            if (owner != null)
                settingsWindow.Show(owner);
            else
                settingsWindow.Show();
        }

        private void ApplySettings(RdfSettings updated)
        {
            settings = updated;
            if (enabledMenuItem != null)
                enabledMenuItem.Checked = updated.Enabled;
            if (lastTxMenuItem != null)
                lastTxMenuItem.Checked = updated.ShowLastTx;
            UpdateRingSizeMenu();
            lastTxLines?.Refresh();
            try
            {
                updated.Save();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("RDF plugin could not save settings: " + ex.Message, ex), PluginName);
            }
        }

        #endregion

        #region IPlugin / ILabelPlugin

        public void OnFDRUpdate(FDP2.FDR updated)
        {
        }

        public void OnRadarTrackUpdate(RDP.RadarTrack updated)
        {
        }

        public CustomLabelItem GetCustomLabelItem(string itemType, Track track, FDP2.FDR flightDataRecord, RDP.RadarTrack radarTrack)
        {
            if (itemType != LabelItemType)
                return null;

            bool on = settings.Enabled && IsHighlighted(track);
            return new CustomLabelItem
            {
                Type = itemType,
                Text = on ? "RX" : "",
                CustomForeColour = on ? ToCustom(state.Current.Concurrent ? settings.ConcurrentColour : settings.Colour) : null,
                Border = on ? BorderFlags.All : BorderFlags.None,
                CustomBorderColour = on ? ToCustom(state.Current.Concurrent ? settings.ConcurrentColour : settings.Colour) : null
            };
        }

        public CustomColour SelectASDTrackColour(Track track)
        {
            var s = settings;
            if (!s.Enabled || !s.HighlightTrack || !IsHighlighted(track))
                return null;
            return ToCustom(state.Current.Concurrent ? s.ConcurrentColour : s.TrackColour);
        }

        public CustomColour SelectGroundTrackColour(Track track) => SelectASDTrackColour(track);

        #endregion

        private static CustomColour ToCustom(System.Drawing.Color c) => new CustomColour(c.R, c.G, c.B);
    }
}
