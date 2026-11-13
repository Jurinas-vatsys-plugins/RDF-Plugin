using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using HarmonyLib;
using vatsys;

namespace vatSys.RDF
{
    /// <summary>
    /// Adds a "VHF effects" toggle to the Emulation box of the vatSys Audio Settings window. When it is off,
    /// the AFV client's BypassEffects switch is set, which removes the simulated radio sound (band-pass
    /// equaliser, white noise and crackle) from received voice.
    /// vatSys builds a new AFV soundcard every time audio (re)starts and new soundcards start with effects on,
    /// so the setting is applied again after each AFV.StartClient.
    /// </summary>
    internal static class VhfEffectsPatch
    {
        private const string HarmonyId = "vatsys.rdfplugin.vhfeffects";
        private const string ToggleName = "rdfVhfEffectsButton";

        private static Func<bool> getEnabled;
        private static Action<bool> setEnabled;
        private static FieldInfo afvInstanceField;
        private static FieldInfo afvClientField;

        public static void Install(Func<bool> enabled, Action<bool> changeEnabled)
        {
            getEnabled = enabled;
            setEnabled = changeEnabled;

            var vatsysAssembly = typeof(MMI).Assembly;
            var afv = vatsysAssembly.GetType("vatsys.AFV", throwOnError: true);
            afvInstanceField = AccessTools.Field(afv, "instance") ?? throw new MissingFieldException(afv.FullName, "instance");
            afvClientField = AccessTools.Field(afv, "Client") ?? throw new MissingFieldException(afv.FullName, "Client");
            var startClient = AccessTools.Method(afv, "StartClient") ?? throw new MissingMethodException(afv.FullName, "StartClient");

            var audioWindow = vatsysAssembly.GetType("vatsys.AudioSettingsWindow", throwOnError: true);
            var audioWindowCtor = AccessTools.Constructor(audioWindow, Type.EmptyTypes)
                ?? throw new MissingMethodException(audioWindow.FullName, ".ctor");

            var harmony = new Harmony(HarmonyId);
            harmony.Patch(startClient, postfix: new HarmonyMethod(typeof(VhfEffectsPatch).GetMethod(nameof(StartClientPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
            harmony.Patch(audioWindowCtor, postfix: new HarmonyMethod(typeof(VhfEffectsPatch).GetMethod(nameof(AudioWindowPostfix), BindingFlags.Static | BindingFlags.NonPublic)));

            // Audio is normally already running when plugins load.
            Apply();
        }

        /// <summary>Pushes the current setting to the running AFV client, if there is one.</summary>
        public static void Apply()
        {
            var afv = afvInstanceField?.GetValue(null);
            if (afv != null)
                ApplyTo(afvClientField.GetValue(afv));
        }

        private static void ApplyTo(object client)
        {
            if (client == null || getEnabled == null)
                return;
            var property = AccessTools.Property(client.GetType(), "BypassEffects");
            property?.SetValue(client, !getEnabled());
        }

        private static void StartClientPostfix(object __instance)
        {
            try
            {
                ApplyTo(afvClientField.GetValue(__instance));
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("RDF plugin could not apply the VHF effects setting (" + ex.Message + ").", ex), RdfPlugin.PluginName);
            }
        }

        private static void AudioWindowPostfix(Form __instance)
        {
            try
            {
                AddToggle(__instance);
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("RDF plugin could not add the VHF effects toggle to the Audio Settings window (" + ex.Message + ").", ex), RdfPlugin.PluginName);
            }
        }

        private static void AddToggle(Form window)
        {
            // The Emulation box: "Emulation" title, the VHF preset label in row 1 (column 0 is empty),
            // and the "Realistic HF" toggle in row 2. The new toggle goes in the empty cell next to the VHF preset.
            var table = window.Controls.Find("tableLayoutPanel4", true).OfType<TableLayoutPanel>().FirstOrDefault();
            var template = window.Controls.Find("hfRealButton", true).OfType<CheckBox>().FirstOrDefault();
            var presetLabel = window.Controls.Find("vhfModeLabel", true).FirstOrDefault();
            if (table == null || template == null || presetLabel == null)
                throw new InvalidOperationException("Emulation controls not found");
            if (table.Controls.ContainsKey(ToggleName))
                return;

            // Same control type as "Realistic HF" so it looks and behaves the same.
            var toggle = (CheckBox)Activator.CreateInstance(template.GetType(), nonPublic: true);
            toggle.Name = ToggleName;
            toggle.AutoCheck = false;
            toggle.Anchor = template.Anchor;
            toggle.Font = template.Font;
            toggle.Margin = template.Margin;
            toggle.Size = template.Size;
            toggle.TextAlign = template.TextAlign;
            toggle.Checked = getEnabled();
            toggle.Click += (s, e) =>
            {
                bool enabled = !toggle.Checked;
                toggle.Checked = enabled;
                setEnabled(enabled);
            };
            new ToolTip().SetToolTip(toggle, "VHF effects (radio filter, noise and crackle) on received voice");

            table.Controls.Add(toggle, 0, 1);
        }
    }
}
