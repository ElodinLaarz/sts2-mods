using System.Collections.Generic;
using System.Linq;

namespace PathPeek;

/// <summary>
/// For each map node X with type T = X.Kind, computes the maximum number of
/// T-typed nodes appearing on any single path that (a) starts at the player's
/// current/choice frontier and (b) passes through X on its way to a boss/leaf.
///
/// Hovering X should highlight every edge that participates in at least one
/// such maximum path.
/// </summary>
public static class MapAnalyzer
{
    public sealed record NodeBadge(int NodeId, RoomKind Kind, int MaxCount);

    public sealed class Result
    {
        public required IReadOnlyDictionary<int, NodeBadge> Badges { get; init; }
        // For hover: prefix[(nodeId, kind)] = max count of `kind` on any path
        // from a frontier source up to and including nodeId. Same idea suffix.
        public required IReadOnlyDictionary<(int NodeId, RoomKind Kind), int> Prefix { get; init; }
        public required IReadOnlyDictionary<(int NodeId, RoomKind Kind), int> Suffix { get; init; }
        public required IReadOnlyList<int> Sources { get; init; }
    }

    public static Result Analyze(MapGraph g)
    {
        // The "frontier" is where the player can legally start counting from.
        // Mid-run: their choice nodes (next legal steps). Pre-start: every
        // first-floor node reachable from the start.
        var sources = g.ChoiceIds.Count > 0
            ? g.ChoiceIds.ToList()
            : g.Nodes.Values.Where(n => n.Row == 0).Select(n => n.Id).ToList();

        var topo = TopologicalOrder(g, sources);
        var topoSet = new HashSet<int>(topo);

        var allKinds = g.Nodes.Values.Select(n => n.Kind).Distinct().ToArray();

        // Forward DP: prefix[(node, kind)] = max # of `kind` nodes on any path
        // from some source up to and including `node`. -1 sentinel means
        // unreachable from a source.
        var prefix = new Dictionary<(int, RoomKind), int>();
        foreach (var nodeId in topo)
        {
            var node = g.Get(nodeId);
            foreach (var kind in allKinds)
            {
                int self = node.Kind == kind ? 1 : 0;
                int best = sources.Contains(nodeId) ? self : -1;
                // Inbound edges: any predecessor whose Next includes nodeId.
                // Build reverse adjacency lazily below — simpler to walk all
                // nodes once.
                prefix[(nodeId, kind)] = best;
            }
        }
        // Build reverse adjacency once.
        var reverse = new Dictionary<int, List<int>>();
        foreach (var n in g.Nodes.Values)
        {
            foreach (var nx in n.Next)
            {
                if (!reverse.TryGetValue(nx, out var list)) { list = new(); reverse[nx] = list; }
                list.Add(n.Id);
            }
        }
        // Forward DP using topological order.
        foreach (var nodeId in topo)
        {
            var node = g.Get(nodeId);
            if (!reverse.TryGetValue(nodeId, out var preds)) continue;
            foreach (var kind in allKinds)
            {
                int self = node.Kind == kind ? 1 : 0;
                int best = prefix[(nodeId, kind)];
                foreach (var p in preds)
                {
                    if (!topoSet.Contains(p)) continue;
                    int via = prefix[(p, kind)];
                    if (via < 0) continue;
                    int candidate = via + self;
                    if (candidate > best) best = candidate;
                }
                prefix[(nodeId, kind)] = best;
            }
        }

        // Backward DP: suffix[(node, kind)] = max # of `kind` nodes on any path
        // from `node` (inclusive) to a leaf (no outgoing edges within graph).
        var suffix = new Dictionary<(int, RoomKind), int>();
        foreach (var nodeId in topo.AsEnumerable().Reverse())
        {
            var node = g.Get(nodeId);
            foreach (var kind in allKinds)
            {
                int self = node.Kind == kind ? 1 : 0;
                int best = self;
                foreach (var nx in node.Next)
                {
                    if (!topoSet.Contains(nx)) continue;
                    int via = suffix[(nx, kind)];
                    int candidate = via + self;
                    if (candidate > best) best = candidate;
                }
                suffix[(nodeId, kind)] = best;
            }
        }

        // Badge for X = max # of X.Kind on any path through X
        //              = prefix[X, X.Kind] + suffix[X, X.Kind] - 1
        // (subtract 1 because X itself is double-counted).
        var badges = new Dictionary<int, NodeBadge>();
        foreach (var nodeId in topo)
        {
            var node = g.Get(nodeId);
            int p = prefix[(nodeId, node.Kind)];
            int s = suffix[(nodeId, node.Kind)];
            if (p < 0) continue; // not reachable from frontier
            int total = p + s - 1;
            badges[nodeId] = new NodeBadge(nodeId, node.Kind, total);
        }

        return new Result
        {
            Badges = badges,
            Prefix = prefix,
            Suffix = suffix,
            Sources = sources,
        };
    }

