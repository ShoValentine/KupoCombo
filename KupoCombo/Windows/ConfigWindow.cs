using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace KupoCombo.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin)
        : base(
            "KupoCombo Settings###KupoComboSettings",
            ImGuiWindowFlags.NoCollapse)
    {
        configuration = plugin.Configuration;

        Size = new Vector2(430, 290);
        SizeCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 260),
            MaximumSize = new Vector2(700, 500)
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        ImGui.Text("Overlay appearance");
        ImGui.Separator();
        ImGui.Spacing();

        DrawTransparencySetting();

        ImGui.Spacing();

        DrawIconScaleSetting();

        ImGui.Spacing();

        DrawTextScaleSetting();

        ImGui.Spacing();

        DrawIconSpacingSetting();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button(
                "Reset Overlay Appearance",
                new Vector2(220, 0)))
        {
            configuration.OverlayTransparent = true;
            configuration.OverlayIconScale = 1.0f;
            configuration.OverlayTextScale = 1.0f;
            configuration.OverlayIconSpacing = 12.0f;

            configuration.Save();
        }

        ImGui.Spacing();

        ImGui.TextDisabled(
            "Resize the overlay by dragging its edges " +
            "or bottom-right corner.");
    }

    private void DrawTransparencySetting()
    {
        var transparent =
            configuration.OverlayTransparent;

        if (ImGui.Checkbox(
                "Transparent overlay",
                ref transparent))
        {
            configuration.OverlayTransparent =
                transparent;

            configuration.Save();
        }
    }

    private void DrawIconScaleSetting()
    {
        var iconPercentage =
            (int)MathF.Round(
                configuration.OverlayIconScale * 100f);

        ImGui.SetNextItemWidth(260);

        if (ImGui.SliderInt(
                "Icon size",
                ref iconPercentage,
                50,
                200,
                "%d%%"))
        {
            configuration.OverlayIconScale =
                iconPercentage / 100f;

            configuration.Save();
        }
    }

    private void DrawTextScaleSetting()
    {
        var textPercentage =
            (int)MathF.Round(
                configuration.OverlayTextScale * 100f);

        ImGui.SetNextItemWidth(260);

        if (ImGui.SliderInt(
                "Text size",
                ref textPercentage,
                50,
                200,
                "%d%%"))
        {
            configuration.OverlayTextScale =
                textPercentage / 100f;

            configuration.Save();
        }
    }

    private void DrawIconSpacingSetting()
    {
        var iconSpacing =
            configuration.OverlayIconSpacing;

        ImGui.SetNextItemWidth(260);

        if (ImGui.SliderFloat(
                "Icon spacing",
                ref iconSpacing,
                -60f,
                60f,
                "%.0f px"))
        {
            configuration.OverlayIconSpacing =
                iconSpacing;

            configuration.Save();
        }
    }
}
