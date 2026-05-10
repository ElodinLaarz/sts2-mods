using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace PathPeek;

/// <summary>
/// Each frame, asks the game for <see cref="NMapScreen.Instance"/>. When that
/// reference appears (or changes), attaches a <see cref="MapOverlay"/> child
/// to it so badges + path highlights render on top. When it goes away, the
/// overlay is freed automatically because it lives under the screen subtree.
///
/// Polling is acceptable here — the cost is one static getter per frame, and
/// it sidesteps the need to install a Harmony patch on every Godot screen
/// transition.
/// </summary>
public sealed partial class MapScreenWatcher : Node
{
    private NMapScreen? _trackedScreen;
    private MapOverlay? _overlay;

    public override void _Process(double delta)
    {
        var screen = Bridge.FindActiveMapScreen();
        // Treat freed nodes as null so we re-attach when a new map appears.
        if (screen != null && !GodotObject.IsInstanceValid(screen)) screen = null;
        if (screen != null && !screen.IsInsideTree()) screen = null;

        if (screen == _trackedScreen) return;

        if (_overlay != null && GodotObject.IsInstanceValid(_overlay))
            _overlay.QueueFree();
        _overlay = null;

        _trackedScreen = screen;
        if (screen == null) return;

        _overlay = new MapOverlay { Name = nameof(MapOverlay) };
        screen.AddChild(_overlay);
        GD.Print(ModEntry.LogPrefix + "Attached overlay to NMapScreen (children=" + screen.GetChildCount() + ")");
    }
}
