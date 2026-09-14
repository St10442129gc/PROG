using SmartX.Api.Models;

namespace SmartX.Api.Services;

// =====================================================================
// ASSIGNMENT REQUIREMENT: DYNAMIC ENGAGEMENT FEATURE
// (the chosen strategy from the research report - not just a progress bar)
// =====================================================================

/// <summary>
/// Implements the "Sensor Health Score &amp; Streaks" engagement strategy chosen in
/// the research report. Every clean (non-anomalous) telemetry packet nudges a
/// sensor's score up and extends its streak; every anomaly or missed heartbeat
/// applies a sharp decay and resets the streak, which is what makes spikes and
/// disconnects immediately visible on the dashboard instead of getting lost in a
/// flat alert log.
/// </summary>
public class HealthScoreService
{
    private const int CleanReadingReward = 2;
    private const int AnomalyPenalty = 15;
    private const int DisconnectPenalty = 30;
    private const int MaxScore = 100;
    private const int MinScore = 0;

    public void RecordCleanReading(SensorProfile sensor)
    {
        sensor.HealthScore = Math.Min(MaxScore, sensor.HealthScore + CleanReadingReward);
        sensor.Streak += 1;
        sensor.Badge = BadgeFor(sensor.Streak, sensor.HealthScore);
    }

    public void RecordAnomaly(SensorProfile sensor)
    {
        sensor.HealthScore = Math.Max(MinScore, sensor.HealthScore - AnomalyPenalty);
        sensor.Streak = 0;
        sensor.Badge = BadgeFor(sensor.Streak, sensor.HealthScore);
    }

    public void RecordDisconnect(SensorProfile sensor)
    {
        sensor.HealthScore = Math.Max(MinScore, sensor.HealthScore - DisconnectPenalty);
        sensor.Streak = 0;
        sensor.Badge = "Disconnected";
    }

    private static string BadgeFor(int streak, int score) => streak switch
    {
        >= 100 => "Rock Solid (100+ streak)",
        >= 50 => "Reliable Veteran (50+ streak)",
        >= 20 => "Steady Streamer (20+ streak)",
        >= 5 => "Warming Up",
        _ when score < 40 => "Needs Attention",
        _ => "New Deployment"
    };

    /// <summary>Simple threshold-based anomaly check used by the ingestion endpoint.</summary>
    public static bool IsAnomalous(double value, double expectedMin, double expectedMax) =>
        value < expectedMin || value > expectedMax;
}
