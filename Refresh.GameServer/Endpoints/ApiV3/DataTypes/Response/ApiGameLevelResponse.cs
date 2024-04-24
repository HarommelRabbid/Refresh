using Bunkum.Core.Storage;
using Refresh.GameServer.Authentication;
using Refresh.GameServer.Database;
using Refresh.GameServer.Types.Levels;
using Refresh.GameServer.Types.Reviews;

namespace Refresh.GameServer.Endpoints.ApiV3.DataTypes.Response;

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class ApiGameLevelResponse : IApiResponse
{
    public required int LevelId { get; set; }
    public required ApiGameUserResponse? Publisher { get; set; }
    public required bool IsReUpload { get; set; }
    public required string? OriginalPublisher { get; set; }

    public required string Title { get; set; }
    public required string IconHash { get; set; }
    private string? _originalIconHash;
    public required string Description { get; set; }
    public required ApiGameLocationResponse Location { get; set; }
    
    public required string RootLevelHash { get; set; }
    public TokenGame GameVersion { get; set; }
    
    public required DateTimeOffset PublishDate { get; set; }
    public required DateTimeOffset UpdateDate { get; set; }
    
    public required int MinPlayers { get; set; }
    public required int MaxPlayers { get; set; }
    public required bool EnforceMinMaxPlayers { get; set; }
    
    public required bool SameScreenGame { get; set; }
    
    public required IEnumerable<ApiGameSkillRewardResponse>? SkillRewards { get; set; }
    
    public int YayRatings { get; set; }
    public int BooRatings { get; set; }
    public int Hearts { get; set; }
    public int UniquePlays { get; set; }
    public required bool TeamPicked { get; set; }
    public required GameLevelType LevelType { get; set; }
    public required bool IsLocked { get; set; }
    public required bool IsSubLevel { get; set; }
    public required bool IsCopyable { get; set; }
    public required float Score { get; set; }

    public static ApiGameLevelResponse? FromOld(GameLevel? level)
    {
        if (level == null) return null;
        
        return new ApiGameLevelResponse
        {
            Title = level.Title,
            Publisher = ApiGameUserResponse.FromOld(level.Publisher),
            OriginalPublisher = level.OriginalPublisher,
            IsReUpload = level.IsReUpload,
            LevelId = level.LevelId,
            IconHash = level.GameVersion == TokenGame.LittleBigPlanetPSP
                ? "psp/" + level.IconHash
                : level.IconHash,
            _originalIconHash = level.IconHash,
            Description = level.Description,
            Location = ApiGameLocationResponse.FromGameLocation(level.Location)!,
            PublishDate = DateTimeOffset.FromUnixTimeMilliseconds(level.PublishDate),
            UpdateDate = DateTimeOffset.FromUnixTimeMilliseconds(level.UpdateDate),
            MinPlayers = level.MinPlayers,
            MaxPlayers = level.MaxPlayers,
            EnforceMinMaxPlayers = level.EnforceMinMaxPlayers,
            SameScreenGame = level.SameScreenGame,
            SkillRewards = ApiGameSkillRewardResponse.FromOldList(level.SkillRewards),
            TeamPicked = level.TeamPicked,
            RootLevelHash = level.RootResource,
            GameVersion = level.GameVersion,
            LevelType = level.LevelType,
            IsCopyable = level.IsCopyable,
            IsLocked = level.IsLocked,
            IsSubLevel = level.IsSubLevel,
            Score = level.Score,
        };
    }
    
    public void FillInExtraData(GameDatabaseContext database, IDataStore dataStore)
    {
        this.Publisher?.FillInExtraData(database, dataStore);

        //Get the icon form of the icon asset
        this.IconHash = database.GetAssetFromHash(this._originalIconHash ?? "0")?.GetAsIcon(TokenGame.Website, database, dataStore) ?? this.IconHash;
        
        // TODO: no
        GameLevel? level = database.GetLevelById(this.LevelId);
        if (level == null) return;
        
        this.YayRatings = database.GetTotalRatingsForLevel(level, RatingType.Yay);
        this.BooRatings = database.GetTotalRatingsForLevel(level, RatingType.Boo);
        this.Hearts = database.GetFavouriteCountForLevel(level);
        this.UniquePlays = database.GetUniquePlaysForLevel(level);
    }
    
    public static ApiGameLevelResponse? FromOldWithExtraData(GameLevel? old, GameDatabaseContext database, IDataStore dataStore)
    {
        if (old == null) return null;

        ApiGameLevelResponse response = FromOld(old)!;
        response.FillInExtraData(database, dataStore);

        return response;
    }

    public static IEnumerable<ApiGameLevelResponse> FromOldList(IEnumerable<GameLevel> oldList) => oldList.Select(FromOld).ToList()!;
}