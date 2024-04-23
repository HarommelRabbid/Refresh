using System.Diagnostics;
using Bunkum.Core.Storage;
using NotEnoughLogs;
using Refresh.GameServer.Database;
using Refresh.GameServer.Types.Levels;
using Refresh.GameServer.Types.Reviews;
using Refresh.GameServer.Types.Roles;

namespace Refresh.GameServer.Workers;

public class CoolLevelsWorker : IWorker
{
    public int WorkInterval => 600_000; // Every 10 minutes
    public bool DoWork(Logger logger, IDataStore dataStore, GameDatabaseContext database)
    {
        DatabaseList<GameLevel> levels = database.GetAllUserLevels();
        if (levels.TotalItems <= 0)
        {
            logger.LogWarning(RefreshContext.CoolLevels, "No levels to process for cool levels. If you're sure this server has levels, this is a bug.");
            return false;
        }

        long now = DateTimeOffset.Now.ToUnixTimeSeconds();
        // Create a dictionary so we can batch the write to the db
        Dictionary<GameLevel, float> scoresToSet = new(levels.TotalItems);

        Stopwatch stopwatch = new();
        stopwatch.Start();

        foreach (GameLevel level in levels.Items)
        {
            logger.LogTrace(RefreshContext.CoolLevels, "Calculating score for '{0}' ({1})", level.Title, level.LevelId);
            float decayMultiplier = CalculateLevelDecayMultiplier(logger, now, level);

            // Calculate positive & negative score separately, so we don't run into issues with
            // the multiplier having an opposite effect with the negative score as time passes
            int positiveScore = CalculatePositiveScore(logger, level, database);
            int negativeScore = CalculateNegativeScore(logger, level, database);

            // Increase to tweak how little negative score gets affected by decay
            const int negativeScoreMultiplier = 2;
            
            // Weigh everything with the multiplier and set a final score
            float finalScore = (positiveScore * decayMultiplier) - (negativeScore * Math.Min(1.0f, decayMultiplier * negativeScoreMultiplier));
            
            logger.LogTrace(RefreshContext.CoolLevels, "Score for '{0}' ({1}) is {2}", level.Title, level.LevelId, finalScore);
            scoresToSet.Add(level, finalScore);
        }
        
        stopwatch.Stop();
        logger.LogInfo(RefreshContext.CoolLevels, "Calculated scores for {0} levels in {1}ms", levels.TotalItems, stopwatch.ElapsedMilliseconds);
        
        // Commit scores to database. This method lets us use a dictionary so we can batch everything in one write
        database.SetLevelScores(scoresToSet);
        
        return true; // Tell the worker manager we did work
    }

    private static float CalculateLevelDecayMultiplier(Logger logger, long now, GameLevel level)
    {
        const int decayMonths = 3;
        const int decaySeconds = decayMonths * 30 * 24 * 3600;
        const float minimumMultiplier = 0.1f;
        
        // Use seconds. Lets us not worry about float stuff
        long publishDate = level.PublishDate / 1000;
        long elapsed = now - publishDate;

        // Get a scale from 0.0f to 1.0f, the percent of decay
        float multiplier = 1.0f - Math.Min(1.0f, (float)elapsed / decaySeconds);
        multiplier = Math.Max(minimumMultiplier, multiplier); // Clamp to minimum multiplier
        
        logger.LogTrace(RefreshContext.CoolLevels, "Decay multiplier is {0}", multiplier);
        return multiplier;
    }

    private static int CalculatePositiveScore(Logger logger, GameLevel level, GameDatabaseContext database)
    {
        int score = 15; // Start levels off with a few points to prevent one dislike from bombing the level
        const int positiveRatingPoints = 5;
        const int uniquePlayPoints = 1;
        const int heartPoints = 5;
        const int trustedAuthorPoints = 5;

        if (level.TeamPicked)
            score += 10;
        
        score += database.GetTotalRatingsForLevel(level, RatingType.Yay) * positiveRatingPoints;
        score += database.GetUniquePlaysForLevel(level) * uniquePlayPoints;
        score += database.GetFavouriteCountForLevel(level) * heartPoints;

        if (level.Publisher?.Role == GameUserRole.Trusted)
            score += trustedAuthorPoints;

        logger.LogTrace(RefreshContext.CoolLevels, "positiveScore is {0}", score);
        return score;
    }

    private static int CalculateNegativeScore(Logger logger, GameLevel level, GameDatabaseContext database)
    {
        int penalty = 0;
        const int negativeRatingPenalty = 5;
        const int noAuthorPenalty = 10;
        const int restrictedAuthorPenalty = 50;
        const int bannedAuthorPenalty = 100;
        
        penalty += database.GetTotalRatingsForLevel(level, RatingType.Boo) * negativeRatingPenalty;
        
        if (level.Publisher == null)
            penalty += noAuthorPenalty;
        else if (level.Publisher?.Role == GameUserRole.Restricted)
            penalty += restrictedAuthorPenalty;
        else if (level.Publisher?.Role == GameUserRole.Banned)
            penalty += bannedAuthorPenalty;

        logger.LogTrace(RefreshContext.CoolLevels, "negativeScore is {0}", penalty);
        return penalty;
    }
}