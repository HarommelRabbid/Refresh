using Bunkum.Core;
using Bunkum.Core.Responses;
using Bunkum.Listener.Protocol;
using MongoDB.Bson;
using NotEnoughLogs;
using Refresh.GameServer.Authentication;
using Refresh.GameServer.Database;
using Refresh.GameServer.Services;
using Refresh.GameServer.Types.Matching.Responses;
using Refresh.GameServer.Types.UserData;

namespace Refresh.GameServer.Types.Matching.MatchMethods;

public class FindRoomMethod : IMatchMethod
{
    public IEnumerable<string> MethodNames => new[] { "FindBestRoom" };

    public Response Execute(MatchService service, Logger logger, GameDatabaseContext database,
        GameUser user,
        Token token,
        SerializedRoomData body)
    {
        GameRoom? usersRoom = service.RoomAccessor.GetRoomByUser(user, token.TokenPlatform, token.TokenGame);
        if (usersRoom == null) return BadRequest; // user should already have a room.
        
        List<int> levelIds = new(body.Slots.Count);
        // Iterate over all sent slots and append their IDs to the list of level IDs to check
        foreach(List<int> slot in body.Slots)
        {
            if (slot.Count != 2)
            {
                logger.LogWarning(BunkumCategory.Matching, "Received request with invalid slot, rejecting.");
                return BadRequest;
            }
            
            // 0 means "no level specified"
            if (slot[1] == 0) 
                continue;
            
            levelIds.Add(slot[1]);
        }
        
        // If we are on vita and the game specified more than one level ID, then its trying to do dive in only to players in a certain category of levels
        // This is not how most people expect dive in to work, so let's pretend that the game didn't specify any level IDs whatsoever, so they will get matched with all players
        if (token.TokenGame == TokenGame.LittleBigPlanetVita && levelIds.Count > 1)
            levelIds = [];
        
        //TODO: Add user option to filter rooms by language
        
        IEnumerable<GameRoom> rooms = service.RoomAccessor
            // Get all the available rooms 
            .GetRoomsByGameAndPlatform(token.TokenGame, token.TokenPlatform)
            .Where(r =>
                // Make sure we don't match the user into their own room
                r.RoomId != usersRoom.RoomId &&
                // If the level id isn't specified, or is 0, then we don't want to try to match against level IDs, else only match the user to people who are playing that level
                (levelIds.Count == 0 || levelIds.Contains(r.LevelId)) &&
                // Make sure that we don't try to match the player into a full room, or a room which won't fit the user's current room
                usersRoom.PlayerIds.Count + r.PlayerIds.Count <= 4 &&
                // Match the build version of the rooms
                (r.BuildVersion ?? 0) == body.BuildVersion)
            // Shuffle the rooms around before sorting, this is because the selection is based on a weighted average towards the top of the range,
            // so there would be a bias towards longer lasting rooms without this shuffle
            .OrderBy(r => Random.Shared.Next())
            // Order by descending room mood, so that rooms with higher mood (e.g. allowing more people) get selected more often
            // This is a stable sort, which is why the order needs to be shuffled above
            .ThenByDescending(r => r.RoomMood)
            // Order by "passed no join point" so that people are more likely to be matched into rooms which they can instantly hop into and play
            .ThenByDescending(r => r.PassedNoJoinPoint ? 0 : 1);
        
        //When a user is behind a Strict NAT layer, we can only connect them to players with Open NAT types
        if (body.NatType != null && body.NatType[0] == NatType.Strict)
        {
            rooms = rooms.Where(r => r.NatType == NatType.Open);
        }

        ObjectId? forceMatch = user.ForceMatch;

        //If the user has a forced match
        if (forceMatch != null)
        {
            //Filter the rooms to only the rooms that contain the player we are wanting to force match to
            rooms = rooms.Where(r => r.PlayerIds.Any(player => player.Id != null && player.Id == forceMatch.Value));
        }
        
        // Now that we've done all our filtering, lets convert it to a list, so we can index it quickly.
        List<GameRoom> roomList = rooms.ToList();
        
        if (roomList.Count <= 0)
        {
#if DEBUG
            logger.LogDebug(BunkumCategory.Matching, "Room search by {0} on {1} ({2}) returned no results, dumping list of available rooms.", user.Username, token.TokenGame, token.TokenPlatform);
            
            // Dump an "overview" of the global room state, to help debug matching issues
            rooms = service.RoomAccessor.GetRoomsByGameAndPlatform(token.TokenGame, token.TokenPlatform);
            foreach (GameRoom logRoom in rooms)
                logger.LogDebug(BunkumCategory.Matching,"Room {0} has NAT type {1} and is on level {2}", logRoom.RoomId, logRoom.NatType, logRoom.LevelId);
#endif
            
            // Return a 404 status code if there's no rooms to match them to
            return new Response(new List<object> { new SerializedStatusCodeMatchResponse(404), }, ContentType.Json);
        }

        // If the user has a forced match and we found a room
        if (forceMatch != null)
        {
            // Clear the user's force match
            database.ClearForceMatch(user);
        }
        
        // Generate a weighted random number, this is weighted relatively strongly towards lower numbers,
        // which makes it more likely to pick rooms with a higher mood and not past a "no join point",
        // since those are sorted near the start of the list
        // Curve: https://www.desmos.com/calculator/aagcmlbb08
        double weightedRandom = 1 - Math.Cbrt(1 - Random.Shared.NextDouble());
        
        // Even though NextDouble guarantees the result to be < 1.0, and this mathematically always will check out,
        // rounding errors may cause this to become roomList.Count (which would crash), so we use a Math.Min to make sure it doesn't
        GameRoom room = roomList[Math.Min(roomList.Count - 1, (int)Math.Floor(weightedRandom * roomList.Count))];
        
        logger.LogInfo(BunkumCategory.Matching, "Matched user {0} into {1}'s room (id: {2})", user.Username, room.HostId.Username, room.RoomId);

        SerializedRoomMatchResponse roomMatch = new()
        {
            HostMood = (byte)room.RoomMood,
            RoomState = (byte)room.RoomState,
            Players = [],
            Slots =
            [
                [
                    (byte)room.LevelType,
                    room.LevelId,
                ],
            ],
        };
        
        foreach (GameUser? roomUser in room.GetPlayers(database))
        {
            if(roomUser == null) continue;
            roomMatch.Players.Add(new SerializedRoomPlayer(roomUser.Username, 0));
        }
        
        foreach (GameUser? roomUser in usersRoom.GetPlayers(database))
        {
            if(roomUser == null) continue;
            roomMatch.Players.Add(new SerializedRoomPlayer(roomUser.Username, 1));
        }

        SerializedStatusCodeMatchResponse status = new(200);

        List<object> response =
        [
            status,
            roomMatch,
        ];

        return new Response(response, ContentType.Json);
    }
}