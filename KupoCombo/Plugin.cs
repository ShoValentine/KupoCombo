using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using KupoCombo.Models;
using KupoCombo.Services;
using KupoCombo.Windows;
using Lumina.Excel.Sheets;

namespace KupoCombo;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService]
    internal static IDalamudPluginInterface PluginInterface
    {
        get;
        private set;
    } = null!;

    [PluginService]
    internal static IGameInteropProvider GameInteropProvider
    {
        get;
        private set;
    } = null!;

    [PluginService]
    internal static ITextureProvider TextureProvider
    {
        get;
        private set;
    } = null!;

    [PluginService]
    internal static ICommandManager CommandManager
    {
        get;
        private set;
    } = null!;

    [PluginService]
    internal static IClientState ClientState
    {
        get;
        private set;
    } = null!;

    [PluginService]
    internal static IPlayerState PlayerState
    {
        get;
        private set;
    } = null!;

    [PluginService]
    internal static IDataManager DataManager
    {
        get;
        private set;
    } = null!;

    [PluginService]
    internal static IChatGui ChatGui
    {
        get;
        private set;
    } = null!;

    [PluginService]
    internal static IPluginLog Log
    {
        get;
        private set;
    } = null!;

    private const string CommandName = "/kupocombo";

    private string SequenceDirectory { get; }

    public Configuration Configuration { get; }

    public IReadOnlyList<SequenceDefinition> Sequences
    {
        get;
        private set;
    } = Array.Empty<SequenceDefinition>();

    public SequenceDefinition? SelectedSequence
    {
        get;
        private set;
    }

    public string CurrentJob
    {
        get;
        private set;
    } = string.Empty;

    public int CurrentSequenceLength =>
        SelectedSequence?.Actions.Count ?? 0;

    public string SelectedSequenceName =>
        SelectedSequence?.DisplayName
        ?? "No sequence selected";

    public bool IsTraining
    {
        get;
        private set;
    }

    public bool IsSequenceComplete
    {
        get;
        private set;
    }

    public int CurrentStep
    {
        get;
        private set;
    }

    public bool OverlayVisible =>
        OverlayWindow.IsOpen;

    public WindowSystem WindowSystem { get; } =
        new("KupoCombo");

    private ConfigWindow ConfigWindow { get; }

    private MainWindow MainWindow { get; }

    private OverlayWindow OverlayWindow { get; }

    private ActionWatcher ActionWatcher { get; }

    public Plugin()
    {
        Configuration =
            PluginInterface.GetPluginConfig()
                as Configuration
            ?? new Configuration();

        var pluginDirectory =
            PluginInterface.AssemblyLocation
                .Directory?
                .FullName
            ?? throw new InvalidOperationException(
                "Could not determine the KupoCombo plugin directory.");

        SequenceDirectory =
            ResolveSequenceDirectory(pluginDirectory);

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
                HelpMessage =
                    "Open KupoCombo. Use /kupocombo refresh " +
                    "to reload the current job's sequences."
            });

        PluginInterface.UiBuilder.Draw +=
            WindowSystem.Draw;

        PluginInterface.UiBuilder.OpenConfigUi +=
            ToggleConfigUi;

        PluginInterface.UiBuilder.OpenMainUi +=
            ToggleMainUi;

        ClientState.ClassJobChanged +=
            OnClassJobChanged;

        ClientState.Login +=
            OnLogin;

        ClientState.Logout +=
            OnLogout;

        UpdateCurrentJobFromPlayerState();

        Log.Information(
            $"KupoCombo loaded. Sequence directory: " +
            $"{SequenceDirectory}");
    }

    public void StartTraining(
        SequenceDefinition sequence)
    {
        if (!sequence.Job.Equals(
                CurrentJob,
                StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(
                $"Cannot start sequence '{sequence.Id}' because " +
                $"it belongs to {sequence.Job}, not {CurrentJob}.");

            return;
        }

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
        ResetTrainingState();

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
        if (!IsTraining &&
            !IsSequenceComplete)
        {
            return;
        }

        OverlayWindow.IsOpen =
            !OverlayWindow.IsOpen;
    }

    public void Dispose()
    {
        ClientState.ClassJobChanged -=
            OnClassJobChanged;

        ClientState.Login -=
            OnLogin;

        ClientState.Logout -=
            OnLogout;

        PluginInterface.UiBuilder.Draw -=
            WindowSystem.Draw;

        PluginInterface.UiBuilder.OpenConfigUi -=
            ToggleConfigUi;

        PluginInterface.UiBuilder.OpenMainUi -=
            ToggleMainUi;

        ActionWatcher.ActionUsed -=
            OnActionUsed;

        ActionWatcher.Dispose();

        CommandManager.RemoveHandler(
            CommandName);

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        OverlayWindow.Dispose();
    }

    private static string ResolveSequenceDirectory(
        string pluginDirectory)
    {
        var directory =
            new DirectoryInfo(pluginDirectory);

        for (var level = 0;
             level < 6 && directory != null;
             level++)
        {
            var developmentDirectory =
                Path.Combine(
                    directory.FullName,
                    "Data",
                    "Sequences");

            if (Directory.Exists(
                    developmentDirectory))
            {
                return developmentDirectory;
            }

            directory = directory.Parent;
        }

        return Path.Combine(
            pluginDirectory,
            "Sequences");
    }

    private string GetCurrentSequenceFilePath()
    {
        if (string.IsNullOrWhiteSpace(
                CurrentJob))
        {
            return string.Empty;
        }

        return Path.Combine(
            SequenceDirectory,
            $"{CurrentJob}.json");
    }

    private void OnCommand(
        string command,
        string args)
    {
        var argument = args.Trim();

        if (argument.Length == 0)
        {
            MainWindow.Toggle();
            return;
        }

        if (argument.Equals(
                "refresh",
                StringComparison.OrdinalIgnoreCase))
        {
            ReloadSequences();
            return;
        }

        ChatGui.PrintError(
            "Unknown command. Use /kupocombo " +
            "or /kupocombo refresh.",
            "KupoCombo");
    }

    private void OnLogin()
    {
        UpdateCurrentJobFromPlayerState();
    }

    private void OnLogout(
        int type,
        int code)
    {
        SetCurrentJob(string.Empty);
    }

    private void OnClassJobChanged(
        uint classJobId)
    {
        var classJobSheet =
            DataManager.GetExcelSheet<ClassJob>();

        if (!classJobSheet.TryGetRow(
                classJobId,
                out var classJob))
        {
            Log.Warning(
                $"Could not resolve ClassJob ID " +
                $"{classJobId}.");

            SetCurrentJob(string.Empty);
            return;
        }

        SetCurrentJob(
            classJob.Abbreviation.ToString());
    }

    private void UpdateCurrentJobFromPlayerState()
    {
        if (!PlayerState.IsLoaded ||
            !PlayerState.ClassJob.IsValid)
        {
            SetCurrentJob(string.Empty);
            return;
        }

        SetCurrentJob(
            PlayerState.ClassJob
                .Value
                .Abbreviation
                .ToString());
    }

    private void SetCurrentJob(
        string jobAbbreviation)
    {
        var normalisedJob =
            jobAbbreviation
                .Trim()
                .ToUpperInvariant();

        if (CurrentJob.Equals(
                normalisedJob,
                StringComparison.Ordinal))
        {
            return;
        }

        CurrentJob = normalisedJob;

        Sequences =
            Array.Empty<SequenceDefinition>();

        SelectedSequence = null;

        ResetTrainingState();

        if (string.IsNullOrWhiteSpace(
                CurrentJob))
        {
            Log.Information(
                "No current job is available.");

            return;
        }

        Log.Information(
            $"Current job changed to {CurrentJob}.");

        LoadSequencesForCurrentJob(
            preserveExistingOnFailure: false);
    }

    private bool LoadSequencesForCurrentJob(
        bool preserveExistingOnFailure)
    {
        if (string.IsNullOrWhiteSpace(
                CurrentJob))
        {
            return false;
        }

        var filePath =
            GetCurrentSequenceFilePath();

        if (!File.Exists(filePath))
        {
            Log.Information(
                $"No sequence file exists for " +
                $"{CurrentJob}: {filePath}");

            if (!preserveExistingOnFailure)
            {
                Sequences =
                    Array.Empty<SequenceDefinition>();

                SelectedSequence = null;

                ResetTrainingState();
            }

            return false;
        }

        try
        {
            var previousSequenceId =
                SelectedSequence?.Id;

            var loadedSequences =
                SequenceLoader.Load(
                    filePath,
                    CurrentJob);

            var reselectedSequence =
                loadedSequences.FirstOrDefault(
                    sequence =>
                        sequence.Id.Equals(
                            previousSequenceId,
                            StringComparison.Ordinal));

            Sequences = loadedSequences;

            SelectedSequence =
                reselectedSequence
                ?? Sequences.FirstOrDefault();

            ResetTrainingState();

            Log.Information(
                $"Loaded {Sequences.Count} " +
                $"{CurrentJob} sequence definitions " +
                $"from {filePath}.");

            return true;
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                $"Failed to load {CurrentJob} " +
                $"sequences from {filePath}.");

            if (!preserveExistingOnFailure)
            {
                Sequences =
                    Array.Empty<SequenceDefinition>();

                SelectedSequence = null;

                ResetTrainingState();
            }

            return false;
        }
    }

    private void ReloadSequences()
    {
        if (string.IsNullOrWhiteSpace(
                CurrentJob))
        {
            ChatGui.PrintError(
                "No current job could be detected.",
                "KupoCombo");

            return;
        }

        if (LoadSequencesForCurrentJob(
                preserveExistingOnFailure: true))
        {
            ChatGui.Print(
                $"Reloaded {Sequences.Count} " +
                $"{CurrentJob} sequences.",
                "KupoCombo");

            return;
        }

        ChatGui.PrintError(
            $"Could not reload " +
            $"{CurrentJob}.json. " +
            "The previous data remains active. " +
            "Check /xllog for details.",
            "KupoCombo");
    }

    private void ResetTrainingState()
    {
        IsTraining = false;
        IsSequenceComplete = false;
        CurrentStep = 0;
        OverlayWindow.IsOpen = false;
    }

    private void OnActionUsed(
        uint actionId)
    {
        if (!IsTraining ||
            SelectedSequence == null)
        {
            return;
        }

        if (CurrentStep >=
            SelectedSequence.Actions.Count)
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

    private void HandleCorrectAction(
        uint actionId)
    {
        CurrentStep++;

        Log.Information(
            $"Correct action: {actionId}. " +
            $"Progress: {CurrentStep}" +
            $"/{CurrentSequenceLength}");

        if (CurrentStep <
            CurrentSequenceLength)
        {
            return;
        }

        CurrentStep =
            CurrentSequenceLength;

        IsTraining = false;
        IsSequenceComplete = true;

        Log.Information(
            $"Completed sequence: " +
            $"{SelectedSequence?.Id}");
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
