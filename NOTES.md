# Verification notes

Written against fresh `--depth=1` clones of all five repos and the real `ONI_Together_API` NuGet
package, pulled 2026-08-08. ONI Together is pre-alpha and moves fast - re-read the "needs live
testing" section before trusting this against a newer ONI Together build, and re-verify anything
that throws a `MissingMethodException`/`AmbiguousMatchException` at runtime.

## What's actually been verified, and how

Two independent passes, not just reading source:

1. **Source reading** of all five repos (target mods + ONI Together), confirming every class,
   method, field name and packet shape this mod depends on, string-quoted from the actual current
   files.
2. **A real, successful compile.** This sandbox has no .NET SDK reachable (Microsoft's dotnet CDN,
   `builds.dotnet.microsoft.com`/`ci.dot.net`, is blocked by the outbound proxy here - both
   `dotnet-install.sh` and `apt-get install dotnet-sdk` failed) and no ONI install. But
   `apt-get install mono-mcs` *is* reachable, and so is `api.nuget.org` - so every `.cs` file in
   this project was compiled with the Mono C# compiler against: the real `ONI_Together_API.dll`
   downloaded straight from its nuget.org package (`0.7.2-alpha.0.34`, the current published
   version), and real Klei game assemblies (`Assembly-CSharp(-firstpass)_public.dll`,
   `0Harmony.dll`, `UnityEngine*.dll` - the "_public" ones are a publicized/de-obfuscated variant
   bundled in peterhaneve/ONIMods' own repo under `ResearchQueue/Lib`, which expose the same member
   visibility a real ONI mod project compiles against). **This compiled clean, zero errors, zero
   warnings**, after fixing several real mistakes the compiler caught that source-reading alone
   missed:
   - `BuildingDef.PrefabID` is `string`, not `Tag` - my first draft called `.Name` on it.
   - `Research.CancelResearch` takes `(Tech, bool)`, not zero args - the peterhaneve source I'd read
     called `screen.CancelResearch()`, a *different*, zero-arg method on `ResearchScreen`, and I'd
     conflated the two. Fixed by simplifying `ResearchQueueActionRequestPacket` to only ever cover
     the queue-toggle case it's actually sent for (see below) rather than guessing at the 2-arg
     semantics for a code path that can't currently be reached.
   - `netstandard2.1` vs `net471`: **this is the important one.** `ONI_Together_API` (and ONI
     Together's own main mod project, confirmed from its `Directory.Build.props`) targets
     `netstandard2.1`. Classic .NET Framework (`net471`/`net472`, what Scaffolds/ResearchQueue/most
     Harmony-only ONI mods target) *cannot* restore a `netstandard2.1` `PackageReference` at all -
     this is the exact "PackageReference friction" the task brief warned about. This project now
     targets `netstandard2.1` too, matching ONI Together's own convention; Unity/ONI's Mono runtime
     loads the result the same way it loads ONI Together itself, and raw `<Reference HintPath=...>`
     access to the classic-Framework-style game DLLs is unaffected by the project's own TFM.
   - `ResearchEntry.targetTech` and `ResearchEntry.OnResearchClicked()` are confirmed to exist
     exactly as expected (a first pass compiled against the *non*-publicized `Assembly-CSharp.dll`
     variant in that same Lib folder and couldn't find them at all - that DLL appears to be some
     other/mismatched build and was a red herring, not a real finding; the publicized variant, and
     by extension a real ONI install's DLLs, has them).

None of this replaces actually loading the mod in ONI with ONI Together and two peers - Harmony
patch *ordering* between this mod, ResearchQueue, and ONI Together in particular can only be
observed at runtime, not by compiling. But "does this code even reference real, current members
correctly" is no longer a source-reading guess for any file in this project.

## Confirmed from source, by mod

- **MoveThisHere**: `HaulingPoint : KMonoBehaviour, ISim1000ms, ISingleSliderControl` - no custom
  side screen, rides ONI Together's generic `SingleSliderSideScreen` sync. **Needs live
  verification**: does the round-trip actually work cleanly given `SetSliderValue` also triggers
  `filteredStorage.FilterChanged()` as a side effect?
  `MoveThisHere_Patch.BuildingDef_Instantiate_Patch` Prefixes `BuildingDef.Instantiate`
  (`__instance.PrefabID != HaulingPointConfig.Id`) and calls `.Build(...)` directly - confirmed
  instant-build-on-placement, fixed by `InstantBuildFix`.
  Also found beyond the original task brief: `HaulingPoint.Sim1000ms` calls
  `DeconstructableHaulingPoint.OnDeconstruct()` (which itself calls `gameObject.DeleteObject()`)
  directly when storage nears full - same unsynced-deletion shape as Scaffolds, fixed the same way
  via `CellMethodRelay`.
- **Scaffolds**: `Scaffolds_Patch.ScaffoldsPatches.BuildingDef_Instantiate_Patch` keyed on
  `__instance.name != "Scaffold"` (note: `BuildingDef.name`, not `PrefabID` - same string value in
  practice). `DeconstructableScaffold.OnDeconstruct()` calls `gameObject.DeleteObject()` directly,
  confirmed no vanilla deconstruct pipeline involved - wired to the "Remove" button AND
  `Scaffold`'s self-destruct `GameScheduler` timer AND `Scaffold.OnCopySettings` (copy/paste-
  settings tool) - all three funnel through the same `OnDeconstruct`/`EnableSelfDestruct`/
  `DisableSelfDestruct` methods this mod patches, so all three are covered without a separate patch
  for `OnCopySettings`.
- **SignsTagsAndRibbons**: `SelectableSign.SetVariant(string variant)` is the single mutation point
  for `selectedIndex` - both `SignSideScreen`'s buttons and `Blueprints_SetData` funnel through it.
  12 sign PrefabIDs share this component; `DangerRibbon`/`DangerRibbonCorner`/`Meter_Scale` only use
  `UserNameable` (already synced generically by ONI Together) and are unaffected.
- **ResearchQueue**: Prefixes `ResearchEntry.OnResearchClicked` (returns `false` in essentially all
  normal conditions) and `Research.AddTechToQueue` (also always `false`, mutating vanilla's own
  `queuedTech` list *inside its own prefix body*, not the original method) - no separate queue data
  structure. `Research.SetActiveResearch`/`CancelResearch` are only Postfixed (UI relabeling), never
  Prefixed, by ResearchQueue.
  **The core Harmony fact this mod's entire ResearchQueue fix rests on**: when two mods both patch
  the same method, a `false` return from one prefix only ever controls whether the *original*
  method body runs - it does not, and cannot, stop a sibling prefix (from a different patch owner)
  from running its own body. This is why `ResearchQueueClientRedirectPatches` doesn't try to fight
  ONI Together over `OnResearchClicked` itself; it snapshots/restores `Research.queuedTech` around
  `AddTechToQueue` (a Finalizer, which Harmony *does* guarantee runs after every prefix/postfix from
  every patch owner) and Prefix-blocks `SetActiveResearch`/`CancelResearch` directly (safe because
  ResearchQueue never Prefixes those two).

## Design decisions and why

- **Detection**: every compat sub-module is gated on both `MP_Mod_Info.MultiplayerModPresent` and
  an assembly-name/type-existence probe (`Infrastructure.ModPresence`) for that specific target mod,
  so any subset of the 4 target mods can be installed/absent independently.
- **Addressing**: per the task's own guidance, ONI Together's NetId/`NetworkIdentity` system is
  internal and not reflected into. Every custom packet in this mod addresses buildings by
  `Grid.PosToCell(gameObject)` alone (`Infrastructure.CellAddressing`) - deterministic and identical
  on host and client with no shared registry.
- **Re-entrancy**: `Infrastructure.ReentrancyGuard` marks a cell as "currently being applied from
  the network" so a Postfix that would normally broadcast a local change can tell it apart from us
  replaying an incoming one - the same `IsApplying`-style idiom ONI Together's own
  `UserNameableChangePacket`/`ResearchPatch` use.
- **InstantBuild fix**: rather than reaching into `BuildPacket`'s fields (all private, no accessor),
  this mirrors the exact trick ONI Together's *own* `UtilityBuildPacket` receive path already uses:
  temporarily flip the public vanilla `DebugHandler.InstantBuildMode` flag around `BuildTool.TryBuild`
  so ONI Together's own `BuildToolPatch.Postfix` computes `InstantBuild = true` when it reads that
  flag. A `Prefix`+`Finalizer` pair (not `Prefix`+`Postfix`) because Finalizers are guaranteed by
  Harmony to run after every prefix/postfix from every patch owner, regardless of patch load order -
  a plain Postfix pair could restore the flag before ONI Together's own Postfix ever reads it.
- **ResearchQueue shift-click**: ONI Together's own `ResearchRequestPacket` has one field (`TechId`)
  and the host handler hardcodes `clearQueue: true` - there is no way to express "queue this without
  making it active" through it. For a shift click this mod sends its own
  `ResearchQueueActionRequestPacket` and best-effort suppresses ONI Together's own
  `ResearchRequestPacket` send for that click (patching `ONI_Together.Networking.PacketSender.SendToHost`
  directly - not part of the public API, exactly the kind of reflection-into-a-private-internal the
  task brief itself calls for on the InstantBuild fix). **This is the single most fragile piece of
  this mod** - if that internal method's name/signature has moved on a newer ONI Together build,
  this silently no-ops (wrapped in try/catch, logged) and shift-clicks fall back to racing both
  packets. Verify this first, before anything else, against whatever ONI Together build is actually
  installed.

## What still needs live testing (host + client, two machines/peers)

In the order the task suggested prioritizing (highest confirmed risk first):

1. **ResearchQueue**: does a shift-click actually queue correctly on both peers without the
   `SendToHost` suppression silently failing? Does a plain click still work identically to today
   (i.e. did neutralizing ResearchQueue's local mutation break anything else it was doing)? What
   happens on load - does `SavedResearchQueue.OnDeserialized` (which unconditionally calls
   `SetActiveResearch` on every peer, host or client, right after a save loads) race with this?
   This mod does *not* patch `SavedResearchQueue` - flagged but not fixed, see below.
2. **Scaffolds**: does `InstantBuildFix` actually make a Scaffold appear instantly-built on every
   peer? Does deconstruction (button, self-destruct timer, and copy-settings toggle - all three
   paths) actually disappear on every peer exactly once, with no double-delete or client/host
   ordering issue?
3. **SignsTagsAndRibbons**: does a variant change propagate both ways without flicker/loop?
4. **MoveThisHere**: does the capacity slider really sync for free as expected? Does the newly-found
   self-destruct/auto-deconstruct fix work the same way Scaffolds' does?

## Known gaps / simplifications, not fixed here

- `ResearchQueueActionRequestPacket`'s un-queue path removes only the single clicked tech, not the
  cascading *unlocked dependents* ResearchQueue's own `RemoveResearch` also removes.
- `SavedResearchQueue.OnDeserialized`'s redundant-local-reapply-on-load risk (flagged in the task
  brief) is not patched - needs a live save/load test with ONI Together's own save-sync behavior to
  know whether it's actually a problem before writing a fix for it.
- Multiplayer settings drift: `Scaffolds_Patch.Settings.Duration` is a local PLib mod option: if
  host and client have configured different self-destruct durations, `ScaffoldSelfDestructTogglePacket`
  syncs the *sender's* actual remaining time (read via reflection off `deconstructMoment`, not
  recomputed from the receiver's own `Settings.Duration`), so this should be correct regardless of
  drift - but not verified live.
