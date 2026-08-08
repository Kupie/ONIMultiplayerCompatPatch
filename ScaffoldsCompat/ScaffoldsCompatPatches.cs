using HarmonyLib;
using MultiplayerCompatPatch.Infrastructure;

namespace MultiplayerCompatPatch.ScaffoldsCompat
{
    /// <summary>
    /// Compat shims for nathantalewis/oni-scaffolds' Scaffold / DeconstructableScaffold.
    ///
    /// Three independent risks fixed here, all confirmed by reading the current source
    /// (see NOTES.md):
    ///  1. BuildingDef.Instantiate instant-builds Scaffolds the same way MoveThisHere does ->
    ///     InstantBuildFix.
    ///  2. DeconstructableScaffold.OnDeconstruct() deletes the GameObject directly, bypassing the
    ///     vanilla deconstruct order pipeline ONI Together's deconstruct patches expect -> a custom
    ///     cell-keyed packet broadcasts the deconstruction.
    ///  3. Scaffold's self-destruct toggle (EnableSelfDestruct/DisableSelfDestruct, also reachable
    ///     via the copy-settings tool) drives a local-only GameScheduler timer that eventually
    ///     calls the same unsynced OnDeconstruct -> a small packet syncs the willSelfDestruct flag
    ///     + remaining duration so every peer's local timer agrees.
    /// </summary>
    public static class ScaffoldsCompatPatches
    {
        public const string AssemblyName = "Scaffolds";
        public const string ScaffoldConfigType = "Scaffolds.ScaffoldConfig";
        public const string ScaffoldPrefabId = "Scaffold";
        public const string DeconstructableScaffoldType = "Scaffolds.DeconstructableScaffold";
        public const string ScaffoldType = "Scaffolds.Scaffold";

        public static void TryApply(Harmony harmony)
        {
            if (!ModPresence.IsAssemblyLoaded(AssemblyName) || !ModPresence.TypeExists(ScaffoldConfigType))
            {
                return;
            }

            InstantBuildFix.ApplyFor(harmony, ScaffoldPrefabId);
            CellMethodRelay.ApplyPostfix(harmony, DeconstructableScaffoldType, "OnDeconstruct");
            SelfDestructSyncPatches.Apply(harmony);
        }
    }
}
