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
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;

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
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    [PluginService]
    internal static IJobGauges JobGauges { get; private set; } = null!;

    [PluginService]
    internal static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/kupocombo";

    private static readonly TimeSpan StateRefreshInterval =
        TimeSpan.FromMilliseconds(100);

    private string SequenceDirectory { get; }

    private string GuidanceDirectory { get; }

    private DateTime nextStateRefreshUtc = DateTime.MinValue;

    public string PromptMooglePath { get; }

    public Configuration Configuration { get; }

    public TrainingSession TrainingSession { get; } = new();

    public PromptManager PromptManager { get; } = new();

    public GuidanceFile Guidance { get; private set; } = new();

    public IReadOnlyList<SequenceDefinition> Sequences { get; private set; } =
        Array.Empty<SequenceDefinition>();

    public SequenceDefinition? SelectedSequence { get; private set; }

    public RulePolicyDefinition? CurrentRulePolicy { get; private set; }

    public string CurrentJob { get; private set; } = string.Empty;

    public int CurrentSequenceLength =>
        TrainingSession.Length > 0
            ? TrainingSession.Length
            : SelectedSequence?.Actions.Count ?? 0;

    public string SelectedSequenceName =>
        TrainingSession.Policy?.Name
        ?? SelectedSequence?.DisplayName
        ?? "No sequence selected";

    public bool IsTraining => TrainingSession.IsActive;

    public bool IsSequenceComplete => TrainingSession.IsComplete;

    public bool IsDynamicPractice => TrainingSession.IsEndless;

    public bool HasDynamicPractice => CurrentRulePolicy != null;

    public int CurrentStep => TrainingSession.CurrentStep;

    public bool OverlayVisible => OverlayWindow.IsOpen;

    public WindowSystem WindowSystem { get; } = new("KupoCombo");

    private ConfigWindow ConfigWindow { get; }

    private MainWindow MainWindow { get; }

    private OverlayWindow OverlayWindow { get; }

    private PromptOverlayWindow PromptOverlayWindow { get; }

    private ActionWatcher ActionWatcher { get; }

    private TrainingStateReader TrainingStateReader { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration
            ?? new Configuration();

        var pluginDirectory = PluginInterface.AssemblyLocation.Directory?.FullName
            ?? throw new InvalidOperationException(
                "Could not determine the KupoCombo plugin directory.");

        SequenceDirectory = ResolveDataDirectory(
            pluginDirectory,
            "Sequences");

        GuidanceDirectory = ResolveDataDirectory(
            pluginDirectory,
            "Guidance");

        PromptMooglePath = ResolveAssetPath(
            pluginDirectory,
            "kupoicon.png");

        TrainingStateReader = new TrainingStateReader(
            JobGauges,
            ObjectTable,
            PlayerState);

        ActionWatcher = new ActionWatcher(GameInteropProvider);
        ActionWatcher.ActionUsed += OnActionUsed;

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        OverlayWindow = new OverlayWindow(this);
        PromptOverlayWindow = new PromptOverlayWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(OverlayWindow);
        WindowSystem.AddWindow(PromptOverlayWindow);

        CommandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                HelpMessage =
                    "Open KupoCombo. Use /kupocombo refresh " +
                    "to reload the current job's sequences, guidance, and policy."
            });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Framework.Update += OnFrameworkUpdate;
        ClientState.ClassJobChanged += OnClassJobChanged;
        ClientState.Login += OnLogin;
        ClientState.Logout += OnLogout;

        UpdateCurrentJobFromPlayerState();

        Log.Information(
            $"KupoCombo loaded. Sequence directory: {SequenceDirectory}. " +
            $"Guidance directory: {GuidanceDirectory}.");
    }

    public void StartTraining(SequenceDefinition sequence)
    {
        if (!sequence.Job.Equals(CurrentJob, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(
                $"Cannot start sequence '{sequence.Id}' because it belongs " +
                $"to {sequence.Job}, not {CurrentJob}.");
            return;
        }

        SelectedSequence = sequence;
        TrainingSession.Start(sequence);
        RefreshTrainingState();
        OverlayWindow.IsOpen = true;

        var sequenceGuidance = GetSelectedSequenceGuidance();
        ShowPrompt(sequenceGuidance?.StartPrompt);

        Log.Information($"Armed sequence: {sequence.Id}");
    }

    public void StartDynamicPractice()
    {
        var definition = CurrentRulePolicy;

        if (definition == null)
        {
            ChatGui.PrintError(
                $"No conditional practice policy is available for {CurrentJob}.",
                "KupoCombo");
            return;
        }

        var effectiveLevel = PlayerState.IsLoaded
            ? PlayerState.EffectiveLevel
            : definition.MinimumLevel;

        SelectedSequence = null;
        TrainingSession.Start(
            new RuleSetTrainingPolicy(definition),
            Math.Max(effectiveLevel, definition.MinimumLevel));
        RefreshTrainingState();
        OverlayWindow.IsOpen = true;

        ShowPrompt(
            new TrainingPrompt
            {
                Text =
                    $"Endless {definition.Job} priority practice started. " +
                    "KupoCombo is reading live job state and evaluating data-driven rules.",
                DurationSeconds = 5f
            });

        Log.Information(
            $"Started {definition.Job} conditional priority practice " +
            $"with policy '{definition.Id}'.");
    }

    public void StopTraining()
    {
        ResetTrainingState();
        Log.Information("Stopped sequence.");
    }

    public void SimulateCorrectAction()
    {
        if (!IsTraining)
        {
            return;
        }

        var preferredActionId =
            TrainingSession.CurrentDecision?.PreferredActionId ?? 0;

        if (preferredActionId == 0)
        {
            return;
        }

        OnActionUsed(preferredActionId);
    }

    public void ShowTestPrompt()
    {
        ShowPrompt(
            new TrainingPrompt
            {
                Text = "Keep the sequence moving and watch your weave timing, kupo!",
                DurationSeconds = 5f
            });
    }

    public void SetTrainingPromptsEnabled(bool enabled)
    {
        Configuration.ShowTrainingPrompts = enabled;
        Configuration.Save();

        if (!enabled)
        {
            PromptManager.Clear();
            PromptOverlayWindow.IsOpen = false;
            return;
        }

        if (!IsTraining)
        {
            return;
        }

        var sequenceGuidance = GetSelectedSequenceGuidance();
        var prompt = CurrentStep == 0
            ? sequenceGuidance?.StartPrompt
            : GetStepGuidance(CurrentStep)?.Prompt;

        ShowPrompt(prompt);
    }

    public SequenceGuidance? GetSelectedSequenceGuidance()
    {
        if (SelectedSequence == null)
        {
            return null;
        }

        return Guidance.Sequences.FirstOrDefault(
            item => item.SequenceId.Equals(
                SelectedSequence.Id,
                StringComparison.OrdinalIgnoreCase));
    }

    public StepGuidance? GetStepGuidance(int zeroBasedStep)
    {
        if (zeroBasedStep < 0)
        {
            return null;
        }

        return GetSelectedSequenceGuidance()?.Steps.FirstOrDefault(
            item => item.Step == zeroBasedStep + 1);
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
        Framework.Update -= OnFrameworkUpdate;
        ClientState.ClassJobChanged -= OnClassJobChanged;
        ClientState.Login -= OnLogin;
        ClientState.Logout -= OnLogout;

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
        PromptOverlayWindow.Dispose();
    }

    private static string ResolveDataDirectory(
        string pluginDirectory,
        string folderName)
    {
        var directory = new DirectoryInfo(pluginDirectory);

        for (var level = 0; level < 6 && directory != null; level++)
        {
            var developmentDirectory = Path.Combine(
                directory.FullName,
                "Data",
                folderName);

            if (Directory.Exists(developmentDirectory))
            {
                return developmentDirectory;
            }

            directory = directory.Parent;
        }

        return Path.Combine(pluginDirectory, folderName);
    }

    private static string ResolveAssetPath(
        string pluginDirectory,
        string fileName)
    {
        var directory = new DirectoryInfo(pluginDirectory);

        for (var level = 0; level < 6 && directory != null; level++)
        {
            var developmentPath = Path.Combine(
                directory.FullName,
                "Data",
                fileName);

            if (File.Exists(developmentPath))
            {
                return developmentPath;
            }

            directory = directory.Parent;
        }

        return Path.Combine(pluginDirectory, "Assets", fileName);
    }

    private string GetCurrentSequenceFilePath()
    {
        return string.IsNullOrWhiteSpace(CurrentJob)
            ? string.Empty
            : Path.Combine(SequenceDirectory, $"{CurrentJob}.json");
    }

    private string GetCurrentGuidanceFilePath()
    {
        return string.IsNullOrWhiteSpace(CurrentJob)
            ? string.Empty
            : Path.Combine(GuidanceDirectory, $"{CurrentJob}.json");
    }

    private void OnCommand(string command, string args)
    {
        var argument = args.Trim();

        if (argument.Length == 0)
        {
            MainWindow.Toggle();
            return;
        }

        if (argument.Equals("refresh", StringComparison.OrdinalIgnoreCase))
        {
            ReloadSequences();
            return;
        }

        ChatGui.PrintError(
            "Unknown command. Use /kupocombo or /kupocombo refresh.",
            "KupoCombo");
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!IsTraining || DateTime.UtcNow < nextStateRefreshUtc)
        {
            return;
        }

        RefreshTrainingState();
    }

    private void OnLogin()
    {
        UpdateCurrentJobFromPlayerState();
    }

    private void OnLogout(int type, int code)
    {
        SetCurrentJob(string.Empty);
    }

    private void OnClassJobChanged(uint classJobId)
    {
        var classJobSheet = DataManager.GetExcelSheet<ClassJob>();

        if (!classJobSheet.TryGetRow(classJobId, out var classJob))
        {
            Log.Warning($"Could not resolve ClassJob ID {classJobId}.");
            SetCurrentJob(string.Empty);
            return;
        }

        SetCurrentJob(classJob.Abbreviation.ToString());
    }

    private void UpdateCurrentJobFromPlayerState()
    {
        if (!PlayerState.IsLoaded || !PlayerState.ClassJob.IsValid)
        {
            SetCurrentJob(string.Empty);
            return;
        }

        SetCurrentJob(
            PlayerState.ClassJob.Value.Abbreviation.ToString());
    }

    private void SetCurrentJob(string jobAbbreviation)
    {
        var normalisedJob = jobAbbreviation.Trim().ToUpperInvariant();

        if (CurrentJob.Equals(normalisedJob, StringComparison.Ordinal))
        {
            return;
        }

        CurrentJob = normalisedJob;
        Sequences = Array.Empty<SequenceDefinition>();
        Guidance = new GuidanceFile();
        SelectedSequence = null;
        CurrentRulePolicy = null;
        ResetTrainingState();

        if (string.IsNullOrWhiteSpace(CurrentJob))
        {
            Log.Information("No current job is available.");
            return;
        }

        Log.Information($"Current job changed to {CurrentJob}.");
        LoadCurrentRulePolicy(preserveExistingOnFailure: false);
        LoadCurrentJobData(preserveExistingOnFailure: false);
    }

    private bool LoadCurrentRulePolicy(bool preserveExistingOnFailure)
    {
        if (string.IsNullOrWhiteSpace(CurrentJob))
        {
            return false;
        }

        var effectiveLevel = PlayerState.IsLoaded
            ? PlayerState.EffectiveLevel
            : 0;

        try
        {
            CurrentRulePolicy = RulePolicyRuntimeLoader.LoadBestProfile(
                CurrentJob,
                effectiveLevel);

            Log.Information(
                $"Loaded {CurrentJob} rule policy " +
                $"'{CurrentRulePolicy.Id}'.");
            return true;
        }
        catch (FileNotFoundException)
        {
            Log.Information(
                $"No rule policy file exists for {CurrentJob}.");

            if (!preserveExistingOnFailure)
            {
                CurrentRulePolicy = null;
            }

            return false;
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                $"Failed to load {CurrentJob} rule policy.");

            if (!preserveExistingOnFailure)
            {
                CurrentRulePolicy = null;
            }

            return false;
        }
    }

    private bool LoadCurrentJobData(bool preserveExistingOnFailure)
    {
        if (string.IsNullOrWhiteSpace(CurrentJob))
        {
            return false;
        }

        var sequenceFilePath = GetCurrentSequenceFilePath();

        if (!File.Exists(sequenceFilePath))
        {
            Log.Information(
                $"No sequence file exists for {CurrentJob}: {sequenceFilePath}");

            if (!preserveExistingOnFailure)
            {
                Sequences = Array.Empty<SequenceDefinition>();
                Guidance = new GuidanceFile();
                SelectedSequence = null;
                ResetTrainingState();
            }

            return false;
        }

        try
        {
            var previousSequenceId = SelectedSequence?.Id;
            var loadedSequences = SequenceLoader.Load(
                sequenceFilePath,
                CurrentJob);

            var loadedGuidance = GuidanceLoader.Load(
                GetCurrentGuidanceFilePath(),
                CurrentJob);

            var reselectedSequence = loadedSequences.FirstOrDefault(
                sequence => sequence.Id.Equals(
                    previousSequenceId,
                    StringComparison.Ordinal));

            Sequences = loadedSequences;
            Guidance = loadedGuidance;
            SelectedSequence = reselectedSequence ?? Sequences.FirstOrDefault();
            ResetTrainingState();

            Log.Information(
                $"Loaded {Sequences.Count} {CurrentJob} sequences and " +
                $"{Guidance.Sequences.Count} guidance profiles.");

            return true;
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                $"Failed to load {CurrentJob} training data.");

            if (!preserveExistingOnFailure)
            {
                Sequences = Array.Empty<SequenceDefinition>();
                Guidance = new GuidanceFile();
                SelectedSequence = null;
                ResetTrainingState();
            }

            return false;
        }
    }

    private void ReloadSequences()
    {
        if (string.IsNullOrWhiteSpace(CurrentJob))
        {
            ChatGui.PrintError(
                "No current job could be detected.",
                "KupoCombo");
            return;
        }

        var policyReloaded = LoadCurrentRulePolicy(
            preserveExistingOnFailure: true);
        var sequencesReloaded = LoadCurrentJobData(
            preserveExistingOnFailure: true);

        if (policyReloaded || sequencesReloaded)
        {
            var loadedParts = new List<string>();

            if (policyReloaded)
            {
                loadedParts.Add("rule policy");
            }

            if (sequencesReloaded)
            {
                loadedParts.Add(
                    $"{Sequences.Count} sequence(s) and guidance");
            }

            ChatGui.Print(
                $"Reloaded {CurrentJob} {string.Join(" and ", loadedParts)}.",
                "KupoCombo");
            return;
        }

        ChatGui.PrintError(
            $"Could not reload {CurrentJob} training data. " +
            "The previous data remains active. Check /xllog for details.",
            "KupoCombo");
    }

    private void ResetTrainingState()
    {
        TrainingSession.Stop();
        PromptManager.Clear();
        OverlayWindow.IsOpen = false;
        PromptOverlayWindow.IsOpen = false;
        nextStateRefreshUtc = DateTime.MinValue;
    }

    private void RefreshTrainingState()
    {
        var policy = TrainingSession.Policy;

        if (!TrainingSession.IsActive || policy == null)
        {
            return;
        }

        try
        {
            TrainingSession.RefreshState(
                state => TrainingStateReader.Refresh(state, policy));
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                "Failed to refresh the conditional training state.");
        }
        finally
        {
            nextStateRefreshUtc =
                DateTime.UtcNow + StateRefreshInterval;
        }
    }

    private void OnActionUsed(uint actionId)
    {
        RefreshTrainingState();

        var result = TrainingSession.ProcessAction(actionId);
        nextStateRefreshUtc = DateTime.MinValue;

        switch (result.Outcome)
        {
            case TrainingActionOutcome.Ignored:
                return;

            case TrainingActionOutcome.Correct:
                Log.Information(
                    $"Correct action: {actionId}. Progress: " +
                    $"{CurrentStep}/{CurrentSequenceLength}");

                ShowPrompt(GetStepGuidance(CurrentStep)?.Prompt);
                return;

            case TrainingActionOutcome.Acceptable:
                Log.Information(
                    $"Accepted alternative action: {actionId}. " +
                    $"Preferred: {result.ExpectedActionId}.");
                return;

            case TrainingActionOutcome.Incorrect:
                Log.Warning(
                    $"Incorrect action: {result.UsedActionId}. Expected: " +
                    $"{result.ExpectedActionId}. Recalculating training state.");

                var mistakePrompt =
                    GetSelectedSequenceGuidance()?.MistakePrompt;

                if (mistakePrompt == null && IsDynamicPractice)
                {
                    mistakePrompt = new TrainingPrompt
                    {
                        Text = result.DecisionReason,
                        DurationSeconds = 3f
                    };
                }

                ShowPrompt(mistakePrompt);
                return;

            case TrainingActionOutcome.Completed:
                Log.Information(
                    $"Completed sequence: {SelectedSequence?.Id}");

                ShowPrompt(GetSelectedSequenceGuidance()?.CompletionPrompt);
                return;
        }
    }

    private void ShowPrompt(TrainingPrompt? prompt)
    {
        if (!Configuration.ShowTrainingPrompts || prompt == null)
        {
            return;
        }

        PromptManager.Show(prompt);
        PromptOverlayWindow.IsOpen = PromptManager.IsVisible;
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
