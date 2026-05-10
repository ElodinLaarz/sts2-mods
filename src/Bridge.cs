using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace PathPeek;

/// <summary>
/// Adapter between live STS2 game state and our generic <see cref="MapGraph"/>.
/// All knowledge of <c>MegaCrit.Sts2.*</c> types lives here so the algorithm
/// and overlay can be reviewed/tested independently.
///
/// Type/member names verified against the shipping <c>sts2.dll</c> via
/// PE metadata reflection — see /tmp/inspect for the dumper.
/// </summary>
public static class Bridge
{
    /// <summary>Returns the active <see cref="NMapScreen"/> singleton, or null.</summary>
    public static NMapScreen? FindActiveMapScreen() => NMapScreen.Instance;

    /// <summary>
    /// Returns the live (ActMap, CurrentMapPoint) pair without allocating.
    /// Used by callers that want to cheaply detect topology changes.
    /// </summary>
    public static (object? Map, object? Current) TryReadStateRefs()
    {
        var rm = RunManager.Instance;
        if (rm == null || !rm.IsInProgress) return (null, null);
        var run = rm.DebugOnlyGetState();
        if (run == null) return (null, null);
        return (run.Map, run.CurrentMapPoint);
    }

    /// <summary>
    /// Reads the live run's map graph and player position. Returns null if
    /// no run is in progress or the map has not been generated yet.
    /// </summary>
    public static MapGraph? TryReadGraph()
    {
        var rm = RunManager.Instance;
        if (rm == null || !rm.IsInProgress) return null;
        // RunManager.State has a private getter; DebugOnlyGetState() is the
        // only public accessor exposed by the shipped sts2.dll.
        var run = rm.DebugOnlyGetState();
        if (run == null) return null;
        var map = run.Map;
        if (map == null) return null;

        var nodes = new Dictionary<int, MapNode>();
        foreach (var p in EnumerateAllPoints(map))
        {
            int id = EncodeId(p.coord.row, p.coord.col);
            var node = new MapNode
            {
                Id = id,
                Kind = MapKind(p.PointType),
                Row = p.coord.row,
            };
            foreach (var child in p.Children)
                node.Next.Add(EncodeId(child.coord.row, child.coord.col));
            nodes[id] = node;
        }

        // Player position + legal next steps.
        int? currentId = null;
        var current = run.CurrentMapPoint;
        if (current != null)
            currentId = EncodeId(current.coord.row, current.coord.col);

        IReadOnlyList<int> choices;
        if (currentId.HasValue && nodes.TryGetValue(currentId.Value, out var cur))
        {
            choices = cur.Next;
        }
        else
        {
            // Pre-start: legal first picks = StartingMapPoint.Children.
            // The StartingMapPoint itself sits at coord.row = -1 in STS2, so
            // a Row==0 filter on the live grid wouldn't find anything.
            var start = map.StartingMapPoint;
            var first = new List<int>();
            if (start != null)
                foreach (var c in start.Children)
                    first.Add(EncodeId(c.coord.row, c.coord.col));
            choices = first;
        }

        return new MapGraph
        {
            Nodes = nodes,
            CurrentId = currentId,
            ChoiceIds = choices,
        };
    }

    /// <summary>
    /// Returns the screen-space center of the icon for the room with the given
    /// (encoded) id, or null if the matching <see cref="NMapPoint"/> is not
    /// currently in the tree.
    /// </summary>
    public static Vector2? TryGetRoomIconPosition(NMapScreen mapScreen, int encodedId)
    {
        var (row, col) = DecodeId(encodedId);
        foreach (var visual in FindAllNMapPoints(mapScreen))
        {
            var mp = visual.Point;
            if (mp == null) continue;
            if (mp.coord.row != row || mp.coord.col != col) continue;
            // Both NMapScreen and NMapPoint inherit from Godot.Control.
            return visual.GlobalPosition + visual.Size / 2;
        }
        return null;
    }

    private static IEnumerable<MapPoint> EnumerateAllPoints(ActMap map)
    {
        // ActMap.GetAllMapPoints() returns every point in the act's grid.
        // The return type is non-generic in some builds — iterate dynamically
        // to stay tolerant of List<MapPoint> vs. IEnumerable<MapPoint>.
        var raw = map.GetAllMapPoints();
        if (raw == null) yield break;
        foreach (var obj in (System.Collections.IEnumerable)raw)
            if (obj is MapPoint p) yield return p;
    }

    private static IEnumerable<NMapPoint> FindAllNMapPoints(Node root)
    {
        if (root is NMapPoint mp) yield return mp;
        foreach (var child in root.GetChildren())
            foreach (var sub in FindAllNMapPoints(child))
                yield return sub;
    }

    private static RoomKind MapKind(MapPointType t) => t switch
    {
        MapPointType.Monster  => RoomKind.Monster,
        MapPointType.Elite    => RoomKind.Elite,
        MapPointType.RestSite => RoomKind.RestSite,
        MapPointType.Treasure => RoomKind.Treasure,
        MapPointType.Shop     => RoomKind.Shop,
        MapPointType.Boss     => RoomKind.Boss,
        MapPointType.Ancient  => RoomKind.Ancient,
        MapPointType.Unknown  => RoomKind.Unknown,
        _                     => RoomKind.Unknown,
    };

    // The map is small (< ~120 rooms) so packing row+col into a single int is
    // cheaper than carrying a struct key and rewriting the algorithm signatures.
    private static int EncodeId(int row, int col) => (row << 16) | (col & 0xFFFF);
    private static (int row, int col) DecodeId(int id) => (id >> 16, id & 0xFFFF);
}
