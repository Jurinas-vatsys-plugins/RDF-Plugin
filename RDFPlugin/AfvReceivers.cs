using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GeoVR.Shared;
using vatsys;

namespace vatSys.RDF
{
    /// <summary>
    /// Reads who is being received straight from the AFV client, per transceiver.
    ///
    /// vatSys keeps one ReceivingCallsigns list per VSCS frequency, but the AFV client reports callsigns per
    /// transceiver, and a frequency usually has several transceivers. AFV routes each pilot to the transceiver
    /// that hears it best, so two pilots on the same frequency can be on different transceivers; every report
    /// then overwrites the frequency's list and only the last pilot is left. AFV also only reports when the
    /// number of speakers on a transceiver changes. Reading the receivers directly avoids both problems.
    ///
    /// Path: AFV.instance.Client.Soundcards[] -> soundcardSampleProvider -> receiverInputs (transceiver ID ->
    /// ReceiverSampleProvider) -> voiceInputs (CallsignSampleProvider: InUse, Callsign).
    /// Everything is looked up by reflection; if any of it is missing, Read returns null and the caller
    /// uses the vatSys lists only.
    /// </summary>
    internal static class AfvReceivers
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Type AfvType = typeof(MMI).Assembly.GetType("vatsys.AFV");
        private static readonly FieldInfo AfvInstance = AfvType == null ? null : AfvType.GetField("instance", Any);
        private static readonly FieldInfo AfvClient = AfvType == null ? null : AfvType.GetField("Client", Any);
        private static readonly PropertyInfo UseHfSquelchProperty = AfvType == null ? null : AfvType.GetProperty("UseHfSquelch", Any);
        private static readonly PropertyInfo TransceiversProperty = typeof(VSCSFrequency).GetProperty("Transceivers", Any);

        private static bool failed;

        /// <summary>
        /// Transceiver ID -> callsigns currently received on it, or null when the AFV client cannot be read.
        /// </summary>
        public static Dictionary<ushort, List<string>> Read()
        {
            if (failed || AfvInstance == null || AfvClient == null)
                return null;
            try
            {
                var result = new Dictionary<ushort, List<string>>();
                var afv = AfvInstance.GetValue(null);
                var client = afv == null ? null : AfvClient.GetValue(afv);
                if (client == null)
                    return result;

                if (!(Get(client, "Soundcards") is IEnumerable soundcards))
                    return result;
                foreach (var soundcard in soundcards)
                {
                    var provider = Get(soundcard, "soundcardSampleProvider");
                    if (!(Get(provider, "receiverInputs") is IDictionary receivers))
                        continue;
                    foreach (DictionaryEntry entry in receivers)
                    {
                        var receiver = entry.Value;
                        if (!(Get(receiver, "voiceInputs") is IEnumerable inputs))
                            continue;
                        var id = (ushort)entry.Key;
                        foreach (var input in inputs.Snapshot())
                        {
                            if (!(Get(input, "InUse") is bool inUse) || !inUse)
                                continue;
                            var callsign = (Get(input, "Callsign") as string)?.Trim();
                            if (string.IsNullOrEmpty(callsign))
                                continue;
                            if (!result.TryGetValue(id, out var list))
                                result[id] = list = new List<string>();
                            if (!list.Contains(callsign))
                                list.Add(callsign);
                        }
                    }
                }
                return result;
            }
            catch (InvalidOperationException)
            {
                // A collection changed while it was read (audio restarting); try again on the next poll.
                return null;
            }
            catch (Exception ex)
            {
                failed = true;
                Errors.Add(new Exception("RDF plugin could not read the AFV receivers (" + ex.Message + "); simultaneous transmissions on one frequency may show only one aircraft.", ex), RdfPlugin.PluginName);
                return null;
            }
        }

        /// <summary>Transceiver IDs of a VSCS frequency (the ones AFV reports callsigns for).</summary>
        public static IEnumerable<ushort> TransceiverIds(VSCSFrequency frequency)
        {
            if (TransceiversProperty?.GetValue(frequency) is IEnumerable transceivers)
            {
                foreach (var t in transceivers.Snapshot())
                {
                    if (t is TransceiverDto dto)
                        yield return dto.ID;
                }
            }
        }

        /// <summary>vatSys ignores HF receive reports unless HF squelch is on; do the same.</summary>
        public static bool Counts(VSCSFrequency frequency)
        {
            if (!frequency.IsHF)
                return true;
            return UseHfSquelchProperty?.GetValue(null) is bool squelch && squelch;
        }

        private static readonly Dictionary<(Type, string), MemberInfo> members = new Dictionary<(Type, string), MemberInfo>();

        private static object Get(object target, string name)
        {
            if (target == null)
                return null;
            var key = (target.GetType(), name);
            if (!members.TryGetValue(key, out var member))
            {
                member = (MemberInfo)key.Item1.GetField(name, Any) ?? key.Item1.GetProperty(name, Any)
                    ?? throw new MissingMemberException(key.Item1.FullName, name);
                members[key] = member;
            }
            return member is FieldInfo f ? f.GetValue(target) : ((PropertyInfo)member).GetValue(target);
        }

        private static IEnumerable<object> Snapshot(this IEnumerable source)
        {
            // Copy first: these lists belong to the audio thread.
            var copy = new List<object>();
            foreach (var item in source)
                copy.Add(item);
            return copy;
        }
    }
}
