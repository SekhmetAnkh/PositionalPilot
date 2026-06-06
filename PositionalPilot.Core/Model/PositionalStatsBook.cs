namespace PositionalPilot.Core.Model;

public sealed class PositionalStatsBook
{
    public Dictionary<uint, PositionalClassStats> ByJob { get; set; } = new();

    public long TotalSuccessfulPositionals => ByJob.Values.Sum(x => x.SuccessfulPositionals);

    public long GetSuccessfulPositionals(uint jobId) =>
        ByJob.TryGetValue(jobId, out var stats) ? stats.SuccessfulPositionals : 0;

    public long RecordSuccess(uint jobId)
    {
        if (!ByJob.TryGetValue(jobId, out var stats))
        {
            stats = new PositionalClassStats();
            ByJob[jobId] = stats;
        }

        stats.SuccessfulPositionals++;
        return stats.SuccessfulPositionals;
    }

    public void ClearJob(uint jobId) => ByJob.Remove(jobId);

    public void Clear() => ByJob.Clear();
}
