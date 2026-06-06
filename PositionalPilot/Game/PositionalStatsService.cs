using PositionalPilot.Core.Model;

namespace PositionalPilot.Game;

internal sealed class PositionalStatsService
{
    private readonly Configuration config;
    private readonly PositionalStatsBook sessionStats = new();

    public PositionalStatsService(Configuration config)
    {
        this.config = config;
        config.Settings.LifetimeStats ??= new PositionalStatsBook();
    }

    public PositionalStatsBook SessionStats => sessionStats;
    public PositionalStatsBook LifetimeStats => config.Settings.LifetimeStats;

    public void RecordSuccess(uint jobId)
    {
        if (!config.Settings.TrackSuccessfulPositionals || jobId == 0)
            return;

        sessionStats.RecordSuccess(jobId);
        config.Settings.LifetimeStats.RecordSuccess(jobId);
        config.Save();
    }

    public void ClearSession() => sessionStats.Clear();

    public void ClearLifetime()
    {
        config.Settings.LifetimeStats.Clear();
        config.Save();
    }

    public void ClearLifetimeJob(uint jobId)
    {
        config.Settings.LifetimeStats.ClearJob(jobId);
        config.Save();
    }
}
