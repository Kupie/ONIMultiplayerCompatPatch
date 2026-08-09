using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MultiplayerCompatPatch.Infrastructure
{
    /// <summary>
    /// Fixes the instant-build desync shared by MoveThisHere (HaulingPoint) and Scaffolds
    /// (Scaffold): both mods Prefix BuildingDef.Instantiate and, for their one building, skip the
    /// normal ghost/construction flow entirely and call BuildingDef.Build(...) directly - so the
    /// building is fully finished the instant it's placed, for the player who placed it.
    ///
    /// ONI Together's BuildToolPatch.Postfix (Patches/ToolPatches/Build/BuildToolPatch.cs) runs
    /// after BuildTool.TryBuild and packages a BuildPacket sent to all other peers, computing its
    /// private InstantBuild field as:
    ///   DebugHandler.InstantBuildMode || (Game.Instance.SandboxModeActive &amp;&amp; SandboxToolParameterMenu.instance.settings.InstantBuild)
    /// Since neither of those is true during a normal placement, the packet always claims
    /// InstantBuild = false, so every other peer receives a queued construction ghost
    /// (BuildPacket.OnDispatched -&gt; QueueBuild -&gt; def.TryPlace(...)) instead of the finished
    /// building the placer actually has - a real, confirmed state divergence.
    ///
    /// BuildPacket's InstantBuild field is private with no public/internal accessor, so there is
    /// no direct way to force the packet itself. But ONI Together's own UtilityBuildPacket receive
    /// path uses the exact same trick this fix uses: temporarily flip the public vanilla
    /// DebugHandler.InstantBuildMode flag so BuildToolPatch.Postfix's own read of it computes
    /// InstantBuild = true, then restore it. We do this only for the specific tracked PrefabIDs,
    /// and only if it wasn't already true (i.e. never turn OFF a real debug/sandbox instant-build
    /// session that was legitimately in progress).
    ///
    /// The Prefix sets the flag before BuildTool.TryBuild's original body runs (so it's already
    /// true by the time BuildToolPatch's own Postfix reads it - Harmony runs prefixes, then the
    /// original, then all postfixes across every patch owner). The Finalizer - guaranteed by
    /// Harmony to run after every prefix/postfix from every patch owner, not just ours - restores
    /// the flag, so this can't leak `true` past a single TryBuild call regardless of what patch
    /// order ONI Together's own Postfix happens to run in.
    ///
    /// Verified from source (see NOTES.md); not yet exercised against a live game.
    /// </summary>
    internal static class InstantBuildFix
    {
        private static readonly HashSet<string> TrackedPrefabIds = new HashSet<string>(StringComparer.Ordinal);
        private static bool _patched;

        public static void ApplyFor(Harmony harmony, string prefabId)
        {
            TrackedPrefabIds.Add(prefabId);

            if (_patched)
            {
                return;
            }

            // String-targeted, not nameof(BuildTool.TryBuild): as of the current game build
            // (verified against decompiled source, see NOTES.md) TryBuild is private, so nameof
            // wouldn't compile here even though BuildTool itself is a hard-referenced vanilla type.
            var tryBuild = AccessTools.Method(typeof(BuildTool), "TryBuild", new[] { typeof(int) });
            if (tryBuild == null)
            {
                return;
            }

            harmony.Patch(
                tryBuild,
                prefix: new HarmonyMethod(typeof(InstantBuildFix), nameof(Prefix)),
                finalizer: new HarmonyMethod(typeof(InstantBuildFix), nameof(Finalizer)));
            _patched = true;
        }

        // ___def: BuildTool.def is a private field (verified against decompiled source) - Harmony's
        // underscore-prefixed injection reads it via reflection regardless of accessibility, same
        // idiom used for Research.queuedTech elsewhere in this mod.
        private static void Prefix(BuildingDef ___def, out bool __state)
        {
            __state = false;
            try
            {
                var prefabId = ___def != null ? ___def.PrefabID : null;
                if (prefabId != null && TrackedPrefabIds.Contains(prefabId) && !DebugHandler.InstantBuildMode)
                {
                    DebugHandler.InstantBuildMode = true;
                    __state = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MultiplayerCompatPatch] InstantBuildFix.Prefix failed: " + e);
            }
        }

        private static void Finalizer(bool __state)
        {
            if (__state)
            {
                DebugHandler.InstantBuildMode = false;
            }
        }
    }
}
