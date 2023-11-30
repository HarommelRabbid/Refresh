using System.Reflection;
using JetBrains.Annotations;
using MongoDB.Bson;
using Refresh.GameServer.Authentication;
using Refresh.GameServer.Endpoints.ApiV3.DataTypes.Request;
using Refresh.GameServer.Types;
using Refresh.GameServer.Types.Levels;
using Refresh.GameServer.Types.Photos;
using Refresh.GameServer.Types.Roles;
using Refresh.GameServer.Types.UserData;

namespace Refresh.GameServer.Database;

public partial interface IGameDatabaseContext // Users
{
    private static readonly GameUser DeletedUser = new()
    {
        Location = GameLocation.Zero,
        Username = "!DeletedUser",
        Description = "I'm a fake user that represents deleted users for levels.",
    };
    
    [Pure]
    [ContractAnnotation("null => null; notnull => canbenull")]
    public GameUser? GetUserByUsername(string? username)
    {
        if (username == null) return null;
        if (username == "!DeletedUser") return DeletedUser;

        return this.All<GameUser>().FirstOrDefault(u => u.Username == username);
    }
    
    [Pure]
    [ContractAnnotation("null => null; notnull => canbenull")]
    public GameUser? GetUserByEmailAddress(string? emailAddress)
    {
        if (emailAddress == null) return null;
        emailAddress = emailAddress.ToLowerInvariant();
        return this.All<GameUser>().FirstOrDefault(u => u.EmailAddress == emailAddress);
    }

    [Pure]
    [ContractAnnotation("null => null; notnull => canbenull")]
    public GameUser? GetUserByObjectId(ObjectId? id)
    {
        if (id == null) return null;
        return this.All<GameUser>().FirstOrDefault(u => u.UserId == id);
    }
    
    [Pure]
    [ContractAnnotation("null => null; notnull => canbenull")]
    public GameUser? GetUserByUuid(string? uuid)
    {
        if (uuid == null) return null;
        if(!ObjectId.TryParse(uuid, out ObjectId objectId)) return null;
        return this.All<GameUser>().FirstOrDefault(u => u.UserId == objectId);
    }
    
    /// <summary>
    /// ID lookup for legacy API (v1). Do not use for any other purpose.
    /// </summary>
    [Pure]
    [ContractAnnotation("null => null; notnull => canbenull")]
    public GameUser? GetUserByLegacyId(int? legacyId)
    {
        if (legacyId == null) return null;
        
        // THIS SUCKS
        return this.All<GameUser>().ToList().FirstOrDefault(u => u.UserId.Timestamp == legacyId);
    }

    public DatabaseList<GameUser> GetUsers(int count, int skip)
        => new(this.All<GameUser>().OrderByDescending(u => u.JoinDate), skip, count);
    
    private void UpdateUserData<TUpdateData>(GameUser user, TUpdateData data)
    {
        this.Write(() =>
        {
            PropertyInfo[] userProps = typeof(GameUser).GetProperties();
            foreach (PropertyInfo prop in typeof(TUpdateData).GetProperties())
            {
                if(prop.Name == "PlanetsHash") continue;
                
                object? value = prop.GetValue(data);
                if(value == null) continue;

                PropertyInfo? userProp = userProps.FirstOrDefault(p => p.Name == prop.Name);
                if (userProp == null) throw new ArgumentOutOfRangeException(prop.Name);
                
                userProp.SetValue(user, value);
            }
        });
    }
    
    public void UpdateUserData(GameUser user, SerializedUpdateData data) 
        => this.UpdateUserData<SerializedUpdateData>(user, data);
    
    public void UpdateUserData(GameUser user, ApiUpdateUserRequest data)
    {
        if (data.EmailAddress != null && data.EmailAddress != user.EmailAddress)
        {
            // TODO: batch this in with the other realm write somehow
            this.Write(() =>
            {
                user.EmailAddressVerified = false;
            });
        }

        data.EmailAddress = data.EmailAddress?.ToLowerInvariant();
        
        this.UpdateUserData<ApiUpdateUserRequest>(user, data);
    }

    [Pure]
    public int GetTotalUserCount() => this.All<GameUser>().Count();
    
    [Pure]
    public int GetActiveUserCount()
    {
        DateTimeOffset lastMonth = this.Time.Now.Subtract(TimeSpan.FromDays(30));
        return this.All<GameUser>().Count(u => u.LastLoginDate > lastMonth);
    }

