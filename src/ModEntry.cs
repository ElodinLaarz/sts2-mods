using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace PathPeek;

[ModInitializer(nameof(ModLoaded))]
public static class ModEntry
{
    public const string ModId = "PathPeek";
    public const string LogPrefix = "[PathPeek] ";

    public static void ModLoaded()
    {
        GD.Print(LogPrefix + "Loading...");

        // Install the screen watcher onto the autoload tree so we get to
        // observe scene changes for the entire run lifetime.
        var tree = (SceneTree)Engine.GetMainLoop();
        var watcher = new MapScreenWatcher();
        watcher.Name = nameof(MapScreenWatcher);
        tree.Root.CallDeferred(Node.MethodName.AddChild, watcher);

        GD.Print(LogPrefix + "Loaded.");
    }
}
