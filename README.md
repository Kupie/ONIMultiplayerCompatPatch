# MultiplayerCompatPatch

A standalone Oxygen Not Included Harmony mod that patches four independent single-player mods from
the outside so they behave correctly when [Oxygen Not Included Together](https://github.com/Lyraedan/Oxygen_Not_Included_Together)
(multiplayer) is also active:

- [MoveThisHere](https://github.com/DoctorFeelGoodMD/OxygenNotIncluded-Mods) (`HaulingPoint`)
- [SignsTagsAndRibbons](https://github.com/pether-pg/ONI_Mods_byPether)
- [Scaffolds](https://github.com/nathantalewis/oni-scaffolds) (`Scaffold`)
- [ResearchQueue](https://github.com/peterhaneve/ONIMods)

This mod does not modify, fork, or reference the source of any of those four mods, or of ONI
Together itself. All patching is done via Harmony, targeting each mod's types/methods by string
(`AccessTools.TypeByName`) so a missing target mod is a graceful no-op rather than a crash. Each
target mod's assembly must actually be loaded (checked via `Infrastructure.ModPresence`) for that
target's compat patches to apply, so any subset of the four mods can be installed. The one hard
dependency is a NuGet `PackageReference` on ONI Together's own `ONI_Together_API` package, which is
designed to no-op safely when ONI Together isn't installed (see its `README_NUGET.md`).

See `NOTES.md` for exactly what's been verified against current source for each mod vs. what's
inferred and still needs empirical host+client testing.

## Building

Requires the .NET SDK and an Oxygen Not Included install. Point the `ONIPath` MSBuild property (or
an `ONIPath` environment variable) at your ONI install root - the folder containing
`OxygenNotIncluded_Data` - then:

```
dotnet build /p:ONIPath="C:\...\Oxygen Not Included"
```

The project targets `netstandard2.1`, not `net471`/`net48` (the classic-Framework target most
Harmony-only ONI mods use, post-Aqua-update) - `netstandard2.1` is what bridges ONI Together (which
also targets `netstandard2.1`) with the base game and the four target mods (`net48`). See NOTES.md
for why that matters and isn't a mistake.

This mod's own dev environment had no `dotnet` SDK reachable (Microsoft's download CDN was blocked
by an outbound proxy) and no ONI install, so it couldn't be built or run with `dotnet build`/in-game.
It has, however, been compiled clean with the Mono C# compiler against the real `ONI_Together_API.dll`
pulled from nuget.org and real Klei game assemblies, and every vanilla game API surface it touches
has been cross-checked against a full decompilation of the actual current (post-Aqua) game assemblies
- see NOTES.md for exactly what that did and didn't catch, including two real accessibility changes
it caught. Build with the real SDK and playtest host + client for every scenario in NOTES.md before
trusting this in a real game.
