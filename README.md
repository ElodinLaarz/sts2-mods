# PathPeek — a Slay the Spire 2 mod

Adds a small badge next to each room icon on the map showing the **maximum
number of that room type** you can reach on any single path that passes
through it (starting from your current position). Hover a room to highlight
the path(s) that achieve that maximum. Badges that tie the per-kind global
maximum are colored green so the optimal-elite / optimal-shop nodes pop
visually.

## Layout

```
PathPeek.csproj        Godot 4.5.1 .NET SDK project, refs lib/sts2.dll
mod_manifest.json      shipped to the game
project.godot          for editing in the Godot editor (optional)
icon.svg, main.tscn    placeholders the Godot editor expects
lib/                   put sts2.dll, 0Harmony.dll, GodotSharp.dll here (git-ignored)
src/
  ModEntry.cs           [ModInitializer] entry — installs MapScreenWatcher
  MapScreenWatcher.cs   detects when NMapScreen.Instance appears, attaches overlay
  MapOverlay.cs         draws number badges + hover highlight (cached per frame)
  MapAnalyzer.cs        the algorithm (pure C#, game-agnostic)
  MapGraph.cs           generic DAG types the analyzer operates on
  Bridge.cs             adapter to MegaCrit.Sts2.* types
```

## Algorithm

The map is a DAG with rooms as nodes and forward edges as transitions.
For a room `X` of type `T = X.Kind`:

```
badge(X) = max over all paths P (frontier → leaf) passing through X of |{ n in P : n.kind = T }|
        = prefix(X, T) + suffix(X, T) - 1
```

`prefix(X, T)` = max count of `T` on any path from a frontier source to `X`
inclusive (forward DP in topological order).
`suffix(X, T)` = max count of `T` on any path from `X` to a leaf inclusive
(reverse DP). The `-1` removes the double-count of `X`.

Hover highlight: walk forward from `X` along edges where
`suffix(child, T) + (X.kind==T ? 1 : 0) == suffix(X, T)` and backward along
edges where `prefix(parent, T) + (X.kind==T ? 1 : 0) == prefix(X, T)`. Every
such edge participates in at least one maximum path.

The "frontier" is the player's legal next-step choices mid-run
(`run.CurrentMapPoint.Children`) or `map.StartingMapPoint.Children` pre-start.

## Performance

The naive implementation re-ran the DP and walked the scene tree per badge
per frame, which tanked FPS on the map screen. The current overlay:

- Caches the analyzed graph and only re-runs `MapAnalyzer.Analyze` when
  `RunState.Map` or `RunState.CurrentMapPoint` change reference.
- Walks the scene tree exactly once per frame to populate an
  `encodedId -> Vector2` icon-position dict, then does O(1) lookups during
  hover detection and draw.
- No per-frame allocations once the graph is cached.

## STS2 internals (verified against sts2.dll)

Resolved by reflecting over the shipped assembly's PE metadata (see
`/tmp/inspect/Program.cs` from the dev session for the dumper):

| What                | Where it lives                                              |
| ------------------- | ----------------------------------------------------------- |
| Entry point         | `[ModInitializer("ModLoaded")]` from `MegaCrit.Sts2.Core.Modding` |
| Run access          | `RunManager.Instance.DebugOnlyGetState()` (the `State` getter is private) |
| Map access          | `RunState.Map` returns `ActMap`; `ActMap.GetAllMapPoints()` |
| Player position     | `RunState.CurrentMapPoint` (null pre-first-pick)            |
| Pre-start frontier  | `ActMap.StartingMapPoint.Children`                          |
| Node geometry       | `MapPoint.coord.row/col` (fields), `PointType`, `Children`  |
| Node-type enum      | `MapPointType { Unassigned, Unknown, Shop, Treasure, RestSite, Monster, Elite, Boss, Ancient }` |
| Map-screen UI       | `NMapScreen.Instance` (singleton)                           |
| Per-room UI node    | `NMapPoint.Point` returns the underlying `MapPoint`         |
| Both UI nodes       | inherit from `Godot.Control` (`GlobalPosition + Size/2` works for screen-space) |

## Build

Prerequisites:

- [Godot 4.5.1 .NET edition](https://godotengine.org/download/) (matches the
  game's runtime — the SDK reference resolves to `Godot.NET.Sdk/4.5.1`).
- .NET SDK 9.0+.
- A copy of `sts2.dll` plus `0Harmony.dll` and `GodotSharp.dll` from your
  Slay the Spire 2 install — see [`lib/README.md`](lib/README.md).

```powershell
dotnet build -c Release
```

Output: `.godot/mono/temp/bin/Release/PathPeek.dll`.

## Install

Slay the Spire 2 looks for mods in
`C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\`.

Copy the following into `mods/PathPeek/`:

- `.godot/mono/temp/bin/Release/PathPeek.dll`
- `mod_manifest.json`

Launch the game; the in-game mod menu should list **PathPeek** and the
console log should print `[PathPeek] Loading...` / `Loaded.`. Open the map
inside any run — badges should appear next to room icons whose per-kind
max-along-path is greater than 1.

(The mod is code-only — no `.pck` needed. `mod_manifest.json` declares
`has_pck: false, has_dll: true`.)

## Reference mods

If a future game build breaks something in `Bridge.cs`, cross-check against:

- [jiegec/STS2RouteSuggest](https://github.com/jiegec/STS2RouteSuggest) —
  similar use of `RunState.Map` + `MapPoint.Children` for path scoring.
- [Gennadiyev/STS2MCP](https://github.com/Gennadiyev/STS2MCP) — uses
  `NMapScreen.Instance` + walks `NMapPoint`s for UI-layer state.
- [cpimhoff/Sts2-ModSmith](https://github.com/cpimhoff/Sts2-ModSmith) —
  framework + [decompile guide](https://cpimhoff.github.io/Sts2-ModSmith/docs/setup/decompile.html).
- [jiegec/STS2FirstMod](https://github.com/jiegec/STS2FirstMod) — minimal
  `[ModInitializer]` skeleton.

## License

TBD.
