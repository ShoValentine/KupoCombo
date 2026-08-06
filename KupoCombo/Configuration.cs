using System;
using Dalamud.Configuration;

namespace KupoCombo;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public float OverlayIconScale { get; set; } = 1.0f;

    public float OverlayTextScale { get; set; } = 1.0f;

    public bool OverlayTransparent { get; set; } = true;

    public float OverlayIconSpacing { get; set; } = 12.0f;

    public bool ShowTrainingPrompts { get; set; } = true;

    public float PromptScale { get; set; } = 1.0f;

    public float PromptTextScale { get; set; } = 1.0f;

    public float PromptMoogleScale { get; set; } = 1.0f;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
