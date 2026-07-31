using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KupoCombo.Models;

namespace KupoCombo.Services;

public static class GuidanceLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static GuidanceFile Load(
        string filePath,
        string expectedJob)
    {
        var guidanceDirectory = Path.GetDirectoryName(filePath);
        var dataDirectory = guidanceDirectory == null
            ? null
            : Directory.GetParent(guidanceDirectory)?.FullName;

        var sequenceFilePath = dataDirectory == null
            ? string.Empty
            : Path.Combine(
                dataDirectory,
                "Sequences",
                $"{expectedJob}.json");

        IReadOnlyCollection<SequenceDefinition> sequences =
            File.Exists(sequenceFilePath)
                ? SequenceLoader.Load(sequenceFilePath, expectedJob)
                : Array.Empty<SequenceDefinition>();

        return Load(filePath, expectedJob, sequences);
    }

    public static GuidanceFile Load(
        string filePath,
        string expectedJob,
        IReadOnlyCollection<SequenceDefinition> sequences)
    {
        if (!File.Exists(filePath))
        {
            return new GuidanceFile
            {
                SchemaVersion = 1,
                Job = expectedJob
            };
        }

        var json = File.ReadAllText(filePath);
        var guidance = JsonSerializer.Deserialize<GuidanceFile>(json, JsonOptions)
            ?? throw new InvalidDataException(
                $"{Path.GetFileName(filePath)} could not be deserialized.");

        if (guidance.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported guidance schema version: {guidance.SchemaVersion}");
        }

        if (!guidance.Job.Equals(expectedJob, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Guidance file belongs to '{guidance.Job}', not '{expectedJob}'.");
        }

        var duplicateSequence = guidance.Sequences
            .GroupBy(item => item.SequenceId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateSequence != null)
        {
            throw new InvalidDataException(
                $"Duplicate guidance sequence ID: {duplicateSequence.Key}");
        }

        var sequenceLookup = sequences.ToDictionary(
            sequence => sequence.Id,
            StringComparer.OrdinalIgnoreCase);

        foreach (var sequenceGuidance in guidance.Sequences)
        {
            ValidateSequenceGuidance(
                sequenceGuidance,
                sequenceLookup);
        }

        return guidance;
    }

    private static void ValidateSequenceGuidance(
        SequenceGuidance guidance,
        IReadOnlyDictionary<string, SequenceDefinition> sequenceLookup)
    {
        if (string.IsNullOrWhiteSpace(guidance.SequenceId))
        {
            throw new InvalidDataException(
                "Guidance sequence is missing its sequenceId.");
        }

        if (!sequenceLookup.TryGetValue(
                guidance.SequenceId,
                out var sequence))
        {
            throw new InvalidDataException(
                $"Guidance references unknown sequence '{guidance.SequenceId}'.");
        }

        if (guidance.Steps.Any(step => step.Step <= 0))
        {
            throw new InvalidDataException(
                $"Guidance for '{guidance.SequenceId}' contains a step below 1.");
        }

        if (guidance.Steps.Any(step => step.Step > sequence.Actions.Count))
        {
            throw new InvalidDataException(
                $"Guidance for '{guidance.SequenceId}' contains a step beyond " +
                $"the sequence length of {sequence.Actions.Count}.");
        }

        var duplicateStep = guidance.Steps
            .GroupBy(step => step.Step)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateStep != null)
        {
            throw new InvalidDataException(
                $"Guidance for '{guidance.SequenceId}' contains duplicate " +
                $"step {duplicateStep.Key}.");
        }

        ValidatePrompt(guidance.StartPrompt, guidance.SequenceId, "start");
        ValidatePrompt(guidance.MistakePrompt, guidance.SequenceId, "mistake");
        ValidatePrompt(guidance.CompletionPrompt, guidance.SequenceId, "completion");

        foreach (var step in guidance.Steps)
        {
            ValidatePrompt(
                step.Prompt,
                guidance.SequenceId,
                $"step {step.Step}");
        }
    }

    private static void ValidatePrompt(
        TrainingPrompt? prompt,
        string sequenceId,
        string location)
    {
        if (prompt == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(prompt.Text))
        {
            throw new InvalidDataException(
                $"Guidance for '{sequenceId}' has an empty {location} prompt.");
        }

        if (prompt.DurationSeconds <= 0f)
        {
            throw new InvalidDataException(
                $"Guidance for '{sequenceId}' has a non-positive " +
                $"{location} prompt duration.");
        }
    }
}
