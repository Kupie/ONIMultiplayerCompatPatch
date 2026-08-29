using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together_API;
using ONI_Together_API.Networking;
using UnityEngine;

namespace MultiplayerCompatPatch.Infrastructure
{
    /// <summary>
    /// ONI Together's own NetId/NetworkIdentity addressing system is internal and not exposed via
    /// the public API - per the task brief, we don't reflect into it. Instead, like the task's
    /// "Addressing game objects across peers" guidance describes, every custom packet in this mod
    /// addresses a building by Grid.PosToCell(gameObject): deterministic and identical on host and
    /// client with no shared registry required.
    /// </summary>
    public static class CellAddressing
    {
        /// <summary>
        /// Layer-agnostic: scans every populated ObjectLayer at this cell for a GameObject
        /// carrying the given component type, rather than assuming ObjectLayer.Building. Custom
        /// mod buildings don't necessarily live on that layer - e.g. Scaffolds deliberately places
        /// Scaffold on ObjectLayer.FillPlacer specifically to avoid clashing with anything else
        /// (see ScaffoldConfig.ObjectLayer), which meant a hardcoded ObjectLayer.Building lookup
        /// here silently found nothing on the receiving peer and every cell-addressed packet for
        /// that building failed to apply (confirmed live: the "Remove" button, which routes through
        /// this lookup, never synced; the vanilla deconstruct order, which ONI Together addresses
        /// through its own NetId-based system instead of this one, did).
        /// </summary>
        public static GameObject FindBuildingWithComponentAt(int cell, Type componentType)
        {
            if (!Grid.IsValidCell(cell) || componentType == null)
            {
                return null;
            }

            int numLayers = (int)ObjectLayer.NumLayers;
            for (int layer = 0; layer < numLayers; layer++)
            {
                var go = Grid.Objects[cell, layer];
                if (go != null && go.GetComponent(componentType) != null)
                {
                    return go;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Generic relay for "a parameterless custom method on a cell-addressed building bypasses
    /// ONI Together's vanilla sync and needs to just be replayed identically on every other peer" -
    /// the shape shared by DeconstructableScaffold.OnDeconstruct and
    /// DeconstructableHaulingPoint.OnDeconstruct (both directly call gameObject.DeleteObject(),
    /// bypassing the vanilla deconstruct-order pipeline ONI Together's own deconstruct patches
    /// expect). CellMethodRelay.ApplyPostfix Postfixes the target method (found by string, so a
    /// missing target mod is a no-op) to broadcast a CellMethodInvokePacket; the receiving peer
    /// looks the type back up locally (if it doesn't have the owning mod, it just can't act on it -
    /// no crash) and invokes the same method, guarded by ReentrancyGuard so the replay doesn't
    /// re-broadcast.
    /// </summary>
    public static class CellMethodRelay
    {
        private static readonly Dictionary<MethodBase, (string TypeName, string MethodName)> Targets =
            new Dictionary<MethodBase, (string, string)>();

        public static void ApplyPostfix(Harmony harmony, string typeName, string methodName)
        {
            var type = AccessTools.TypeByName(typeName);
            var method = type == null ? null : AccessTools.Method(type, methodName);
            if (method == null)
            {
                return;
            }

            harmony.Patch(method, postfix: new HarmonyMethod(typeof(CellMethodRelay), nameof(Postfix)));
            Targets[method] = (typeName, methodName);
        }

        private static void Postfix(object __instance, MethodBase __originalMethod)
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
                if (!Targets.TryGetValue(__originalMethod, out var info))
                {
                    return;
                }

                PacketSenderAPI.SendToAllOtherPeers(new CellMethodInvokePacket(cell, info.TypeName, info.MethodName));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MultiplayerCompatPatch] CellMethodRelay.Postfix failed: " + e);
            }
        }
    }

    public sealed class CellMethodInvokePacket : IPacket
    {
        public int Cell;
        public string TypeName;
        public string MethodName;

        public CellMethodInvokePacket() { }

        public CellMethodInvokePacket(int cell, string typeName, string methodName)
        {
            Cell = cell;
            TypeName = typeName;
            MethodName = methodName;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Cell);
            writer.Write(TypeName ?? string.Empty);
            writer.Write(MethodName ?? string.Empty);
        }

        public void Deserialize(BinaryReader reader)
        {
            Cell = reader.ReadInt32();
            TypeName = reader.ReadString();
            MethodName = reader.ReadString();
        }

        public void OnDispatched()
        {
            try
            {
                var type = AccessTools.TypeByName(TypeName);
                if (type == null)
                {
                    // This peer doesn't have the owning mod installed - nothing we can replay.
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

                var method = AccessTools.Method(type, MethodName);
                if (method == null)
                {
                    return;
                }

                using (new ReentrancyGuard.Scope(Cell))
                {
                    method.Invoke(component, null);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MultiplayerCompatPatch] CellMethodInvokePacket.OnDispatched failed: " + e);
            }
        }
    }
}
