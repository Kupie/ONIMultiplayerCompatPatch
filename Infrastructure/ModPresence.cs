using System;
using System.Linq;
using HarmonyLib;

namespace MultiplayerCompatPatch.Infrastructure
{
    /// <summary>
    /// Detects whether specific other mods' assemblies are actually loaded in the current
    /// process, so each compat sub-module can gate its own patches independently. We deliberately
    /// avoid hard assembly references to the target mods (see repo README) - detection is done by
    /// scanning loaded assemblies / probing for a known type by name, exactly like Harmony's own
    /// string-based patch targeting does internally.
    /// </summary>
    public static class ModPresence
    {
        public static bool IsAssemblyLoaded(string assemblyName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// A stronger check than assembly presence alone: also confirms a specific type from that
        /// assembly actually resolves, which is what Harmony needs anyway before it can patch it.
        /// </summary>
        public static bool TypeExists(string fullTypeName)
        {
            return AccessTools.TypeByName(fullTypeName) != null;
        }
    }
}
