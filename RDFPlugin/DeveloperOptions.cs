using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Xml;
using System.Xml.Linq;

namespace vatSys.RDF
{
    /// <summary>
    /// Hidden options read from an optional RDFPlugin.json, next to the DLL or in Documents\vatSys Files.
    /// Example: { "EnableTest": true } shows the "Test on selected track" menu item.
    /// </summary>
    internal sealed class DeveloperOptions
    {
        public const string FileName = "RDFPlugin.json";

        public bool EnableTest { get; private set; }

        public static DeveloperOptions Load()
        {
            var options = new DeveloperOptions();
            foreach (var path in new[] { RdfSettings.PluginFolderPath(FileName), RdfSettings.DocumentsPath(FileName) })
            {
                if (!File.Exists(path))
                    continue;
                try
                {
                    // Built-in JSON reader (no dependency on the Newtonsoft.Json version shipped with vatSys).
                    var bytes = File.ReadAllBytes(path);
                    using (var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, XmlDictionaryReaderQuotas.Max))
                    {
                        var root = XElement.Load(reader);
                        var value = root.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "EnableTest", StringComparison.OrdinalIgnoreCase));
                        options.EnableTest = value != null && (string)value.Attribute("type") == "boolean" && bool.Parse(value.Value);
                    }
                    return options;
                }
                catch (Exception)
                {
                    // Malformed file: ignore it and keep the hidden options off.
                }
            }
            return options;
        }
    }
}
