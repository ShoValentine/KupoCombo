using System;
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

    public static GuidanceFile Load(string filePath, string expectedJob)
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
            .GroupBy(item => item.SequenceId)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateSequence != null)
        {
            throw new InvalidDataException(
                $"Duplicate guidance sequence ID: {duplicateSequence.Key}");
        }

        foreach (var sequence in guidance.Sequences)
        {
            if (string.IsNullOrWhiteSpace(sequence.SequenceId))
            {
                throw new InvalidDataException("Guidance sequence is missing its sequenceId.");
            }

            if (sequence.Steps.Any(step => step.Step <= 0))
            {
                throw new InvalidDataException(
                    $"Guidance for '{sequence.SequenceId}' contains a step below 1.");
            }

            var duplicateStep = sequence.Steps
                .GroupBy(step => step.Step)
                .FirstOrDefault(group => group.Count() > 1);

            if (duplicateStep != null)
            {
                throw new InvalidDataException(
                    $"Guidance for '{sequence.SequenceId}' contains duplicate step {duplicateStep.Key}.");
            }
        }

        return guidance;
    }
}
