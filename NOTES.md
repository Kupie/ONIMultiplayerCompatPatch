# Verification notes

Written against fresh `--depth=1` clones of all five repos and the real `ONI_Together_API` NuGet
package, pulled 2026-08-08. ONI Together is pre-alpha and moves fast - re-read the "needs live
testing" section before trusting this against a newer ONI Together build, and re-verify anything
that throws a `MissingMethodException`/`AmbiguousMatchException` at runtime.

## Update 2026-08-29: fixed a live bug - Scaffolds' "Remove" button didn't sync (deconstruct order did)

Reported from actual play: using the custom "Remove" user-menu button on a Scaffold didn't sync to
other peers, but deconstructing it via ONI Together's own vanilla deconstruct-order sync did.

Root cause, confirmed from source: `ScaffoldConfig.cs` sets
`public static ObjectLayer ObjectLayer = ObjectLayer.FillPlacer;` (with the mod author's own comment
"This layer doesn't seem to be used anywhere else... hopefully") and assigns it to
`scaffoldDef.ObjectLayer` - Scaffold is deliberately placed on `ObjectLayer.FillPlacer`, not
`ObjectLayer.Building`. `Infrastructure.CellAddressing.FindBuildingAt` hardcoded a lookup against
`Grid.Objects[cell, (int)ObjectLayer.Building]`, so on the receiving peer it silently found nothing
for a Scaffold - the "Remove" button's `CellMethodInvokePacket` (via `CellMethodRelay`, patched onto
`DeconstructableScaffold.OnDeconstruct`) was being sent correctly, it just could never find its
target to replay the call against. The vanilla deconstruct order worked because ONI Together
addresses buildings through its own `NetworkIdentity`/NetId system there, not through this lookup.

This affected every cell-addressed packet in the mod, not just Scaffolds' deconstruct sync -
`ScaffoldSelfDestructTogglePacket` and `SignVariantChangePacket` used the same helper. Fixed by
replacing the hardcoded-layer lookup with `CellAddressing.FindBuildingWithComponentAt(cell, type)`,
which scans every `ObjectLayer` at the cell for a GameObject carrying the expected component type
instead of assuming any particular layer - this is layer-agnostic by construction, so it can't
recur for some other custom mod building that also picks an unusual `ObjectLayer`. All three call
sites (`CellMethodInvokePacket`, `ScaffoldSelfDestructTogglePacket`, `SignVariantChangePacket`) were
updated. SignsTagsAndRibbons' sign buildings don't appear to override `ObjectLayer` (so were
probably fine on the old hardcoded lookup already, defaulting to `ObjectLayer.Building`), and
MoveThisHere's `HaulingPoint` likewise doesn't override it - but there's no reason to leave either
on the fragile assumption now that the general fix exists.

**Not yet re-verified live** - the theory fits the reported symptom exactly (send succeeds, receive
silently no-ops) and the fix compiles clean, but this specific scenario (Remove button, host+client)
should be the first thing re-tested.

## Update 2026-08-12: verified against ONI Together's `testing/network-backend-upgrades` branch

Checked this mod against `Lyraedan/Oxygen_Not_Included_Together@testing/network-backend-upgrades`
(HEAD `45664b0`, 212 commits ahead of `main`'s common ancestor). **Conclusion: no code changes were
needed.** This branch introduces a large new internal networking subsystem ("OxySync" - a
Mirror/FishNet-style `NetworkBehaviour`/`[SyncVar]`/`[Command]`/`[ClientRpc]`/`[TargetRpc]`
component framework, `Shared/OxySync/` + `ONI_Together/Networking/OxySync/` +
`ONI_Together/Patches/OxySync/`), but three things keep this mod unaffected:

1. **OxySync is additive, not a replacement.** It's already wired up and driving real features -
   plant lifecycle, duplicant/creature vitals and status items, entity position, batteries/
   generators, storage/toilets/printing-pod/nuclear-reactor, several building-internal state
   machines (algae habitat, electrolyzer, rottable food, clinic, bottle emptier, rust deoxidizer,
   grave), game clock/speed, and chat - but building placement, research sync, vanilla side-screen/
   slider sync, and building renaming are all **still on the old plain-`IPacket` system** this mod
   already integrates with. None of the four target mods' components (HaulingPoint, Scaffold,
   SelectableSign, ResearchQueue) appear in OxySync's migrated-feature list.
