using System.Security.Cryptography;
using MongoDB.Bson;
using Refresh.GameServer.Authentication;
using Refresh.GameServer.Extensions;
using Refresh.GameServer.Types.UserData;
using Refresh.GameServer.Verification;

namespace Refresh.GameServer.Database;

public partial class GameDatabaseContext // Registration
{
    public GameUser CreateUser(string username, string emailAddress, bool skipChecks = false)
    {
        if (!skipChecks)
        {
            if (!this.IsUsernameValid(username))
                throw new FormatException(
                    "Username must be valid (3 to 16 alphanumeric characters, plus hyphens and underscores)"); 
            
            if (this.IsUsernameTaken(username))
                throw new InvalidOperationException("Cannot create a user with an existing username");
        
            if (this.IsEmailTaken(emailAddress))
                throw new InvalidOperationException("Cannot create a user with an existing email address");
        }

        emailAddress = emailAddress.ToLowerInvariant();
        
        GameUser user = new()
        {
            Username = username,
            EmailAddress = emailAddress,
            EmailAddressVerified = false,
            JoinDate = this._time.Now,
        };

        this._realm.Write(() =>
        {
            this._realm.Add(user);
        });
        return user;
    }

    public GameUser CreateUserFromQueuedRegistration(QueuedRegistration registration, TokenPlatform? platform = null)
    {
        QueuedRegistration cloned = (QueuedRegistration)registration.Clone();

        this._realm.Write(() =>
        {
            this._realm.Remove(registration);
        });

        GameUser user = this.CreateUser(cloned.Username, cloned.EmailAddress);
        this.SetUserPassword(user, cloned.PasswordBcrypt);

        if (platform != null)
        {
            this._realm.Write(() =>
            {
                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (platform)
                {
                    case TokenPlatform.PS3:
                    case TokenPlatform.Vita:
                        user.PsnAuthenticationAllowed = true;
                        break;
                    case TokenPlatform.RPCS3:
                        user.RpcnAuthenticationAllowed = true;
                        break;
                }
            });
        }

        return user;
    }
    
    public bool IsUsernameValid(string username)
    {
        return CommonPatterns.UsernameRegex().IsMatch(username);
    }

    public bool IsUsernameTaken(string username)
    {
        return this._realm.All<GameUser>().Any(u => u.Username == username) ||
               this._realm.All<QueuedRegistration>().Any(r => r.Username == username);
    }
    
    public bool IsEmailTaken(string emailAddress)
    {
        return this._realm.All<GameUser>().Any(u => u.EmailAddress == emailAddress) ||
               this._realm.All<QueuedRegistration>().Any(r => r.EmailAddress == emailAddress);
    }

    public void AddRegistrationToQueue(string username, string emailAddress, string passwordBcrypt)
    {
        if (this.IsUsernameTaken(username))
            throw new InvalidOperationException("Cannot create a registration with an existing username");
        
        if (this.IsEmailTaken(emailAddress))
            throw new InvalidOperationException("Cannot create a user with an existing email address");
        
        QueuedRegistration registration = new()
        {
            Username = username,
            EmailAddress = emailAddress,
            PasswordBcrypt = passwordBcrypt,
            ExpiryDate = this._time.Now + TimeSpan.FromHours(1),
        };

        this._realm.Write(() =>
        {
            this._realm.Add(registration);
        });
    }

    public void RemoveRegistrationFromQueue(QueuedRegistration registration)
    {
        this._realm.Write(() =>
        {
            this._realm.Remove(registration);
        });
    }
    
    public void RemoveAllRegistrationsFromQueue()
    {
        this._realm.Write(() =>
        {
            this._realm.RemoveAll<QueuedRegistration>();
        });
    }
    
    public bool IsRegistrationExpired(QueuedRegistration registration) => registration.ExpiryDate < this._time.Now;

    public QueuedRegistration? GetQueuedRegistrationByUsername(string username) 
        => this._realm.All<QueuedRegistration>().FirstOrDefault(q => q.Username == username);
    
    public QueuedRegistration? GetQueuedRegistrationByObjectId(ObjectId id) 
        => this._realm.All<QueuedRegistration>().FirstOrDefault(q => q.RegistrationId == id);
    

    public DatabaseList<QueuedRegistration> GetAllQueuedRegistrations()
        => new(this._realm.All<QueuedRegistration>());
    
    public DatabaseList<EmailVerificationCode> GetAllVerificationCodes()
        => new(this._realm.All<EmailVerificationCode>());
    
    public void VerifyUserEmail(GameUser user)
    {
        this._realm.Write(() =>
        {
            user.EmailAddressVerified = true;
            this._realm.RemoveRange(this._realm.All<EmailVerificationCode>()
                .Where(c => c.User == user));
        });
    }

    public bool VerificationCodeMatches(GameUser user, string code) => 
        this._realm.All<EmailVerificationCode>().Any(c => c.User == user && c.Code == code);
    
    public bool IsVerificationCodeExpired(EmailVerificationCode code) => code.ExpiryDate < this._time.Now;

    private static string GenerateDigitCode()
    {
        ReadOnlySpan<byte> validChars = "0123456789"u8;
        Span<char> result = stackalloc char[6];
        Span<byte> randomBytes = stackalloc byte[6];

        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
            
        for (int i = 0; i < randomBytes.Length; i++)
        {
            int index = randomBytes[i] % validChars.Length;
            result[i] = (char)validChars[index];
        }

        return new string(result);
    }

    public EmailVerificationCode CreateEmailVerificationCode(GameUser user)
    {
        EmailVerificationCode verificationCode = new()
        {
            User = user,
            Code = GenerateDigitCode(),
            ExpiryDate = this._time.Now + TimeSpan.FromDays(1),
        };

        this._realm.Write(() =>
        {
            this._realm.Add(verificationCode);
        });

        return verificationCode;
    }

    public void RemoveEmailVerificationCode(EmailVerificationCode code)
    {
        this._realm.Write(() =>
        {
            this._realm.Remove(code);
        });
    }
    
    public bool DisallowUser(string username)
    {
        if (this._realm.Find<DisallowedUser>(username) != null) 
            return false;
        
        this._realm.Write(() =>
        {
            this._realm.Add(new DisallowedUser
            {
                Username = username,
            });
        });
        
        return true;
    }
    
    public bool ReallowUser(string username)
    {
        DisallowedUser? disallowedUser = this._realm.Find<DisallowedUser>(username);
        if (disallowedUser == null) 
            return false;
        
        this._realm.Write(() =>
        {
            this._realm.Remove(disallowedUser);
        });
        
        return true;
    }
    
    public bool IsUserDisallowed(string username)
    {
        return this._realm.Find<DisallowedUser>(username) != null;
    }
}