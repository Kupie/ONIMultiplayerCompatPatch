using System;
using System.IO;
using HarmonyLib;
using MultiplayerCompatPatch.Infrastructure;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together_API;
using ONI_Together_API.Networking;
using UnityEngine;

namespace MultiplayerCompatPatch.SignsTagsAndRibbonsCompat
{
    /// <summary>
    /// SelectableSign.SetVariant(string variant) is the single mutation point for the
    /// [Serialize] selectedIndex field - both SignSideScreen's buttons and
    /// SelectableSign.Blueprints_SetData funnel through it, confirmed by reading current source.
    /// One Postfix here, modeled on ONI Together's own UserNameableChangePacket/UserNameablePatch
    /// idiom (synchronous ApplyingPacket-style guard, cell-keyed instead of NetId-keyed per the
    /// task's addressing guidance), covers both paths.
    /// </summary>
    internal static class SignVariantSyncPatches
    {
        public static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName(SignsTagsAndRibbonsCompatPatches.SelectableSignType);
            var method = type == null ? null : AccessTools.Method(type, "SetVariant", new[] { typeof(string) });
            if (method == null)
            {
                return;
            }

            harmony.Patch(method, postfix: new HarmonyMethod(typeof(SignVariantSyncPatches), nameof(Postfix)));
        }

        // Parameter name "variant" must match SelectableSign.SetVariant's own parameter name for
        // Harmony to inject the value here.
        private static void Postfix(object __instance, string variant)
        {
            try
            {
                if (!(__instance is Component component))
                {
                    return;
                }
                if (!SessionInfoAPI.InSession)
                {
                    return;
                }

                int cell = Grid.PosToCell(component.gameObject);
                if (!Grid.IsValidCell(cell) || ReentrancyGuard.IsApplying(cell))
                {
                    return;
                }

                PacketSenderAPI.SendToAllOtherPeers(new SignVariantChangePacket(cell, variant));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MultiplayerCompatPatch] SignVariantSyncPatches.Postfix failed: " + e);
            }
        }
    }

    public sealed class SignVariantChangePacket : IPacket
    {
        public int Cell;
        public string Variant;

        public SignVariantChangePacket() { }

        public SignVariantChangePacket(int cell, string variant)
        {
            Cell = cell;
            Variant = variant;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Cell);
            writer.Write(Variant ?? string.Empty);
        }

        public void Deserialize(BinaryReader reader)
        {
            Cell = reader.ReadInt32();
            Variant = reader.ReadString();
        }

        public void OnDispatched()
        {
            try
            {
                var type = AccessTools.TypeByName(SignsTagsAndRibbonsCompatPatches.SelectableSignType);
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

                var method = AccessTools.Method(type, "SetVariant", new[] { typeof(string) });
                if (method == null)
                {
                    return;
                }

                using (new ReentrancyGuard.Scope(Cell))
                {
                    method.Invoke(component, new object[] { Variant });
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MultiplayerCompatPatch] SignVariantChangePacket.OnDispatched failed: " + e);
            }
        }
    }
}
