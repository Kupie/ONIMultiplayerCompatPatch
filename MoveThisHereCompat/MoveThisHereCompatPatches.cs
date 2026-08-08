using HarmonyLib;
using MultiplayerCompatPatch.Infrastructure;

namespace MultiplayerCompatPatch.MoveThisHereCompat
{
    /// <summary>
    /// Compat shims for DoctorFeelGoodMD/OxygenNotIncluded-Mods' MoveThisHere (HaulingPoint).
    ///
    /// HaulingPoint's capacity slider implements ISingleSliderControl with no custom side screen,
    /// so it rides ONI Together's generic SingleSliderSideScreen sync for free - no patch needed
    /// here for that part (verify empirically; see NOTES.md).
    ///
    /// What *does* need a patch: HaulingPointConfig's building placement bypasses the normal
    /// ghost/construction flow and instant-builds via BuildingDef.Instantiate -> __instance.Build(),
    /// the same way Scaffolds does. See InstantBuildFix for the shared fix, applied here for
    /// PrefabID "HaulingPoint".
    /// </summary>
    public static class MoveThisHereCompatPatches
    {
        public const string AssemblyName = "MoveThisHere";
        public const string HaulingPointConfigType = "MoveThisHere.HaulingPointConfig";
        public const string HaulingPointPrefabId = "HaulingPoint";

        public static void TryApply(Harmony harmony)
        {
            if (!ModPresence.IsAssemblyLoaded(AssemblyName) || !ModPresence.TypeExists(HaulingPointConfigType))
            {
                return;
            }

            InstantBuildFix.ApplyFor(harmony, HaulingPointPrefabId);
        }
    }
}
