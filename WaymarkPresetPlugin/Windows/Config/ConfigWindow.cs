using System;
using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace WaymarkPresetPlugin.Windows.Config;

public partial class ConfigWindow : Window, IDisposable
{
    private readonly Plugin Plugin;

    public ConfigWindow(Plugin plugin) : base("Configuration##WaymarkPresetPlugin")
    {
        Plugin = plugin;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(450, 540),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        InitHelp();
    }

    public void Dispose()
    {
        CoordinateSystemsDiagram?.Dispose();
    }

    public override void Draw()
    {
        using var tabBar = ImRaii.TabBar("##ConfigTabBar");
        if (!tabBar.Success)
            return;

        Settings();

        Help();

        About();
    }
}