2. **OxySync's own traffic rides inside the same old `IPacket`/`PacketSender` transport anyway**
   (`CommandPacket`/`ClientRpcPacket`/`SyncVarPacket`/`SyncVarBatchPacket` are themselves `IPacket`
   implementations sent via the same `PacketSender.SendToGroup`/`SendToHost`/etc. this mod already
   knows about) - so even where this mod does touch the transport layer directly
   (`ResearchQueueClientRedirectPatches.SendToHostGuard`, which Harmony-patches
   `ONI_Together.Networking.PacketSender.SendToHost` by name), that method's namespace and exact
   signature (`SendToHost(IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate)`)
   are confirmed unchanged on this branch, even though the file itself physically moved to
   `ONI_Together/Networking/Packets/Architecture/PacketSender.cs`. `AccessTools.TypeByName` resolves
   by namespace+name, not by file path, so this was never going to matter - worth having confirmed
   directly rather than assumed, though, given how much this specific patch was flagged as fragile.
3. **Every specific internal patch/packet this mod reflects into or mirrors the idiom of was
   individually checked and confirmed unchanged in every way that matters**:
   - `BuildToolPatch`/`BuildPacket` (`InstantBuildFix`'s target): `BuildPacket.InstantBuild` is still
     a private bool with no accessor, still computed in `BuildToolPatch.Postfix` from
     `DebugHandler.InstantBuildMode || (Game.Instance.SandboxModeActive && SandboxToolParameterMenu.instance.settings.InstantBuild)`.
     The packet's constructor and internal dispatch were refactored (priority is now read from
     `PlanScreen.Instance` inside the constructor instead of passed in, and replacement-layer
     handling was added), but none of that is anything this mod touches.
   - `ResearchPatch`/`ResearchEntryPatch`/`ResearchRequestPacket`/`ResearchStatePacket`
     (`ResearchQueueCompat`'s whole design rests on these): all confirmed unchanged, including the
     one fact this mod's design specifically depends on - `ResearchRequestPacket` still has only a
     single `TechId` field, no shift-click/queue semantics. (A new, unrelated `ResearchProgressPacket`
     was added for progress-bar percentage sync - nothing to do with this mod.)
   - `UserNameableChangePacket`/`UserNameablePatch`, `BuildingConfigPacket`/`SliderPatches`/
     `SideScreenSyncHelper`, `NetworkIdentity`/`NetworkIdentityRegistry`: all still exist at the same
     paths implementing the same idioms this mod's own design mirrors or relies on existing
     generically (`BuildingConfigPacket` picked up a new `Sender` self-echo field and had its
     dispatch refactored into a `BuildingConfigHandlerRegistry`, but this mod never reflects into
     that class directly - it only relies on the generic slider sync working, which is unaffected).

   There's one blanket-scope thing worth flagging even though it doesn't currently intersect with
   this mod: a new Harmony prefix, `StateMachineGoToFreeze_Patch`
   (`[HarmonyPatch(typeof(StateMachine.Instance), nameof(StateMachine.Instance.GoTo), typeof(string))]`),
   intercepts **every** Klei `StateMachine.Instance.GoTo` call in the entire game and blocks it if
   OxySync has frozen that instance (done for OxySync-migrated buildings only, today). None of the
   four target mods' components use Klei `StateMachine<T>` internally as far as this mod's research
   found, so this shouldn't matter - but if a future target mod (or a newer version of one of the
   current four) turns out to drive its behavior through a `StateMachine.Instance`, this is a global
   patch worth knowing exists.

