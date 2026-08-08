# Verification notes

This mod was written against fresh `--depth=1` clones of all five repos, pulled while writing this
code. Snapshot dates / commit context: cloned 2026-08-08. ONI Together is pre-alpha and moves fast -
re-read this file's "inferred, not tested" callouts before trusting this mod against a newer ONI
Together build, and re-verify method/field names against current `main` if anything here throws a
`MissingMethodException`/`AmbiguousMatchException` at runtime.

**Big caveat: everything in this mod is verified by source-reading only.** This container has no
Oxygen Not Included install and no .NET SDK reachable through the network proxy (`dotnet-install`
and `apt-get install dotnet-sdk` both failed - outbound access to `dot.net`/`security.ubuntu.com`
package mirrors is blocked here), so **none of this has been compiled, let alone run with two
peers**. Build this on a machine with ONI installed and `ONIPath` pointed at it, then playtest
host+client for every scenario below before trusting it. Everything marked "inferred" is a best
effort from reading source side by side; it is exactly the kind of thing the task brief asked to
empirically verify and I could not.

## Confirmed from source (this snapshot)

- `MoveThisHere.HaulingPoint : KMonoBehaviour, ISim1000ms, ISingleSliderControl` (assembly
  `MoveThisHere.dll`) - the slider is genuinely `ISingleSliderControl`, no custom side screen exists
  in this mod, so it should ride ONI Together's generic `SingleSliderSideScreen` sync for free.
  **Inferred, not tested**: whether ONI Together's generic sync actually round-trips a
  `ISingleSliderControl` implementor cleanly for this specific building - the two-arg
  `SetSliderValue(float value, int index)` also triggers `filteredStorage.FilterChanged()` as a
  side effect, and I have not confirmed ONI Together's replay path doesn't double-fire it in a way
  that matters.
- `MoveThisHere.MoveThisHere_Patch.BuildingDef_Instantiate_Patch` Prefixes `BuildingDef.Instantiate`,
  keyed on `__instance.PrefabID != HaulingPointConfig.Id` ("HaulingPoint"), and calls
  `__instance.Build(...)` directly, returning `false` - confirmed instant-build-on-placement, no
  ghost ever exists for this building.
- `MoveThisHere` also has its own unsynced self-destruct/auto-deconstruct path
  (`HaulingPoint.Sim1000ms` calling `DeconstructableHaulingPoint.OnDeconstruct()` directly when
  storage nears full) that the original task brief didn't call out - same shape of risk as
  Scaffolds' self-destruct. See `MoveThisHereCompat/NOTES` below.
- `Scaffolds.Scaffolds_Patch.ScaffoldsPatches.BuildingDef_Instantiate_Patch` Prefixes
  `BuildingDef.Instantiate`, keyed on `__instance.name != "Scaffold"` (note: `BuildingDef.name`, not
  `PrefabID` - happens to hold the same string), same instant-build-via-`.Build()` shape as
  MoveThisHere.
- `Scaffolds.DeconstructableScaffold.OnDeconstruct()` calls `gameObject.DeleteObject()` directly,
  confirmed no vanilla deconstruct order pipeline involved. Wired to the "Remove" user-menu button
  AND to `Scaffold`'s self-destruct `GameScheduler` timer AND to `Scaffold.OnCopySettings` (a third,
  previously-undocumented mutation path via the copy/paste-settings tool).
- `SignsTagsAndRibbons.SelectableSign.SetVariant(string variant)` is the single mutation point for
  `selectedIndex` (`[Serialize]` field) - both the custom `SignSideScreen` buttons and
  `Blueprints_SetData` funnel through it, confirmed a Postfix on this one method covers both paths.
  Twelve distinct sign PrefabIDs share this component (`AlertTag`, `GasTag`, `GeyserTag`, `InfoTag`,
  `LetterTag`, `LiquidTag`, `LocationTag`, `NumbersTag`, `SmallElementTag`, `SolidTag`, `SystemTag`,
  `UtilityTag`); `DangerRibbon`/`DangerRibbonCorner`/`Meter_Scale` only use `UserNameable` and are
  unaffected by this patch (and should already be synced by ONI Together's generic
  `UserNameableChangePacket`).
- `PeterHan.ResearchQueue.ResearchQueuePatches` Prefixes `ResearchEntry.OnResearchClicked`
  (returning `false` in essentially all normal gameplay conditions) and `Research.AddTechToQueue`
  (also always returning `false`), and mutates vanilla's own `queuedTech` list in place - it does
  **not** keep a separate queue data structure, so there's a single vanilla list to reconcile with
  ONI Together, not two competing models. `Research.SetActiveResearch` is also transpiled (a
  `List<TechInstance>.Sort` call is stripped from its IL) to stop vanilla re-sorting the queue.
- `SavedResearchQueue.OnDeserialized` unconditionally calls `Research.Instance.SetActiveResearch(...)`
  and re-walks the queue on every local KSerialization load of the `SaveGame` object, with no
  host/client distinction. **Inferred, not tested**: whether ONI Together's save/sync flow causes
  every peer to run this independently after a synced load, and whether that's actually a problem
  given ONI Together's own `ResearchPatch` Postfix will also fire and broadcast state right after.

## Design decisions and why

- **Detection**: `Infrastructure.ModPresence` gates every compat sub-module on both
  `MP_Mod_Info.MultiplayerModPresent` and an assembly-name/type-existence probe for that specific
  target mod, so any subset of the 4 target mods can be installed/absent independently and this mod
  never throws on a missing one.
- **ResearchQueue**: client-side, we don't fight over `OnResearchClicked` - we let ResearchQueue's
  own click patch run locally (queue display, etc.) but redirect the actual mutation
  (`Research.AddTechToQueue` / `SetActiveResearch`/`CancelResearch`) through a client->host request
  packet when `SessionInfoAPI.IsClient`, and only actually invoke ResearchQueue's private
  `Research.AddTechToQueue(Tech)` (via reflection, see ResearchQueueCompat) on the host, so ONI
  Together's existing host-authoritative `ResearchPatch` Postfix on `SetActiveResearch` picks it up
  and distributes it for free.
- **Scaffolds / MoveThisHere instant-build**: rather than reaching into ONI Together's private
  `BuildPacket` internals from a Postfix timing race, this mod force-sets the `InstantBuild` flag by
  patching at the point ONI Together itself packages the packet (see `Infrastructure` /
  `*Compat/*InstantBuildFix.cs` for the exact patch target and field name actually found in this
  snapshot).
