using Bunkum.Core.Storage;
using JetBrains.Annotations;
using Refresh.GameServer.Authentication;
using Refresh.GameServer.Database;
using Refresh.GameServer.Types.UserData;

namespace Refresh.GameServer.Endpoints.ApiV3.DataTypes.Response;

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public class ApiGameUserResponse : IApiResponse, IDataConvertableFrom<ApiGameUserResponse, GameUser>
{
    // HEY! When adding fields here, remember to propagate them in ApiExtendedGameUser too!
    // Otherwise, they won't show up in the admin panel endpoints or /users/me. Thank you!
    
    public required string UserId { get; set; }
    public required string Username { get; set; }
    public required string IconHash { get; set; }
    public required string Description { get; set; }
    public required ApiGameLocationResponse Location { get; set; }
    public required DateTimeOffset JoinDate { get; set; }
    public required DateTimeOffset LastLoginDate { get; set; }

    [ContractAnnotation("null => null; notnull => notnull")]
    public static ApiGameUserResponse? FromOld(GameUser? user)
    {
        if (user == null) return null;
        
        return new ApiGameUserResponse
        {
            UserId = user.UserId.ToString()!,
            Username = user.Username,
            IconHash = user.IconHash,
            Description = user.Description,
            Location = ApiGameLocationResponse.FromGameLocation(user.Location)!,
            JoinDate = user.JoinDate,
            LastLoginDate = user.LastLoginDate,
        };
    }

    public void FillInExtraData(GameDatabaseContext database, IDataStore dataStore)
    {
        this.IconHash = database.GetAssetFromHash(this.IconHash)?.GetAsIcon(TokenGame.Website, database, dataStore) ?? this.IconHash;
    }
    
    public static ApiGameUserResponse? FromOldWithExtraData(GameUser? old, GameDatabaseContext database, IDataStore dataStore)
    {
        if (old == null) return null;

        ApiGameUserResponse response = FromOld(old)!;
        response.FillInExtraData(database, dataStore);

        return response;
    }

    public static IEnumerable<ApiGameUserResponse> FromOldList(IEnumerable<GameUser> oldList) => oldList.Select(FromOld).ToList()!;
}