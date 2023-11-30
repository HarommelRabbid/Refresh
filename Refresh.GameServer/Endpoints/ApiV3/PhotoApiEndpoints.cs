using AttribDoc.Attributes;
using Bunkum.Core;
using Bunkum.Core.Endpoints;
using Bunkum.Listener.Protocol;
using Bunkum.Protocols.Http;
using Refresh.GameServer.Database;
using Refresh.GameServer.Documentation.Attributes;
using Refresh.GameServer.Endpoints.ApiV3.ApiTypes;
using Refresh.GameServer.Endpoints.ApiV3.ApiTypes.Errors;
using Refresh.GameServer.Endpoints.ApiV3.DataTypes.Response;
using Refresh.GameServer.Extensions;
using Refresh.GameServer.Types.Levels;
using Refresh.GameServer.Types.Photos;
using Refresh.GameServer.Types.UserData;

namespace Refresh.GameServer.Endpoints.ApiV3;

public class PhotoApiEndpoints : EndpointGroup
{
    [ApiV3Endpoint("photos/id/{id}", HttpMethods.Delete)]
    [DocSummary("Deletes an uploaded photo")]
    [DocError(typeof(ApiNotFoundError), ApiNotFoundError.PhotoMissingErrorWhen)]
    [DocError(typeof(ApiValidationError), ApiValidationError.NoPhotoDeletionPermissionErrorWhen)]
    public ApiResponse<ApiEmptyResponse> DeletePhoto(RequestContext context, IGameDatabaseContext database, GameUser user, int id)
    {
        GamePhoto? photo = database.GetPhotoById(id);
        if (photo == null) return ApiNotFoundError.PhotoMissingError;

        if (photo.Publisher.UserId != user.UserId)
            return ApiValidationError.NoPhotoDeletionPermissionError;

        database.RemovePhoto(photo);
        return new ApiOkResponse();
    }
    
    private static ApiListResponse<ApiGamePhotoResponse> PhotosByUser(RequestContext context, IGameDatabaseContext database, GameUser? user)
    {
        if (user == null) return ApiNotFoundError.Instance;
        (int skip, int count) = context.GetPageData(true);

        DatabaseList<GamePhoto> photos = database.GetPhotosByUser(user, count, skip);
        return DatabaseList<ApiGamePhotoResponse>.FromOldList<ApiGamePhotoResponse, GamePhoto>(photos);
    }
    
    private static ApiListResponse<ApiGamePhotoResponse> PhotosWithUser(RequestContext context, IGameDatabaseContext database, GameUser? user)
    {
        if (user == null) return ApiNotFoundError.Instance;
        (int skip, int count) = context.GetPageData(true);

        DatabaseList<GamePhoto> photos = database.GetPhotosWithUser(user, count, skip);
        return DatabaseList<ApiGamePhotoResponse>.FromOldList<ApiGamePhotoResponse, GamePhoto>(photos);
    }
    
    private static ApiListResponse<ApiGamePhotoResponse> PhotosInLevel(RequestContext context, IGameDatabaseContext database, GameLevel? level)
    {
        if (level == null) return ApiNotFoundError.Instance;
        (int skip, int count) = context.GetPageData(true);

        DatabaseList<GamePhoto> photos = database.GetPhotosInLevel(level, count, skip);
        return DatabaseList<ApiGamePhotoResponse>.FromOldList<ApiGamePhotoResponse, GamePhoto>(photos);
    }
    
    [ApiV3Endpoint("photos/by/username/{username}"), Authentication(false)]
    [DocUsesPageData, DocSummary("Gets photos by a user by their username")]
    [DocError(typeof(ApiNotFoundError), "The user cannot be found")]
    public ApiListResponse<ApiGamePhotoResponse> PhotosByUsername(RequestContext context, IGameDatabaseContext database,
        [DocSummary("The username of the user")] string username) 
        => PhotosByUser(context, database, database.GetUserByUsername(username));
    
    [ApiV3Endpoint("photos/with/username/{username}"), Authentication(false)]
    [DocUsesPageData, DocSummary("Gets photos including a user by their username")]
    [DocError(typeof(ApiNotFoundError), "The user cannot be found")]
    public ApiListResponse<ApiGamePhotoResponse> PhotosWithUsername(RequestContext context, IGameDatabaseContext database,
        [DocSummary("The username of the user")] string username)
        => PhotosWithUser(context, database, database.GetUserByUsername(username));

    [ApiV3Endpoint("photos/by/uuid/{uuid}"), Authentication(false)]
    [DocUsesPageData, DocSummary("Gets photos by a user by their uuid")]
    [DocError(typeof(ApiNotFoundError), "The user cannot be found")]
    public ApiListResponse<ApiGamePhotoResponse> PhotosByUserUuid(RequestContext context, IGameDatabaseContext database,
        [DocSummary("The UUID of the user")] string uuid)
        => PhotosByUser(context, database, database.GetUserByUuid(uuid));

    [ApiV3Endpoint("photos/with/uuid/{uuid}"), Authentication(false)]
    [DocUsesPageData, DocSummary("Gets photos including a user by their uuid")]
    [DocError(typeof(ApiNotFoundError), "The user cannot be found")]
    public ApiListResponse<ApiGamePhotoResponse> PhotosWithUserUuid(RequestContext context, IGameDatabaseContext database,
        [DocSummary("The UUID of the user")] string uuid)
        => PhotosWithUser(context, database, database.GetUserByUuid(uuid));

    [ApiV3Endpoint("levels/id/{id}/photos"), Authentication(false)]
    [DocUsesPageData, DocSummary("Gets photos taken in a level by its id")]
    [DocError(typeof(ApiNotFoundError), "The level cannot be found")]
    public ApiListResponse<ApiGamePhotoResponse> PhotosInLevelById(RequestContext context, IGameDatabaseContext database,
        [DocSummary("The ID of the level")] int id)
        => PhotosInLevel(context, database, database.GetLevelById(id));
    
    [ApiV3Endpoint("photos"), Authentication(false)]
    [DocUsesPageData, DocSummary("Get all photos taken recently")]
    public ApiListResponse<ApiGamePhotoResponse> RecentPhotos(RequestContext context, IGameDatabaseContext database)
    {
        (int skip, int count) = context.GetPageData(true);
        DatabaseList<GamePhoto> photos = database.GetRecentPhotos(count, skip);
        
        return DatabaseList<ApiGamePhotoResponse>.FromOldList<ApiGamePhotoResponse, GamePhoto>(photos);
    }
    
    [ApiV3Endpoint("photos/id/{id}"), Authentication(false)]
    [DocUsesPageData, DocSummary("Get an individual photo by the photo's id")]
    [DocError(typeof(ApiNotFoundError), "The photo cannot be found")]
    public ApiResponse<ApiGamePhotoResponse> PhotoById(RequestContext context, IGameDatabaseContext database, [DocSummary("The ID of the photo")] int id)
    {
        GamePhoto? photo = database.GetPhotoById(id);
        if (photo == null) return ApiNotFoundError.Instance;
        
        return ApiGamePhotoResponse.FromOld(photo);
    }
}