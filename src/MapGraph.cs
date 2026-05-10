using System.Collections.Generic;

namespace PathPeek;

// Game-agnostic map representation. The STS2 adapter layer (Bridge.cs) is
// responsible for translating MegaCrit's MapPoint graph into this shape.
// Keeping the algorithm decoupled lets it be unit-tested without the game.

// One-to-one with MegaCrit.Sts2.Core.Map.MapPointType (minus Unassigned).
public enum RoomKind
{
    Unknown,    // the "?" room
    Monster,
    Elite,
    RestSite,
    Treasure,
    Shop,
    Boss,
    Ancient,
}

public sealed class MapNode
{
    public required int Id { get; init; }
    public required RoomKind Kind { get; init; }
    /// <summary>Map row, increasing toward the boss.</summary>
    public required int Row { get; init; }
    /// <summary>Ids of nodes reachable in one step forward.</summary>
    public List<int> Next { get; } = new();
}

public sealed class MapGraph
{
    public required Dictionary<int, MapNode> Nodes { get; init; }
    /// <summary>Id the player is currently sitting on, or null if pre-start.</summary>
    public required int? CurrentId { get; init; }
    /// <summary>Ids the player is allowed to step to next turn.</summary>
    public required IReadOnlyList<int> ChoiceIds { get; init; }

    public MapNode Get(int id) => Nodes[id];
}
