namespace StealDeal.Services.Identity.Domain.Models;

// Result of an atomic update, not a persisted entity or a new database table.
public sealed class TrustScoreUpdateResult
{
    public int ScoreBefore { get; set; }
    public int ScoreAfter { get; set; }
}
