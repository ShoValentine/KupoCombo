using System.Collections.Generic;
using KupoCombo.Models;

namespace KupoCombo.Services;

public interface ITrainingForecastPolicy
{
    IReadOnlyList<TrainingForecastStep> Forecast(
        TrainingState state,
        int maximumGcds);
}