    /// <summary>
    /// All edges (a, b) that lie on at least one maximum-count path through
    /// <paramref name="hoveredNodeId"/> for the room kind of that node.
    /// Use this to render the highlight overlay.
    /// </summary>
    public static HashSet<(int From, int To)> EdgesForMaxPathsThrough(
        MapGraph g, Result r, int hoveredNodeId)
    {
        var edges = new HashSet<(int, int)>();
        if (!r.Badges.ContainsKey(hoveredNodeId)) return edges;
        var kind = g.Get(hoveredNodeId).Kind;

        // Forward trace: walk to leaves staying on edges that preserve the
        // max suffix count.
        var forwardStack = new Stack<int>();
        forwardStack.Push(hoveredNodeId);
        var seenF = new HashSet<int>();
        while (forwardStack.Count > 0)
        {
            int cur = forwardStack.Pop();
            if (!seenF.Add(cur)) continue;
            if (!g.Nodes.TryGetValue(cur, out var curNode)) continue;
            if (!r.Suffix.TryGetValue((cur, kind), out var curSuffix)) continue;
            int self = curNode.Kind == kind ? 1 : 0;
            foreach (var nx in curNode.Next)
            {
                if (!r.Suffix.TryGetValue((nx, kind), out var nxSuffix)) continue;
                if (nxSuffix + self == curSuffix)
                {
                    edges.Add((cur, nx));
                    forwardStack.Push(nx);
                }
            }
        }

        // Backward trace: walk to sources staying on edges that preserve the
        // max prefix count.
        var reverse = new Dictionary<int, List<int>>();
        foreach (var n in g.Nodes.Values)
            foreach (var nx in n.Next)
            {
                if (!reverse.TryGetValue(nx, out var list)) { list = new(); reverse[nx] = list; }
                list.Add(n.Id);
            }

        var backStack = new Stack<int>();
        backStack.Push(hoveredNodeId);
        var seenB = new HashSet<int>();
        while (backStack.Count > 0)
        {
            int cur = backStack.Pop();
            if (!seenB.Add(cur)) continue;
            if (!g.Nodes.TryGetValue(cur, out var curNode)) continue;
            if (!r.Prefix.TryGetValue((cur, kind), out var curPrefix)) continue;
            int self = curNode.Kind == kind ? 1 : 0;
            if (!reverse.TryGetValue(cur, out var preds)) continue;
            foreach (var p in preds)
            {
                if (!r.Prefix.TryGetValue((p, kind), out var pPrefix) || pPrefix < 0) continue;
                if (pPrefix + self == curPrefix)
                {
                    edges.Add((p, cur));
                    backStack.Push(p);
                }
            }
        }

        return edges;
    }

    private static List<int> TopologicalOrder(MapGraph g, IEnumerable<int> sources)
    {
        // Kahn's algorithm restricted to the subgraph reachable from sources.
        var reachable = new HashSet<int>();
        var dfs = new Stack<int>();
        foreach (var s in sources)
            if (g.Nodes.ContainsKey(s)) dfs.Push(s);
        while (dfs.Count > 0)
        {
            int cur = dfs.Pop();
            if (!reachable.Add(cur)) continue;
            if (!g.Nodes.TryGetValue(cur, out var node)) continue;
            foreach (var nx in node.Next)
                if (g.Nodes.ContainsKey(nx)) dfs.Push(nx);
        }

        var indeg = new Dictionary<int, int>();
        foreach (var id in reachable) indeg[id] = 0;
        foreach (var id in reachable)
            foreach (var nx in g.Get(id).Next)
                if (reachable.Contains(nx)) indeg[nx]++;

        var ready = new Queue<int>(reachable.Where(id => indeg[id] == 0));
        var order = new List<int>(reachable.Count);
        while (ready.Count > 0)
        {
            int cur = ready.Dequeue();
            order.Add(cur);
            foreach (var nx in g.Get(cur).Next)
            {
                if (!reachable.Contains(nx)) continue;
                if (--indeg[nx] == 0) ready.Enqueue(nx);
            }
        }
        return order;
    }
}
