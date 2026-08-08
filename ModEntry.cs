using System;
using System.Reflection;
using HarmonyLib;
using KMod;
using ONI_Together.API;

namespace MultiplayerCompatPatch
{
    public sealed class ModEntry : UserMod2
    {
        public const string HarmonyId = "MultiplayerCompatPatch";

        private Harmony _harmony;

        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);
            _harmony = harmony;
            // Every individual compat patch is applied lazily from OnAllModsLoaded, once we know
            // (a) whether ONI Together is present at all, and (b) which of the four target mods'
            // assemblies actually got loaded. Nothing here assumes any of those five mods exist.
        }

        public override void OnAllModsLoaded(Harmony harmony, System.Collections.Generic.IReadOnlyList<Mod> mods)
        {
            base.OnAllModsLoaded(harmony, mods);

            if (!MP_Mod_Info.MultiplayerModPresent)
            {
                // ONI Together isn't installed/enabled - nothing for this mod to do. All four
                // target mods behave exactly as they do single-player; we never patch anything.
                Debug.Log("[MultiplayerCompatPatch] ONI Together not present - compat patches skipped.");
                return;
            }

            Debug.Log("[MultiplayerCompatPatch] ONI Together detected - applying compat patches.");

            MoveThisHereCompat.MoveThisHereCompatPatches.TryApply(_harmony);
            SignsTagsAndRibbonsCompat.SignsTagsAndRibbonsCompatPatches.TryApply(_harmony);
            ScaffoldsCompat.ScaffoldsCompatPatches.TryApply(_harmony);
            ResearchQueueCompat.ResearchQueueCompatPatches.TryApply(_harmony);

            PacketRegistryAPI.AutoRegisterAll(Assembly.GetExecutingAssembly());
        }
    }
}
