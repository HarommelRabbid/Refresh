using AttribDoc.Attributes;
using Bunkum.Core;
using Bunkum.Core.Endpoints;
using Bunkum.Listener.Protocol;
using Bunkum.Protocols.Http;
using MongoDB.Bson;
using Refresh.GameServer.Database;
using Refresh.GameServer.Endpoints.ApiV3.ApiTypes;
using Refresh.GameServer.Endpoints.ApiV3.ApiTypes.Errors;
using Refresh.GameServer.Endpoints.ApiV3.DataTypes.Response.Admin;
using Refresh.GameServer.Types.Roles;
using Refresh.GameServer.Types.UserData;

namespace Refresh.GameServer.Endpoints.ApiV3.Admin;

public class AdminRegistrationApiEndpoints : EndpointGroup
{
    [ApiV3Endpoint("admin/registrations"), MinimumRole(GameUserRole.Admin)]
    [DocSummary("Retrieves all queued registrations on the server.")]
    public ApiListResponse<ApiAdminQueuedRegistrationResponse> GetAllQueuedRegistrations(RequestContext context, IGameDatabaseContext database) 
        => new(ApiAdminQueuedRegistrationResponse.FromOldList(database.GetAllQueuedRegistrations().Items));

    [ApiV3Endpoint("admin/registrations/{uuid}"), MinimumRole(GameUserRole.Admin)]
    [DocSummary("Retrieves a single registration by its UUID.")]
    [DocError(typeof(ApiValidationError), ApiValidationError.ObjectIdParseErrorWhen)]
    [DocError(typeof(ApiNotFoundError), "The registration could not be found")]
    public ApiResponse<ApiAdminQueuedRegistrationResponse> GetQueuedRegistrationByUuid(RequestContext context,
        IGameDatabaseContext database, string uuid)
    {
        bool parsed = ObjectId.TryParse(uuid, out ObjectId id);
        if (!parsed) return ApiValidationError.ObjectIdParseError;

        QueuedRegistration? registration = database.GetQueuedRegistrationByObjectId(id);
        if (registration == null) return ApiNotFoundError.Instance;
        
        return ApiAdminQueuedRegistrationResponse.FromOld(registration);
    }

    [ApiV3Endpoint("admin/registrations/{uuid}", HttpMethods.Delete), MinimumRole(GameUserRole.Admin)]
    [DocSummary("Deletes a registration by its UUID.")]
    [DocError(typeof(ApiValidationError), ApiValidationError.ObjectIdParseErrorWhen)]
    [DocError(typeof(ApiNotFoundError), "The registration could not be found")]
    public ApiOkResponse DeleteQueuedRegistrationByUuid(RequestContext context, IGameDatabaseContext database, string uuid)
    {
        bool parsed = ObjectId.TryParse(uuid, out ObjectId id);
        if (!parsed) return ApiValidationError.ObjectIdParseError;

        QueuedRegistration? registration = database.GetQueuedRegistrationByObjectId(id);
        if (registration == null) return ApiNotFoundError.Instance;
        
        database.RemoveRegistrationFromQueue(registration);
        return new ApiOkResponse();
    }
    
    [ApiV3Endpoint("admin/registrations", HttpMethods.Delete), MinimumRole(GameUserRole.Admin)]
    [DocSummary("Clears all queued registrations from the server.")]
    public ApiOkResponse DeleteAllQueuedRegistrations(RequestContext context, IGameDatabaseContext database)
    {
        database.RemoveAllRegistrationsFromQueue();
        return new ApiOkResponse();
    }
}