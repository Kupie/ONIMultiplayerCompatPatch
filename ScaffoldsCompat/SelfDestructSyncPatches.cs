using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MultiplayerCompatPatch.Infrastructure;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together_API;
using ONI_Together_API.Networking;
using UnityEngine;

namespace MultiplayerCompatPatch.ScaffoldsCompat
{
    /// <summary>
    /// Scaffold.EnableSelfDestruct()/DisableSelfDestruct() (both private, no args) are the sole
    /// mutation points for the willSelfDestruct flag and the GameScheduler timer that eventually
    /// calls the same unsynced DeconstructableScaffold.OnDeconstruct fixed by CellMethodRelay. Both
    /// the "Remove"-style toggle user-menu button AND the copy/paste-settings tool
    /// (Scaffold.OnCopySettings) funnel through these two methods, so patching them here covers
    /// both without needing a separate patch for OnCopySettings.
    ///
    /// We deliberately don't re-invoke EnableSelfDestruct/DisableSelfDestruct on the receiving
    /// peer (that would need a re-entrancy guard and would recompute the duration from that peer's
    /// own possibly-different mod settings). Instead we call the lower-level, non-Harmony-patched
    /// scheduleDeconstruct(float)/unscheduleDeconstruct() directly and set the willSelfDestruct
    /// field ourselves, replaying the sender's exact remaining duration - since those methods are
    /// never themselves patched, there's no re-entrancy loop to guard against.
    /// </summary>
    internal static class SelfDestructSyncPatches
    {
        public static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName(ScaffoldsCompatPatches.ScaffoldType);
            if (type == null)
            {
                return;
            }

            var enable = AccessTools.Method(type, "EnableSelfDestruct");
            var disable = AccessTools.Method(type, "DisableSelfDestruct");
            if (enable != null)
            {
                harmony.Patch(enable, postfix: new HarmonyMethod(typeof(SelfDestructSyncPatches), nameof(EnablePostfix)));
            }
            if (disable != null)
            {
                harmony.Patch(disable, postfix: new HarmonyMethod(typeof(SelfDestructSyncPatches), nameof(DisablePostfix)));
            }
        }

        private static void EnablePostfix(object __instance) => Broadcast(__instance, true);

        private static void DisablePostfix(object __instance) => Broadcast(__instance, false);

        private static void Broadcast(object instance, bool willSelfDestruct)
        {
            try
            {
                if (!(instance is Component component))
                {
                    return;
                }
                if (!SessionInfoAPI.InSession)
                {
                    return;
                }

                int cell = Grid.PosToCell(component.gameObject);
                if (!Grid.IsValidCell(cell))
                {
                    return;
                }

                float remaining = 0f;
                if (willSelfDestruct)
                {
                    var moment = AccessTools.Field(instance.GetType(), "deconstructMoment");
                    if (moment != null && GameClock.Instance != null)
                    {
                        remaining = (float)moment.GetValue(instance) - GameClock.Instance.GetTime();
                    }
                }

                PacketSenderAPI.SendToAllOtherPeers(new ScaffoldSelfDestructTogglePacket(cell, willSelfDestruct, remaining));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MultiplayerCompatPatch] SelfDestructSyncPatches.Broadcast failed: " + e);
            }
        }
    }

    public sealed class ScaffoldSelfDestructTogglePacket : IPacket
    {
        public int Cell;
        public bool WillSelfDestruct;
        public float RemainingSeconds;

        public ScaffoldSelfDestructTogglePacket() { }

        public ScaffoldSelfDestructTogglePacket(int cell, bool willSelfDestruct, float remainingSeconds)
        {
            Cell = cell;
            WillSelfDestruct = willSelfDestruct;
            RemainingSeconds = remainingSeconds;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Cell);
            writer.Write(WillSelfDestruct);
            writer.Write(RemainingSeconds);
        }

        public void Deserialize(BinaryReader reader)
        {
            Cell = reader.ReadInt32();
            WillSelfDestruct = reader.ReadBoolean();
            RemainingSeconds = reader.ReadSingle();
        }

        public void OnDispatched()
        {
            try
            {
                var type = AccessTools.TypeByName(ScaffoldsCompatPatches.ScaffoldType);
                if (type == null)
                {
                    return;
                }

                var go = CellAddressing.FindBuildingWithComponentAt(Cell, type);
                if (go == null)
                {
                    return;
                }

                var component = go.GetComponent(type);
                if (component == null)
                {
                    return;
                }

                var flagField = AccessTools.Field(type, "willSelfDestruct");

                if (WillSelfDestruct)
                {
                    var schedule = AccessTools.Method(type, "scheduleDeconstruct", new[] { typeof(float) });
                    schedule?.Invoke(component, new object[] { RemainingSeconds });
                }
                else
                {
                    var unschedule = AccessTools.Method(type, "unscheduleDeconstruct");
                    unschedule?.Invoke(component, null);
                }

                flagField?.SetValue(component, WillSelfDestruct);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MultiplayerCompatPatch] ScaffoldSelfDestructTogglePacket.OnDispatched failed: " + e);
            }
        }
    }
}
