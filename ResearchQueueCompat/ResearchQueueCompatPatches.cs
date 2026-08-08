using HarmonyLib;
using MultiplayerCompatPatch.Infrastructure;

namespace MultiplayerCompatPatch.ResearchQueueCompat
{
    /// <summary>
    /// Compat shims for peterhaneve/ONIMods' ResearchQueue.
    ///
    /// ResearchQueue and ONI Together both patch ResearchEntry.OnResearchClicked, and ONI Together
    /// additionally patches Research.SetActiveResearch (Postfix, host-authoritative full-state
    /// broadcast) and Research.AddTechToQueue is where ResearchQueue itself does its real queueing
    /// work (mutating vanilla's own queuedTech list in place - there is no separate ResearchQueue
    /// data structure to reconcile).
    ///
    /// Rather than fight over OnResearchClicked's patch ordering, this module intercepts one level
    /// lower: on a non-host client, it prevents the local mutation of Research.queuedTech /
    /// active research from taking effect at all, and instead sends a small compat request packet
    /// to the host describing the intended action. The host-side handler invokes ResearchQueue's
    /// own Research.AddTechToQueue (via reflection - Harmony's patch on it will run since it's the
    /// same process) so the queueing logic runs with host authority. Because that call bottoms out
    /// in Research.SetActiveResearch either way, ONI Together's own existing ResearchPatch Postfix
    /// picks up the resulting state change and broadcasts it to all clients - this module does not
    /// need to build its own broadcast for queue contents, only the client->host request.
    /// </summary>
    public static class ResearchQueueCompatPatches
    {
        public const string AssemblyName = "ResearchQueue";
        public const string ResearchQueuePatchesType = "PeterHan.ResearchQueue.ResearchQueuePatches";

        public static void TryApply(Harmony harmony)
        {
            if (!ModPresence.IsAssemblyLoaded(AssemblyName) || !ModPresence.TypeExists(ResearchQueuePatchesType))
            {
                return;
            }

            ResearchQueueClientRedirectPatches.Apply(harmony);
        }
    }
}
