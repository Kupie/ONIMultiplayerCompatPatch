using System.Collections.Generic;

namespace MultiplayerCompatPatch.Infrastructure
{
    /// <summary>
    /// Marks a cell as "currently being applied from the network" so the Postfix that would
    /// normally broadcast a local change can tell the difference between a local player action
    /// and us replaying a change that just arrived from a peer, and avoid re-broadcasting it
    /// (the same IsApplying-style guard ONI Together's own UserNameableChangePacket/ResearchPatch
    /// use around their receive-side handlers).
    /// </summary>
    public static class ReentrancyGuard
    {
        private static readonly HashSet<int> ApplyingCells = new HashSet<int>();

        public static bool IsApplying(int cell) => ApplyingCells.Contains(cell);

        public readonly struct Scope : System.IDisposable
        {
            private readonly int _cell;
            public Scope(int cell)
            {
                _cell = cell;
                ApplyingCells.Add(cell);
            }
            public void Dispose() => ApplyingCells.Remove(_cell);
        }
    }
}
