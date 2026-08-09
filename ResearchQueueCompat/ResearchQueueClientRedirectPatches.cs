using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MultiplayerCompatPatch.Infrastructure;
using ONI_Together_API;
using ONI_Together_API.Networking;
using UnityEngine;

namespace MultiplayerCompatPatch.ResearchQueueCompat
{
    /// <summary>
    /// ResearchQueue and ONI Together both Harmony-patch ResearchEntry.OnResearchClicked. Harmony
    /// runs every prefix registered on a method regardless of what any other prefix on that same
    /// method returns - a false return only ever controls whether the *original* method body runs,
    /// never whether a sibling patch's own prefix body executes. That means on a client, ONI
    /// Together's own prefix (which suppresses the original and sends a host-authoritative
    /// ResearchRequestPacket) does NOT stop ResearchQueue's own prefix from also running and
    /// mutating Research's private queuedTech list locally and unsynced - a real, confirmed source
    /// of desync (see NOTES.md).
    ///
    /// Rather than fight over OnResearchClicked itself (which, per the above, is a fight we cannot
    /// win by patching that method), this intercepts one level lower, at the actual vanilla
    /// mutation points ResearchQueue drives:
    ///
    ///  - Research.AddTechToQueue(Tech) is private and ResearchQueue Prefixes it with its own
    ///    unconditional `return false`, meaning ResearchQueue's real mutation happens *inside its
    ///    own prefix body*, not in the original method - so we can't block it with a competing
    ///    prefix's return value either. Instead we snapshot the queuedTech list immediately before
    ///    (highest-priority prefix) and restore it immediately after (a Finalizer, which Harmony
    ///    guarantees runs after every prefix/postfix from every patch owner, regardless of patch
    ///    load order) - but only while we're inside a local client click
    ///    (_suppressingLocalClick), so this never touches the legitimate receive-side mutation that
    ///    happens when this client applies the host's authoritative ResearchStatePacket.
    ///  - Research.SetActiveResearch / Research.CancelResearch are public vanilla methods
    ///    ResearchQueue only *observes* via Postfix (UI relabeling only, confirmed from source) -
    ///    it never Prefixes them, so a plain Prefix returning false from us fully and reliably
    ///    blocks their real effect while we're inside a local client click.
    ///
    /// For a plain (non-shift) click, ONI Together's own existing ResearchRequestPacket -> host
    /// SetActiveResearch(tech, clearQueue: true) already produces exactly the right multiplayer
    /// behavior once ResearchQueue's redundant local mutation is neutralized above - no packet of
    /// our own needed. For a shift click (queue-append/remove, which ONI Together's single-TechId
    /// ResearchRequestPacket has no way to express - confirmed: it always hardcodes
    /// clearQueue: true), we additionally send our own ResearchQueueActionRequestPacket to the
    /// host, and best-effort suppress ONI Together's own ResearchRequestPacket for that one click
    /// (see SendToHostGuard) so the two don't race and clobber each other on the host.
    /// </summary>
    internal static class ResearchQueueClientRedirectPatches
    {
        private static bool _suppressingLocalClick;
        private static bool _currentShiftHeld;
        private static List<TechInstance> _queueSnapshot;

        public static void Apply(Harmony harmony)
        {
            var researchEntryType = typeof(ResearchEntry);
            var clickMethod = AccessTools.Method(researchEntryType, "OnResearchClicked");
            if (clickMethod != null)
            {
                harmony.Patch(
                    clickMethod,
                    prefix: new HarmonyMethod(typeof(ResearchQueueClientRedirectPatches), nameof(OnResearchClicked_Prefix)) { priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(ResearchQueueClientRedirectPatches), nameof(OnResearchClicked_Finalizer)));
            }

            var addTechMethod = AccessTools.Method(typeof(Research), "AddTechToQueue", new[] { typeof(Tech) });
            if (addTechMethod != null)
            {
                harmony.Patch(
                    addTechMethod,
                    prefix: new HarmonyMethod(typeof(ResearchQueueClientRedirectPatches), nameof(SnapshotQueue_Prefix)) { priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(ResearchQueueClientRedirectPatches), nameof(RestoreQueue_Finalizer)));
            }

            var setActiveMethod = AccessTools.Method(typeof(Research), nameof(Research.SetActiveResearch));
            if (setActiveMethod != null)
            {
                harmony.Patch(
                    setActiveMethod,
                    prefix: new HarmonyMethod(typeof(ResearchQueueClientRedirectPatches), nameof(SuppressIfLocalClick_Prefix)) { priority = Priority.First });
            }

            var cancelMethod = AccessTools.Method(typeof(Research), nameof(Research.CancelResearch));
            if (cancelMethod != null)
            {
                harmony.Patch(
                    cancelMethod,
                    prefix: new HarmonyMethod(typeof(ResearchQueueClientRedirectPatches), nameof(SuppressIfLocalClick_Prefix)) { priority = Priority.First });
            }

            SendToHostGuard.TryApply(harmony);
        }

