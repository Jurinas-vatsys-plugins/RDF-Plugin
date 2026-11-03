using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace vatSys.RDF
{
    /// <summary>
    /// Resolves dependencies (Harmony) that are embedded as resources, so the plugin
    /// is a single DLL that works from either vatSys\bin\Plugins or Profile\Plugins.
    /// </summary>
    internal static class EmbeddedAssemblies
    {
        private static bool registered;

        public static void Register()
        {
            if (registered)
                return;
            registered = true;
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            if (!string.Equals(name, "0Harmony", StringComparison.OrdinalIgnoreCase))
                return null;

            // Another plugin may already have loaded Harmony; reuse it.
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
            if (loaded != null)
                return loaded;

            using (var stream = typeof(EmbeddedAssemblies).Assembly.GetManifestResourceStream("vatSys.RDF." + name + ".dll"))
            {
                if (stream == null)
                    return null;
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return Assembly.Load(ms.ToArray());
                }
            }
        }
    }
}
