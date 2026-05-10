using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace PathPeek;

/// <summary>
/// Sits on top of <see cref="NMapScreen"/>. Two responsibilities:
/// 1. Compute per-node badge counts (cached — only re-runs when the live
///    map or the player's position changes).
/// 2. On hover, ask the analyzer for every edge participating in a max-count
///    path through the hovered room and draw those as bright lines.
///
/// Per-frame work is bounded: one tree walk to refresh the icon-position
/// cache, plus O(badges) lookups during draw/hover. No DP allocations on
/// frames where nothing has changed.
/// </summary>
public sealed partial class MapOverlay : Control
{
    // Cached analysis state.
    private MapGraph? _graph;
    private MapAnalyzer.Result? _analysis;
    private object? _lastMapRef;
    private object? _lastCurrentRef;

    // Per-frame icon position cache. Encoded id -> screen-space center.
    private readonly Dictionary<int, Vector2> _iconPositions = new();
    private int _lastChildCount = -1;

    // Hover state.
    private int? _hoveredId;
    private HashSet<(int, int)> _highlightEdges = new();

    private static readonly Color BadgeBg     = new("000000cc");
    private static readonly Color BadgeBgMax  = new("2bb84acc"); // green when badge ties the per-kind max
    private static readonly Color BadgeText   = new("ffffffff");
    private static readonly Color HighlightFg = new("ffd166ff");
    private const float BadgeRadius = 11f;
    private const float HighlightWidth = 4f;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        TopLevel = true;
        ZIndex = 4096;
        GD.Print(ModEntry.LogPrefix + "MapOverlay ready, parent=" + GetParent()?.Name);
    }

    public override void _Process(double delta)
    {
        var screen = GetParent() as NMapScreen;
        if (screen == null) return;

        // Cheap reference check — only rebuild graph + analysis when the
        // live ActMap or CurrentMapPoint references change.
        var (mapRef, curRef) = Bridge.TryReadStateRefs();
        if (mapRef == null)
        {
            ClearAll();
            return;
        }
        if (!ReferenceEquals(mapRef, _lastMapRef) || !ReferenceEquals(curRef, _lastCurrentRef))
        {
            try
            {
                _graph = Bridge.TryReadGraph();
                _analysis = _graph != null ? MapAnalyzer.Analyze(_graph) : null;
                _lastMapRef = mapRef;
                _lastCurrentRef = curRef;
                if (_graph != null)
                    GD.Print(ModEntry.LogPrefix + "graph re-analyzed: " + _graph.Nodes.Count
                             + " nodes, choices=" + _graph.ChoiceIds.Count);
            }
            catch (System.Exception e)
            {
                GD.PrintErr(ModEntry.LogPrefix + "Analyze threw: " + e);
                _graph = null; _analysis = null;
            }
        }

        if (_graph == null || _analysis == null)
        {
            QueueRedraw();
            return;
        }

        RefreshIconPositions(screen);
        UpdateHover();
        QueueRedraw();
    }

    private void ClearAll()
    {
        _graph = null; _analysis = null;
        _lastMapRef = null; _lastCurrentRef = null;
        _iconPositions.Clear();
        _hoveredId = null;
        _highlightEdges.Clear();
        QueueRedraw();
    }

    /// <summary>One scene-tree walk per frame; populates the icon dict.</summary>
    private void RefreshIconPositions(NMapScreen screen)
    {
        _iconPositions.Clear();
        WalkAndCollect(screen);
    }

    private void WalkAndCollect(Node node)
    {
        if (node is NMapPoint mp)
        {
            var pt = mp.Point;
            if (pt != null)
            {
                int id = (pt.coord.row << 16) | (pt.coord.col & 0xFFFF);
                _iconPositions[id] = mp.GlobalPosition + mp.Size / 2;
            }
        }
        foreach (var child in node.GetChildren())
            WalkAndCollect(child);
    }

    private void UpdateHover()
    {
        if (_graph == null || _analysis == null) { _hoveredId = null; return; }

        var mouse = GetGlobalMousePosition();
        int? hit = null;
        float bestDist = BadgeRadius * 2f;
        foreach (var (id, _) in _iconPositions)
        {
            if (!_analysis.Badges.ContainsKey(id)) continue;
            float d = _iconPositions[id].DistanceTo(mouse);
            if (d < bestDist) { bestDist = d; hit = id; }
        }

        if (hit != _hoveredId)
        {
            _hoveredId = hit;
            _highlightEdges = hit.HasValue
                ? MapAnalyzer.EdgesForMaxPathsThrough(_graph, _analysis, hit.Value)
                : new();
        }
    }

    public override void _Draw()
    {
        if (_graph == null || _analysis == null) return;

        // Highlighted edges first so badges sit on top.
        foreach (var (a, b) in _highlightEdges)
        {
            if (!_iconPositions.TryGetValue(a, out var pa)) continue;
            if (!_iconPositions.TryGetValue(b, out var pb)) continue;
            DrawLine(pa, pb, HighlightFg, HighlightWidth, antialiased: true);
        }

        // Per-kind max badge value (cheap to recompute — at most ~65 entries).
        var perKindMax = new Dictionary<RoomKind, int>();
        foreach (var (_, b) in _analysis.Badges)
        {
            perKindMax.TryGetValue(b.Kind, out var cur);
            if (b.MaxCount > cur) perKindMax[b.Kind] = b.MaxCount;
        }

        var font = ThemeDB.FallbackFont;
        const int fontSize = 14;
        foreach (var (id, badge) in _analysis.Badges)
        {
            if (badge.MaxCount <= 1) continue;
            if (!_iconPositions.TryGetValue(id, out var center)) continue;
            var anchor = center + new Vector2(14, -14);
            var bg = (perKindMax.TryGetValue(badge.Kind, out var m) && badge.MaxCount == m)
                ? BadgeBgMax : BadgeBg;
            DrawCircle(anchor, BadgeRadius, bg);
            var text = badge.MaxCount.ToString();
            var textSize = font.GetStringSize(text, HorizontalAlignment.Center, -1, fontSize);
            DrawString(font, anchor - textSize / 2 + new Vector2(0, fontSize * 0.4f),
                text, HorizontalAlignment.Center, -1, fontSize, BadgeText);
        }
    }
}