        // ___targetTech: ResearchEntry.targetTech is a private field (verified against decompiled
        // source) - Harmony's underscore-prefixed injection reads it via reflection regardless of
        // accessibility.
        private static bool OnResearchClicked_Prefix(Tech ___targetTech)
        {
            _suppressingLocalClick = false;
            _currentShiftHeld = false;

            try
            {
                if (!SessionInfoAPI.InSession || !SessionInfoAPI.IsClient)
                {
                    return true;
                }

                var tech = ___targetTech;
                if (tech == null)
                {
                    return true;
                }

                _suppressingLocalClick = true;
                _currentShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                if (_currentShiftHeld)
                {
                    // ONI Together's own ResearchRequestPacket can only express "set active and
                    // clear queue" - no queue-append/remove semantics - so we drive the host
                    // ourselves for this case (SendToHostGuard best-effort suppresses ONI
                    // Together's own packet for this one click so the two don't race).
                    PacketSenderAPI.SendToHost(new ResearchQueueActionRequestPacket(tech.Id));
                }
                // Plain clicks: let ONI Together's own redirect handle it - it's already correct
                // once ResearchQueue's local mutation below is neutralized.
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MultiplayerCompatPatch] OnResearchClicked_Prefix failed: " + e);
            }

            return true;
        }

        private static void OnResearchClicked_Finalizer()
        {
            _suppressingLocalClick = false;
            _currentShiftHeld = false;
        }

        private static void SnapshotQueue_Prefix(List<TechInstance> ___queuedTech)
        {
            if (_suppressingLocalClick && ___queuedTech != null)
            {
                _queueSnapshot = new List<TechInstance>(___queuedTech);
            }
        }

        private static void RestoreQueue_Finalizer(List<TechInstance> ___queuedTech)
        {
            if (_queueSnapshot != null && ___queuedTech != null)
            {
                ___queuedTech.Clear();
                ___queuedTech.AddRange(_queueSnapshot);
            }
            _queueSnapshot = null;
        }

        private static bool SuppressIfLocalClick_Prefix()
        {
            return !_suppressingLocalClick;
        }

        internal static bool IsSuppressingShiftClick => _suppressingLocalClick && _currentShiftHeld;

        /// <summary>
        /// Best-effort: patches ONI Together's internal PacketSender.SendToHost (not part of the
        /// public API, so this is exactly the kind of "reflect into a private internal" the task
        /// brief itself calls for on the InstantBuild fix) to drop ONI Together's own
        /// ResearchRequestPacket send during a shift-click we're already handling ourselves. If
        /// this method can't be resolved (ONI Together internals moved), this silently no-ops and
        /// shift-clicks fall back to racing both packets - see NOTES.md, this is the single most
        /// fragile piece of this mod and needs to be re-verified against current ONI Together
        /// source before being trusted.
        /// </summary>
        private static class SendToHostGuard
        {
            public static void TryApply(Harmony harmony)
            {
                try
                {
                    var senderType = AccessTools.TypeByName("ONI_Together.Networking.PacketSender");
                    var method = senderType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefaultSafe(m => m.Name == "SendToHost");
                    if (method == null)
                    {
                        return;
                    }

                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(SendToHostGuard), nameof(Prefix)));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[MultiplayerCompatPatch] SendToHostGuard.TryApply failed: " + e);
                }
            }

            private static bool Prefix(object packet)
            {
                if (!IsSuppressingShiftClick || packet == null)
                {
                    return true;
                }
                return packet.GetType().Name != "ResearchRequestPacket";
            }
        }
    }

    internal static class EnumerableExtensions
    {
        public static MethodInfo FirstOrDefaultSafe(this MethodInfo[] methods, Func<MethodInfo, bool> predicate)
        {
            foreach (var m in methods)
            {
                if (predicate(m))
                {
                    return m;
                }
            }
            return null;
        }
    }
}
