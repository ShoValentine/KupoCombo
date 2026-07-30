using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KupoCombo.Models;

namespace KupoCombo.Services;

public static class SequenceLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<SequenceDefinition> Load(
        string filePath,
        string expectedJob)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                "The KupoCombo sequence file could not be found.",
                filePath);
        }

        var json = File.ReadAllText(filePath);

        var sequenceFile =
            JsonSerializer.Deserialize<SequenceFile>(
                json,
                JsonOptions)
            ?? throw new InvalidDataException(
                $"{Path.GetFileName(filePath)} could not be deserialized.");

        if (sequenceFile.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported sequence schema version: " +
                $"{sequenceFile.SchemaVersion}");
        }

        ValidateSequences(
            sequenceFile.Sequences,
            expectedJob);

        return sequenceFile.Sequences;
    }

    private static void ValidateSequences(
        IReadOnlyCollection<SequenceDefinition> sequences,
        string expectedJob)
    {
        var duplicateId = sequences
            .GroupBy(sequence => sequence.Id)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateId != null)
        {
            throw new InvalidDataException(
                $"Duplicate sequence ID: {duplicateId.Key}");
        }

        foreach (var sequence in sequences)
        {
            if (string.IsNullOrWhiteSpace(sequence.Id))
            {
                throw new InvalidDataException(
                    "A sequence is missing its ID.");
            }

            if (string.IsNullOrWhiteSpace(sequence.Name))
            {
                throw new InvalidDataException(
                    $"Sequence '{sequence.Id}' is missing its name.");
            }

            if (string.IsNullOrWhiteSpace(sequence.Job))
            {
                throw new InvalidDataException(
                    $"Sequence '{sequence.Id}' is missing its job.");
            }

            if (!sequence.Job.Equals(
                    expectedJob,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Sequence '{sequence.Id}' belongs to " +
                    $"'{sequence.Job}', but it was found inside " +
                    $"'{expectedJob}.json'.");
            }

            if (sequence.Actions.Count == 0)
            {
                throw new InvalidDataException(
                    $"Sequence '{sequence.Id}' contains no actions.");
            }

            if (sequence.Actions.Any(
                    actionId => actionId == 0))
            {
                throw new InvalidDataException(
                    $"Sequence '{sequence.Id}' contains action ID 0.");
            }
        }
    }
}
