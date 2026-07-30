using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using KupoCombo.Models;
using KupoCombo.Services;
using KupoCombo.Windows;

namespace KupoCombo;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static IGameInteropProvider GameInteropProvider
    {
        get;
        private set;
    } = null!;

    [PluginService]
    internal static ITextureProvider TextureProvider { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IClientState ClientState { get; private set; } = null!;

    [PluginService]
    internal static IPlayerState PlayerState { get; private set; } = null!;

    [PluginService]
    internal static IDataManager DataManager { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/kupocombo";

    public Configuration Configuration { get; }

    public IReadOnlyList<SequenceDefinition> Sequences { get; }

    public SequenceDefinition? SelectedSequence { get; private set; }

    public int CurrentSequenceLength =>
        SelectedSequence?.Actions.Count ?? 0;

    public string SelectedSequenceName =>
        SelectedSequence?.DisplayName ?? "No sequence selected";

    public bool IsTraining { get; private set; }

    public bool IsSequenceComplete { get; private set; }

    public int CurrentStep { get; private set; }

    public bool OverlayVisible => OverlayWindow.IsOpen;

    public WindowSystem WindowSystem { get; } = new("KupoCombo");

    private ConfigWindow ConfigWindow { get; }

    private MainWindow MainWindow { get; }

    private OverlayWindow OverlayWindow { get; }
    private ActionWatcher ActionWatcher { get; }

    public Plugin()
    {
        Configuration =
            PluginInterface.GetPluginConfig() as Configuration
            ?? new Configuration();

        var pluginDirectory =
            PluginInterface.AssemblyLocation.Directory?.FullName
            ?? throw new InvalidOperationException(
                "Could not determine the KupoCombo plugin directory.");

        var sequenceFilePath =
            Path.Combine(pluginDirectory, "Sequences.json");

        try
        {
            Sequences = SequenceLoader.Load(sequenceFilePath);

            SelectedSequence =
                Sequences.Count > 0
                    ? Sequences[0]
                    : null;

            Log.Information(
                $"Loaded {Sequences.Count} sequence definitions.");
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                "Failed to load KupoCombo sequence definitions.");

            Sequences = Array.Empty<SequenceDefinition>();
            SelectedSequence = null;
        }

        ActionWatcher =
            new ActionWatcher(GameInteropProvider);

        ActionWatcher.ActionUsed += OnActionUsed;

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        OverlayWindow = new OverlayWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(OverlayWindow);

        CommandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                HelpMessage = "Open the KupoCombo control window."
            });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("KupoCombo loaded.");
    }

    public void StartTraining(SequenceDefinition sequence)
    {
        SelectedSequence = sequence;
        CurrentStep = 0;
        IsSequenceComplete = false;
        IsTraining = true;
        OverlayWindow.IsOpen = true;

        Log.Information(
            $"Started sequence: {sequence.Id}");
    }

    public void StopTraining()
    {
        IsTraining = false;
        IsSequenceComplete = false;
        CurrentStep = 0;
        OverlayWindow.IsOpen = false;

        Log.Information("Stopped sequence.");
    }

    public void SimulateCorrectAction()
    {
        if (!IsTraining ||
            SelectedSequence == null ||
            CurrentStep >= CurrentSequenceLength)
        {
            return;
        }

        var expectedActionId =
            SelectedSequence.Actions[CurrentStep];

        OnActionUsed(expectedActionId);
    }

    public void ToggleOverlay()
    {
        if (!IsTraining && !IsSequenceComplete)
        {
            return;
        }

        OverlayWindow.IsOpen = !OverlayWindow.IsOpen;
    }

    public void Dispose()
    {

        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        ActionWatcher.ActionUsed -= OnActionUsed;
        ActionWatcher.Dispose();

        CommandManager.RemoveHandler(CommandName);

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        OverlayWindow.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
    }

    private void OnActionUsed(uint actionId)
    {
        if (!IsTraining || SelectedSequence == null)
        {
            return;
        }

        if (CurrentStep >= SelectedSequence.Actions.Count)
        {
            return;
        }

        var expectedActionId =
            SelectedSequence.Actions[CurrentStep];

        Log.Information(
            $"Detected action {actionId}. " +
            $"Expected action {expectedActionId} " +
            $"at step {CurrentStep + 1}.");

        if (actionId == expectedActionId)
        {
            HandleCorrectAction(actionId);
            return;
        }

        HandleIncorrectAction(
            actionId,
            expectedActionId);
    }

    private void HandleCorrectAction(uint actionId)
    {
        CurrentStep++;

        Log.Information(
            $"Correct action: {actionId}. " +
            $"Progress: {CurrentStep}/{CurrentSequenceLength}");

        if (CurrentStep < CurrentSequenceLength)
        {
            return;
        }

        CurrentStep = CurrentSequenceLength;
        IsTraining = false;
        IsSequenceComplete = true;

        Log.Information(
            $"Completed sequence: {SelectedSequence?.Id}");
    }

    private void HandleIncorrectAction(
        uint actionId,
        uint expectedActionId)
    {
        Log.Warning(
            $"Incorrect action: {actionId}. " +
            $"Expected: {expectedActionId}. " +
            "Resetting sequence progress.");

        CurrentStep = 0;
        IsSequenceComplete = false;
    }

    public void ToggleConfigUi()
    {
        ConfigWindow.Toggle();
    }

    public void ToggleMainUi()
    {
        MainWindow.Toggle();
    }
}