    public void UpdateUserPins(GameUser user, UserPins pinsUpdate) 
    {
        this.Write(() => {
            user.Pins = new UserPins();

            foreach (long pinsAward in pinsUpdate.Awards) user.Pins.Awards.Add(pinsAward);
            foreach (long pinsAward in pinsUpdate.Progress) user.Pins.Progress.Add(pinsAward);
            foreach (long profilePins in pinsUpdate.ProfilePins) user.Pins.ProfilePins.Add(profilePins);
        });
    }

    public void SetUserRole(GameUser user, GameUserRole role)
    {
        if(role == GameUserRole.Banned) throw new InvalidOperationException($"Cannot ban a user with this method. Please use {nameof(this.BanUser)}().");
        this.Write(() =>
        {
            if (user.Role is GameUserRole.Banned or GameUserRole.Restricted)
            {
                user.BanReason = null;
                user.BanExpiryDate = null;
            };
            
            user.Role = role;
        });
    }

    private void PunishUser(GameUser user, string reason, DateTimeOffset expiryDate, GameUserRole role)
    {
        this.Write(() =>
        {
            user.Role = role;
            user.BanReason = reason;
            user.BanExpiryDate = expiryDate;
        });
    }

    public void BanUser(GameUser user, string reason, DateTimeOffset expiryDate)
    {
        this.PunishUser(user, reason, expiryDate, GameUserRole.Banned);
        this.RevokeAllTokensForUser(user);
    }

    public void RestrictUser(GameUser user, string reason, DateTimeOffset expiryDate) 
        => this.PunishUser(user, reason, expiryDate, GameUserRole.Restricted);

    private bool IsUserPunished(GameUser user, GameUserRole role)
    {
        if (user.Role != role) return false;
        if (user.BanExpiryDate == null) return false;
        
        return user.BanExpiryDate >= this.Time.Now;
    }

    public bool IsUserBanned(GameUser user) => this.IsUserPunished(user, GameUserRole.Banned);
    public bool IsUserRestricted(GameUser user) => this.IsUserPunished(user, GameUserRole.Restricted);

    public DatabaseList<GameUser> GetAllUsersWithRole(GameUserRole role)
    {
        // for some stupid reason, we have to do the byte conversion here or realm won't work correctly.
        byte roleByte = (byte)role;
        
        return new DatabaseList<GameUser>(this.All<GameUser>().Where(u => u._Role == roleByte));
    }

    public void DeleteUser(GameUser user)
    {
        const string deletedReason = "This user's account has been deleted.";
        
        this.BanUser(user, deletedReason, DateTimeOffset.MaxValue);
        this.RevokeAllTokensForUser(user);
        this.DeleteNotificationsByUser(user);
        
        this.Write(() =>
        {
            user.Pins = new UserPins();
            user.Location = new GameLocation();
            user.Description = deletedReason;
            user.EmailAddress = null;
            user.PasswordBcrypt = "deleted";
            user.JoinDate = DateTimeOffset.MinValue;
            user.LastLoginDate = DateTimeOffset.MinValue;
            user.Lbp2PlanetsHash = "0";
            user.Lbp3PlanetsHash = "0";
            user.VitaPlanetsHash = "0";
            user.IconHash = "0";
            user.AllowIpAuthentication = false;
            user.EmailAddressVerified = false;
            user.CurrentVerifiedIp = null;
            user.PsnAuthenticationAllowed = false;
            user.RpcnAuthenticationAllowed = false;

            foreach (GamePhotoSubject subject in user.PhotosWithMe)
                subject.User = null;
            
            this.RemoveRange(user.FavouriteLevelRelations);
            this.RemoveRange(user.UsersFavourited);
            this.RemoveRange(user.UsersFavouritingMe);
            this.RemoveRange(user.QueueLevelRelations);
            this.RemoveRange(user.PhotosByMe);

            foreach (GameLevel level in this.All<GameLevel>().Where(l => l.Publisher == user))
            {
                level.Publisher = null;
            }
        });
    }

    public void ResetUserPlanets(GameUser user)
    {
        this.Write(() =>
        {
            user.Lbp2PlanetsHash = "0";
            user.Lbp3PlanetsHash = "0";
            user.VitaPlanetsHash = "0";
        });
    }

    public void SetUserGriefReportRedirection(GameUser user, bool value)
    {
        this.Write(() =>
        {
            user.RedirectGriefReportsToPhotos = value;
        });
    }
    
    #if DEBUG
    public void ForceUserTokenGame(Token token, TokenGame game)
    {
        this.Write(() =>
        {
            token.TokenGame = game;
        });
    }
    
    public void ForceUserTokenPlatform(Token token, TokenPlatform platform)
    {
        this.Write(() =>
        {
            token.TokenPlatform = platform;
        });
    }
    #endif
}