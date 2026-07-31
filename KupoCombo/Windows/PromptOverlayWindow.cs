using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;

namespace KupoCombo.Windows;

public sealed class PromptOverlayWindow : Window, IDisposable
{
    private const float BaseBubbleWidth = 430f;
    private const float BaseBubbleHeight = 96f;
    private const float BaseMoogleSize = 128f;
    private const float BasePadding = 22f;
    private const float BaseMargin = 8f;
    private const float BaseOverlap = 20f;

    private readonly Plugin plugin;

    public PromptOverlayWindow(Plugin plugin)
        : base(
            "KupoCombo Training Prompt###KupoComboTrainingPrompt",
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoBackground |
            ImGuiWindowFlags.NoNavInputs |
            ImGuiWindowFlags.NoNavFocus |
            ImGuiWindowFlags.NoFocusOnAppearing)
    {
        this.plugin = plugin;

        IsOpen = false;
        ShowCloseButton = false;
        AllowPinning = false;
        AllowClickthrough = true;
        BgAlpha = 0f;

        Size = new Vector2(590f, 150f);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        if (!plugin.Configuration.ShowTrainingPrompts ||
            !plugin.PromptManager.IsVisible)
        {
            IsOpen = false;
            return;
        }

        var promptScale = Math.Clamp(
            plugin.Configuration.PromptScale,
            0.6f,
            2.0f);

        var textScale = Math.Clamp(
            plugin.Configuration.PromptTextScale,
            0.6f,
            2.0f);

        var moogleScale = Math.Clamp(
            plugin.Configuration.PromptMoogleScale,
            0.6f,
            2.0f);

        ImGui.SetWindowFontScale(textScale);

        var margin = BaseMargin * promptScale;
        var padding = BasePadding * promptScale;
        var overlap = BaseOverlap * promptScale;
        var bubbleWidth = BaseBubbleWidth * promptScale;
        var moogleSize = BaseMoogleSize * moogleScale;
        var wrapWidth = Math.Max(100f, bubbleWidth - padding * 2f);

        var unwrappedTextSize = ImGui.CalcTextSize(
            plugin.PromptManager.Text);

        var estimatedLineCount = Math.Max(
            1f,
            MathF.Ceiling(unwrappedTextSize.X / wrapWidth));

        var measuredTextHeight =
            ImGui.GetTextLineHeight() * estimatedLineCount;

        var bubbleHeight = Math.Max(
            BaseBubbleHeight * promptScale,
            measuredTextHeight + padding * 2f);

        var windowWidth =
            margin * 2f +
            moogleSize +
            bubbleWidth -
            overlap;

        var windowHeight =
            margin * 2f +
            Math.Max(moogleSize, bubbleHeight);

        ImGui.SetWindowSize(
            new Vector2(windowWidth, windowHeight),
            ImGuiCond.Always);

        var windowPosition = ImGui.GetWindowPos();
        var bubbleLocal = new Vector2(
            margin + moogleSize - overlap,
            margin);

        var bubbleMin = windowPosition + bubbleLocal;
        var bubbleMax = bubbleMin + new Vector2(
            bubbleWidth,
            bubbleHeight);

        var moogleLocal = new Vector2(
            margin,
            margin + Math.Max(0f, bubbleHeight - moogleSize));

        var drawList = ImGui.GetWindowDrawList();
        var bubbleFill = ImGui.ColorConvertFloat4ToU32(
            new Vector4(0.96f, 0.94f, 0.86f, 0.96f));
        var bubbleBorder = ImGui.ColorConvertFloat4ToU32(
            new Vector4(0.17f, 0.14f, 0.11f, 0.96f));

        drawList.AddRectFilled(
            bubbleMin,
            bubbleMax,
            bubbleFill,
            14f * promptScale);

        drawList.AddRect(
            bubbleMin,
            bubbleMax,
            bubbleBorder,
            14f * promptScale,
            ImDrawFlags.None,
            2f * promptScale);

        var tailCentreY = bubbleMin.Y + bubbleHeight * 0.68f;
        var tailTip = new Vector2(
            bubbleMin.X - 22f * promptScale,
            tailCentreY + 12f * promptScale);
        var tailTop = new Vector2(
            bubbleMin.X + 1f,
            tailCentreY - 15f * promptScale);
        var tailBottom = new Vector2(
            bubbleMin.X + 1f,
            tailCentreY + 17f * promptScale);

        drawList.AddTriangleFilled(
            tailTip,
            tailTop,
            tailBottom,
            bubbleFill);

        drawList.AddLine(
            tailTip,
            tailTop,
            bubbleBorder,
            2f * promptScale);

        drawList.AddLine(
            tailTip,
            tailBottom,
            bubbleBorder,
            2f * promptScale);

        DrawMoogle(moogleLocal, moogleSize);

        ImGui.SetCursorPos(
            bubbleLocal + new Vector2(padding, padding));

        ImGui.PushTextWrapPos(
            bubbleLocal.X + bubbleWidth - padding);

        ImGui.PushStyleColor(
            ImGuiCol.Text,
            new Vector4(0.12f, 0.10f, 0.08f, 1f));

        ImGui.TextWrapped(plugin.PromptManager.Text);

        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
    }

    private void DrawMoogle(
        Vector2 localPosition,
        float size)
    {
        ImGui.SetCursorPos(localPosition);

        if (File.Exists(plugin.PromptMooglePath))
        {
            var texture = Plugin.TextureProvider
                .GetFromFile(plugin.PromptMooglePath)
                .GetWrapOrEmpty();

            ImGui.Image(
                texture.Handle,
                new Vector2(size, size));
            return;
        }

        ImGui.Dummy(new Vector2(size, size));

        var drawList = ImGui.GetWindowDrawList();
        var itemMin = ImGui.GetItemRectMin();
        var itemMax = ImGui.GetItemRectMax();
        var centre = (itemMin + itemMax) / 2f;

        drawList.AddCircleFilled(
            centre,
            size * 0.42f,
            ImGui.ColorConvertFloat4ToU32(
                new Vector4(0.92f, 0.88f, 0.78f, 0.95f)));

        drawList.AddText(
            centre - ImGui.CalcTextSize("Kupo!") / 2f,
            ImGui.ColorConvertFloat4ToU32(
                new Vector4(0.12f, 0.10f, 0.08f, 1f)),
            "Kupo!");
    }
}
