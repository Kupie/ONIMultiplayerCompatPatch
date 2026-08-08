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
    /// Two things do need a patch, both discovered by reading the current source (the second
    /// wasn't called out in the original task brief):
    ///  1. HaulingPointConfig's building placement bypasses the normal ghost/construction flow and
    ///     instant-builds via BuildingDef.Instantiate -> __instance.Build(), the same way Scaffolds
    ///     does. See InstantBuildFix for the shared fix, applied here for PrefabID "HaulingPoint".
    ///  2. HaulingPoint.Sim1000ms calls DeconstructableHaulingPoint.OnDeconstruct() directly
    ///     (which itself calls gameObject.DeleteObject()) when storage nears full - the same
    ///     unsynced-deletion shape as Scaffolds' DeconstructableScaffold.OnDeconstruct, fixed the
    ///     same way via CellMethodRelay.
    /// </summary>
    public static class MoveThisHereCompatPatches
    {
        public const string AssemblyName = "MoveThisHere";
        public const string HaulingPointConfigType = "MoveThisHere.HaulingPointConfig";
        public const string DeconstructableHaulingPointType = "MoveThisHere.DeconstructableHaulingPoint";
        public const string HaulingPointPrefabId = "HaulingPoint";

        public static void TryApply(Harmony harmony)
        {
            if (!ModPresence.IsAssemblyLoaded(AssemblyName) || !ModPresence.TypeExists(HaulingPointConfigType))
            {
                return;
            }

            InstantBuildFix.ApplyFor(harmony, HaulingPointPrefabId);
            CellMethodRelay.ApplyPostfix(harmony, DeconstructableHaulingPointType, "OnDeconstruct");
        }
    }
}
