using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together_API;
using UnityEngine;

namespace MultiplayerCompatPatch.ResearchQueueCompat
{
    /// <summary>
    /// Client -> host request for a shift-clicked ("queue this / un-queue this") research entry.
    /// Only ever sent for shift-held clicks - plain clicks are already handled correctly by ONI
    /// Together's own ResearchRequestPacket once ResearchQueue's local mutation is neutralized by
    /// ResearchQueueClientRedirectPatches, so this packet only needs to express the one thing that
    /// path can't: queue-append/remove.
    ///
    /// Only the host acts on this (SessionInfoAPI.IsHost). The host-side handler mirrors
    /// ResearchQueue's own AddTechToQueue/click semantics: for a not-yet-queued tech, it invokes
    /// Research's actual (private) AddTechToQueue via reflection - Harmony's patch on it runs
    /// transparently for any caller, direct or reflected, exactly like ResearchQueue's own code
    /// relies on for its detoured field access - so the dependency-recursive queueing behavior is
    /// preserved; if ResearchQueue isn't installed on the host, this degrades to vanilla's
    /// single-item AddTechToQueue instead of crashing.
    ///
    /// Known simplification, not yet verified live: unlike ResearchQueue's own RemoveResearch,
    /// which recursively removes a tech's *unlocked* dependents too when un-queueing, this only
    /// removes the single clicked tech. See NOTES.md.
    ///
    /// Because this bottoms out in the same Research.SetActiveResearch/AddTechToQueue calls ONI
    /// Together's own ResearchPatch Postfix already watches (Postfix, host-authoritative,
    /// broadcasts full state after every SetActiveResearch), we don't need our own broadcast for
    /// the resulting queue contents - only this client -> host request.
    /// </summary>
    public sealed class ResearchQueueActionRequestPacket : IPacket
    {
        public string TechId;

        public ResearchQueueActionRequestPacket() { }

        public ResearchQueueActionRequestPacket(string techId)
        {
            TechId = techId;
        }

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(TechId ?? string.Empty);
        }

        public void Deserialize(BinaryReader reader)
        {
            TechId = reader.ReadString();
        }

        public void OnDispatched()
        {
            try
            {
                if (!SessionInfoAPI.IsHost)
                {
                    return;
                }

                var research = Research.Instance;
                if (research == null || string.IsNullOrEmpty(TechId))
                {
                    return;
                }

                // TryGet, not Get: Get logs its own "Could not find" error on a miss, which would
                // double up with our own warning below for no benefit.
                var tech = Db.Get().Techs.TryGet(TechId);
                if (tech == null)
                {
                    Debug.LogWarning("[MultiplayerCompatPatch] ResearchQueueActionRequestPacket: unknown tech id " + TechId);
                    return;
                }

                var queueField = AccessTools.Field(typeof(Research), "queuedTech");
                var queuedTech = queueField?.GetValue(research) as List<TechInstance>;
                int existingIndex = queuedTech?.FindIndex(t => t.tech.Id == TechId) ?? -1;

                if (existingIndex >= 0)
                {
                    queuedTech.RemoveAt(existingIndex);
                    var newActive = queuedTech.Count > 0 ? queuedTech[queuedTech.Count - 1].tech : null;
                    research.SetActiveResearch(newActive, false);
                }
                else
                {
                    var addTechToQueue = AccessTools.Method(typeof(Research), "AddTechToQueue", new[] { typeof(Tech) });
                    addTechToQueue?.Invoke(research, new object[] { tech });
                    research.SetActiveResearch(tech, false);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MultiplayerCompatPatch] ResearchQueueActionRequestPacket.OnDispatched failed: " + e);
            }
        }
    }
}
