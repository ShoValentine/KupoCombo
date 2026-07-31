using System;
using KupoCombo.Models;

namespace KupoCombo.Services;

public sealed class PromptManager
{
    private DateTime visibleUntilUtc;

    public string Text { get; private set; } = string.Empty;

    public bool IsVisible =>
        !string.IsNullOrWhiteSpace(Text) &&
        DateTime.UtcNow < visibleUntilUtc;

    public void Show(TrainingPrompt? prompt)
    {
        if (prompt == null || string.IsNullOrWhiteSpace(prompt.Text))
        {
            return;
        }

        Text = prompt.Text.Trim();
        visibleUntilUtc = DateTime.UtcNow.AddSeconds(
            Math.Clamp(prompt.DurationSeconds, 1f, 15f));
    }

    public void Show(string text, float durationSeconds = 4f)
    {
        Show(new TrainingPrompt
        {
            Text = text,
            DurationSeconds = durationSeconds
        });
    }

    public void Clear()
    {
        Text = string.Empty;
        visibleUntilUtc = DateTime.MinValue;
    }
}
