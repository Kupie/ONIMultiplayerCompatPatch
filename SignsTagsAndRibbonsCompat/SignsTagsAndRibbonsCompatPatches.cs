using HarmonyLib;
using MultiplayerCompatPatch.Infrastructure;

namespace MultiplayerCompatPatch.SignsTagsAndRibbonsCompat
{
    /// <summary>
    /// Compat shims for pether-pg/ONI_Mods_byPether's SignsTagsAndRibbons.
    ///
    /// Building placement is vanilla and UserNameable renaming is already synced generically by
    /// ONI Together - no patch needed for either. The one real gap: SelectableSign's variant
    /// picker (SignSideScreen, a fully custom SideScreenContent) mutates a local-only
    /// [Serialize] selectedIndex field with no network awareness. Both the side-screen buttons and
    /// blueprint paste (Blueprints_SetData) funnel through the single method
    /// SelectableSign.SetVariant(string), so one Postfix there, with a re-entrancy guard, covers
    /// both paths.
    /// </summary>
    public static class SignsTagsAndRibbonsCompatPatches
    {
        public const string AssemblyName = "SignsTagsAndRibbons";
        public const string SelectableSignType = "SignsTagsAndRibbons.SelectableSign";

        public static void TryApply(Harmony harmony)
        {
            if (!ModPresence.IsAssemblyLoaded(AssemblyName) || !ModPresence.TypeExists(SelectableSignType))
            {
                return;
            }

            SignVariantSyncPatches.Apply(harmony);
        }
    }
}
