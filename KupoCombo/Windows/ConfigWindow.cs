using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace KupoCombo.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin)
        : base(
            "KupoCombo Settings###KupoComboSettings",
            ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;

        Size = new Vector2(450, 520);
        SizeCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 420),
            MaximumSize = new Vector2(750, 760)
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        ImGui.Text("Sequence overlay appearance");
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

        ImGui.Text("Training prompts");
        ImGui.Separator();
        ImGui.Spacing();

        DrawPromptEnabledSetting();
        ImGui.Spacing();
        DrawPromptScaleSetting();
        ImGui.Spacing();
        DrawPromptTextScaleSetting();
        ImGui.Spacing();
        DrawPromptMoogleScaleSetting();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button(
                "Show Test Moogle Prompt",
                new Vector2(220, 0)))
        {
            plugin.ShowTestPrompt();
        }

        if (ImGui.Button(
                "Reset Appearance",
                new Vector2(220, 0)))
        {
            configuration.OverlayTransparent = true;
            configuration.OverlayIconScale = 1.0f;
            configuration.OverlayTextScale = 1.0f;
            configuration.OverlayIconSpacing = 12.0f;
            configuration.ShowTrainingPrompts = true;
            configuration.PromptScale = 1.0f;
            configuration.PromptTextScale = 1.0f;
            configuration.PromptMoogleScale = 1.0f;
            configuration.Save();
        }

        ImGui.Spacing();
        ImGui.TextDisabled(
            "Drag the transparent overlays to position them. " +
            "The prompt bubble and moogle move together.");
    }

    private void DrawTransparencySetting()
    {
        var transparent = configuration.OverlayTransparent;

        if (ImGui.Checkbox("Transparent sequence overlay", ref transparent))
        {
            configuration.OverlayTransparent = transparent;
            configuration.Save();
        }
    }

    private void DrawIconScaleSetting()
    {
        var percentage = (int)MathF.Round(
            configuration.OverlayIconScale * 100f);

        ImGui.SetNextItemWidth(260);

        if (ImGui.SliderInt(
                "Icon size",
                ref percentage,
                50,
                200,
                "%d%%"))
        {
            configuration.OverlayIconScale = percentage / 100f;
            configuration.Save();
        }
    }

    private void DrawTextScaleSetting()
    {
        var percentage = (int)MathF.Round(
            configuration.OverlayTextScale * 100f);

        ImGui.SetNextItemWidth(260);

        if (ImGui.SliderInt(
                "Skill text size",
                ref percentage,
                50,
                200,
                "%d%%"))
        {
            configuration.OverlayTextScale = percentage / 100f;
            configuration.Save();
        }
    }

    private void DrawIconSpacingSetting()
    {
        var spacing = configuration.OverlayIconSpacing;

        ImGui.SetNextItemWidth(260);

        if (ImGui.SliderFloat(
                "Icon spacing",
                ref spacing,
                -60f,
                60f,
                "%.0f px"))
        {
            configuration.OverlayIconSpacing = spacing;
            configuration.Save();
        }
    }

    private void DrawPromptEnabledSetting()
    {
        var enabled = configuration.ShowTrainingPrompts;

        if (ImGui.Checkbox("Show moogle training prompts", ref enabled))
        {
            plugin.SetTrainingPromptsEnabled(enabled);
        }
    }

    private void DrawPromptScaleSetting()
    {
        var percentage = (int)MathF.Round(
            configuration.PromptScale * 100f);

        ImGui.SetNextItemWidth(260);

        if (ImGui.SliderInt(
                "Speech bubble size",
                ref percentage,
                60,
                200,
                "%d%%"))
        {
            configuration.PromptScale = percentage / 100f;
            configuration.Save();
        }
    }

    private void DrawPromptTextScaleSetting()
    {
        var percentage = (int)MathF.Round(
            configuration.PromptTextScale * 100f);

        ImGui.SetNextItemWidth(260);

        if (ImGui.SliderInt(
                "Prompt text size",
                ref percentage,
                60,
                200,
                "%d%%"))
        {
            configuration.PromptTextScale = percentage / 100f;
            configuration.Save();
        }
    }

    private void DrawPromptMoogleScaleSetting()
    {
        var percentage = (int)MathF.Round(
            configuration.PromptMoogleScale * 100f);

        ImGui.SetNextItemWidth(260);

        if (ImGui.SliderInt(
                "Moogle size",
                ref percentage,
                60,
                200,
                "%d%%"))
        {
            configuration.PromptMoogleScale = percentage / 100f;
            configuration.Save();
        }
    }
}
