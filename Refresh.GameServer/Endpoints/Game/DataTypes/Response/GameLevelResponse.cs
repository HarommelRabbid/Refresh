using System.Xml.Serialization;
using Bunkum.Core.Storage;
using Refresh.GameServer.Authentication;
using Refresh.GameServer.Database;
using Refresh.GameServer.Endpoints.ApiV3.DataTypes;
using Refresh.GameServer.Extensions;
using Refresh.GameServer.Services;
using Refresh.GameServer.Types;
using Refresh.GameServer.Types.Assets;
using Refresh.GameServer.Types.Levels;
using Refresh.GameServer.Types.Levels.SkillRewards;
using Refresh.GameServer.Types.Matching;
using Refresh.GameServer.Types.Relations;
using Refresh.GameServer.Types.Reviews;
using Refresh.GameServer.Types.UserData;

namespace Refresh.GameServer.Endpoints.Game.DataTypes.Response;

[XmlRoot("slot")]
[XmlType("slot")]
public class GameLevelResponse : IDataConvertableFrom<GameLevelResponse, GameLevel>
{
    [XmlElement("id")] public required int LevelId { get; set; }

    [XmlElement("name")] public required string Title { get; set; }
    [XmlElement("icon")] public required string IconHash { get; set; }
    [XmlElement("description")] public required string Description { get; set; }
    [XmlElement("location")] public required GameLocation Location { get; set; }

    [XmlElement("game")] public required int GameVersion { get; set; }
    [XmlElement("rootLevel")] public required string RootResource { get; set; }

    [XmlElement("firstPublished")] public required long PublishDate { get; set; } // unix seconds
    [XmlElement("lastUpdated")] public required long UpdateDate { get; set; }
    
    [XmlElement("minPlayers")] public required int MinPlayers { get; set; }
    [XmlElement("maxPlayers")] public required int MaxPlayers { get; set; }
    [XmlElement("enforceMinMaxPlayers")] public required bool EnforceMinMaxPlayers { get; set; }
    
    [XmlElement("sameScreenGame")] public required bool SameScreenGame { get; set; }

    [XmlAttribute("type")] public string Type { get; set; } = "user";

    [XmlElement("npHandle")] public SerializedUserHandle Handle { get; set; } = null!;
    
    [XmlElement("heartCount")] public int HeartCount { get; set; }
    
    [XmlElement("playCount")] public int TotalPlayCount { get; set; }
    [XmlElement("uniquePlayCount")] public int UniquePlayCount { get; set; }

    [XmlElement("yourDPadRating")] public int YourRating { get; set; }
    [XmlElement("thumbsup")] public int YayCount { get; set; }
    [XmlElement("thumbsdown")] public int BooCount { get; set; }
    [XmlElement("yourRating")] public int YourStarRating { get; set; }
    
    // 1 by default since this will break reviews if set to 0 for GameLevelResponses that do not have extra data being filled in
    [XmlElement("yourlbp2PlayCount")] public int YourLbp2PlayCount { get; set; } = 1;
    
    [XmlArray("customRewards")]
    [XmlArrayItem("customReward")]
    public required List<GameSkillReward> SkillRewards { get; set; }

    [XmlElement("mmpick")] public required bool TeamPicked { get; set; }
    [XmlElement("resource")] public List<string> XmlResources { get; set; } = new();
    [XmlElement("playerCount")] public int PlayerCount { get; set; }
    
    [XmlElement("leveltype")] public required string LevelType { get; set; }
    
    [XmlElement("initiallyLocked")] public bool IsLocked { get; set; }
    [XmlElement("isSubLevel")] public bool IsSubLevel { get; set; }
    [XmlElement("shareable")] public int IsCopyable { get; set; }
    [XmlElement("backgroundGUID")] public string? BackgroundGuid { get; set; }
    [XmlElement("links")] public string? Links { get; set; }
    [XmlElement("averageRating")] public double AverageStarRating { get; set; }
    [XmlElement("sizeOfResources")] public int SizeOfResourcesInBytes { get; set; }
    [XmlElement("reviewCount")] public int ReviewCount { get; set; }
    [XmlElement("reviewsEnabled")] public bool ReviewsEnabled { get; set; } = true;
    [XmlElement("commentCount")] public int CommentCount { get; set; } = 0;
    [XmlElement("commentsEnabled")] public bool CommentsEnabled { get; set; } = true;

