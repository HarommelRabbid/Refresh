using Bunkum.Core;
using Refresh.GameServer.Authentication;
using Refresh.GameServer.Database;
using Refresh.GameServer.Endpoints.Game.Levels.FilterSettings;
using Refresh.GameServer.Services;
using Refresh.GameServer.Types.UserData;

namespace Refresh.GameServer.Types.Levels.Categories;

public class CurrentlyPlayingCategory : LevelCategory
{
    internal CurrentlyPlayingCategory() : base("currentlyPlaying", "busiest", false)
    {
        this.Name = "Popular Now";
        this.Description = "Levels people are playing right now!";
        this.IconHash = "g820602";
        this.FontAwesomeIcon = "users";
    }

    public override DatabaseList<GameLevel>? Fetch(RequestContext context, int skip, int count,
        MatchService matchService, IGameDatabaseContext database, GameUser? user,
        LevelFilterSettings levelFilterSettings) 
        => database.GetBusiestLevels(count, skip, matchService, user, levelFilterSettings);
}