The published `ONI_Together_API` NuGet package this mod's `PackageReference` pins
(`0.7.2-alpha.0.34`) is unaffected either way: its public surface differs from `main` by exactly one
addition (`SessionInfoAPI.TryGetPlayerCursorPos`/`TryGetPlayerColor`, neither used here), and that
package is itself already stale relative to `main` (nobody has cut a new tag/publish since before
`main`'s current `v0.7.3`) - a pre-existing condition unrelated to this branch. No csproj change
needed.

ONI shipped its Aquatic Planet Pack update June 11 2026 (community wiki records it as build
`U59-736649`, `mod_info.yaml`'s `minimumSupportedBuild` bumped to `736649` accordingly - I could not
independently confirm that exact number since the wiki/Steam news pages were blocked by this
environment's egress proxy when I tried to fetch them directly, so treat it as reasonably-sourced
rather than certain, and double check before shipping).

Two follow-up questions came up, both now resolved:

1. **Should this project's own TargetFramework change to net48?** No. A web search turned up that
   PLib (the foundational library ResearchQueue and most other community ONI mods build on) recently
   migrated its own "legacy" target from `net471` to `net48`, while explicitly keeping
   `netstandard2.1` as its primary target - i.e. `net48` is now the floor for classic-Framework ONI
   mods (replacing `net471`), but `netstandard2.1` remains valid and is what this project needs
   anyway to consume `ONI_Together_API`'s `PackageReference` without the restore-time incompatibility
   described above. This project's `TargetFramework` was already `netstandard2.1` and did not need to
   change.
2. **Does anything in this mod's code actually need to change for the Aqua build?** Yes, two things -
   found by a second, much stronger verification pass: I was given read access to
   `github.com/Kupie/ONI_Decomp`, a full decompilation of the actual current `Assembly-CSharp`/
   `Assembly-CSharp-firstpass`. Unlike the first pass (compiling against a "publicized" reference DLL
   bundled in peterhaneve/ONIMods' own repo, which flips private members to public *for compiling
   against only* - the standard ONI-modding trick, also used by ONI Together itself via its own
   `PublicisedAssembly` folder), this is the real, current, unmodified source, so it's the strongest
   signal available in this environment for "what's actually private now." Grepped/read every vanilla
   API surface this project touches against it. Two real accessibility changes found and fixed:
   - `BuildTool.TryBuild(int)` and `BuildTool.def` are both **private** in the actual game. My
     `InstantBuildFix` originally used `nameof(BuildTool.TryBuild)` (would not compile against the
     real, non-publicized member) and direct `__instance.def` access (same problem). Fixed: string-
     targeted `AccessTools.Method(typeof(BuildTool), "TryBuild", new[] { typeof(int) })`, and Harmony's
     `___def`-prefixed field injection instead of direct field access - the same accessibility-
     agnostic idiom already used for `Research.queuedTech` elsewhere in this project, which works
     against a private field regardless of what a "publicized" compile-time-only DLL claims.
   - `ResearchEntry.targetTech` is also **private**. Same fix: `Tech ___targetTech` field injection
     in `ResearchQueueClientRedirectPatches.OnResearchClicked_Prefix` instead of `__instance.targetTech`.

   Everything else this project touches - `BuildingDef.PrefabID` (`string`), `Research.CancelResearch`
   (`Tech, bool clickedEntry = true`), `Research.AddTechToQueue` (`private void(Tech)`, still recurses
   into `tech.requiredTech`), `Research.SetActiveResearch` (`Tech, bool clearQueue = false`) and
   `queuedTech` (`private List<TechInstance>`), `DebugHandler.InstantBuildMode` (`public static bool`),
   `Grid.PosToCell`/`Grid.Objects`/`Grid.IsValidCell`, `ObjectLayer.Building`,
   `KMod.UserMod2.OnAllModsLoaded(Harmony, IReadOnlyList<Mod>)`, `GameClock.GetTime()`, and
   `Db.Get().Techs.Get/TryGet(string)` - matched exactly what this project already assumed, no other
   changes needed.

   Reading `Research.SetActiveResearch`'s and `Research.AddTechToQueue`'s actual current bodies also
   fully resolved a question the first pass had left as "inferred, not proven": what "active research"
   actually is. It's not a separately-tracked concept - `SetActiveResearch(tech, clearQueue)` always
   ends by sorting `queuedTech` by tier and setting `activeResearch = queuedTech[0]` (or clearing
   everything if `tech == null`). This confirms `ResearchQueueActionRequestPacket`'s host-side handler
   (which calls `AddTechToQueue` then `SetActiveResearch(tech, false)` to add-and-requeue, or removes
   an entry then calls `SetActiveResearch(lastRemaining, false)` to re-sort) does the right thing by
   construction, including the empty-queue-after-removal case (`SetActiveResearch(null, false)` just
   clears the already-empty queue - no `AddTechToQueue(null)` call happens, since the `tech == null`
   branch never reaches that call). No behavior change needed there, just confirmation.

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