    public static GameLevelResponse? FromOldWithExtraData(GameLevel? old, GameDatabaseContext database, MatchService matchService, GameUser user, IDataStore dataStore, TokenGame game)
    {
        if (old == null) return null;

        GameLevelResponse response = FromOld(old)!;
        response.FillInExtraData(database, matchService, user, dataStore, game);

        return response;
    }

    public static GameLevelResponse? FromOld(GameLevel? old)
    {
        if (old == null) return null;
        
        GameLevelResponse response = new()
        {
            LevelId = old.LevelId,
            Title = old.Title,
            IconHash = old.IconHash,
            Description = old.Description,
            Location = old.Location,
            GameVersion = old.GameVersion.ToSerializedGame(),
            RootResource = old.RootResource,
            PublishDate = old.PublishDate,
            UpdateDate = old.UpdateDate,
            MinPlayers = old.MinPlayers,
            MaxPlayers = old.MaxPlayers,
            EnforceMinMaxPlayers = old.EnforceMinMaxPlayers,
            SameScreenGame = old.SameScreenGame,
            SkillRewards = old.SkillRewards.ToList(),
            TeamPicked = old.TeamPicked,
            LevelType = old.LevelType.ToGameString(),
            IsCopyable = old.IsCopyable ? 1 : 0,
            IsLocked = old.IsLocked,
            IsSubLevel = old.IsSubLevel,
            BackgroundGuid = old.BackgroundGuid,
            Links = "",
            ReviewCount = old.Reviews.Count,
            CommentCount = old.LevelComments.Count,
        };

        response.Type = "user";
        if (old is { Publisher: not null, IsReUpload: false })
        {
            response.Handle = SerializedUserHandle.FromUser(old.Publisher);
        }
        else
        {
            response.Handle = new SerializedUserHandle
            {
                IconHash = "0",
                Username = string.IsNullOrEmpty(old.OriginalPublisher) ? "!DeletedUser" : "!" + old.OriginalPublisher,
            };
        }
        
        return response;
    }

    public static IEnumerable<GameLevelResponse> FromOldList(IEnumerable<GameLevel> oldList) => oldList.Select(FromOld).ToList()!;

    private void FillInExtraData(GameDatabaseContext database, MatchService matchService, GameUser user, IDataStore dataStore, TokenGame game)
    {
        GameLevel? level = database.GetLevelById(this.LevelId);
        if (level == null) throw new InvalidOperationException("Cannot fill in level data for a level that does not exist.");

        RatingType? rating = database.GetRatingByUser(level, user);
        
        this.YourRating = rating?.ToDPad() ?? (int)RatingType.Neutral;
        this.YourStarRating = rating?.ToLBP1() ?? 0;
        this.YourLbp2PlayCount = database.GetTotalPlaysForLevelByUser(level, user);
        this.PlayerCount = matchService.GetPlayerCountForLevel(RoomSlotType.Online, this.LevelId);

        GameAsset? rootResourceAsset = database.GetAssetFromHash(this.RootResource);
        if (rootResourceAsset != null)
        {
            rootResourceAsset.TraverseDependenciesRecursively(database, (_, asset) =>
            {
                if (asset != null)
                    this.SizeOfResourcesInBytes += asset.SizeInBytes;
            });
        }

        this.IconHash = database.GetAssetFromHash(this.IconHash)?.GetAsIcon(game, database, dataStore) ?? this.IconHash;

        this.CommentCount = level.LevelComments.Count;
        
        this.HeartCount = database.GetFavouriteCountForLevel(level);
        this.TotalPlayCount = database.GetTotalPlaysForLevel(level);
        this.UniquePlayCount = database.GetUniquePlaysForLevel(level);
        this.YayCount = database.GetTotalRatingsForLevel(level, RatingType.Yay);
        this.BooCount = database.GetTotalRatingsForLevel(level, RatingType.Boo);
        
        this.AverageStarRating = level.CalculateAverageStarRating(database);
    }
}