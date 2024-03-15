using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Bunkum.AutoDiscover.Extensions;
using Bunkum.Core.Authentication;
using Bunkum.Core.Configuration;
using Bunkum.Core.Database;
using Bunkum.Core.RateLimit;
using Bunkum.Core.Services;
using Bunkum.Core.Storage;
using Bunkum.HealthChecks;
using Bunkum.HealthChecks.RealmDatabase;
using Bunkum.Protocols.Http;
using Microsoft.EntityFrameworkCore;
using NotEnoughLogs;
using NotEnoughLogs.Behaviour;
using NotEnoughLogs.Sinks;
using Refresh.Common;
using Refresh.GameServer.Authentication;
using Refresh.GameServer.Configuration;
using Refresh.GameServer.Database;
using Refresh.GameServer.Database.Postgres;
using Refresh.GameServer.Database.Realm;
using Refresh.GameServer.Documentation;
using Refresh.GameServer.Endpoints;
using Refresh.GameServer.Importing;
using Refresh.GameServer.Middlewares;
using Refresh.GameServer.Services;
using Refresh.GameServer.Time;
using Refresh.GameServer.Types.Levels.Categories;
using Refresh.GameServer.Types.Roles;
using Refresh.GameServer.Types.UserData;
using Refresh.GameServer.Workers;

namespace Refresh.GameServer;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class RefreshGameServer : RefreshServer
{
    protected WorkerManager? WorkerManager;
    
    protected readonly IDatabaseProvider<IGameDatabaseContext> _databaseProvider;
    protected readonly IDataStore _dataStore;
    
    protected GameServerConfig? _config;
    protected IntegrationConfig? _integrationConfig;

    public RefreshGameServer(
        BunkumHttpListener? listener = null,
        Func<IDatabaseProvider<IGameDatabaseContext>>? databaseProvider = null,
        IAuthenticationProvider<Token>? authProvider = null,
        IDataStore? dataStore = null
    ) : base(listener)
    {
        // databaseProvider ??= () => new RealmGameDatabaseProvider();
        databaseProvider ??= () => new PostgresGameDatabaseProvider((options) =>
        {
            options.UseNpgsql();
        });
        dataStore ??= new FileSystemDataStore();
        
        this._databaseProvider = databaseProvider.Invoke();
        this._databaseProvider.Initialize();
        this._dataStore = dataStore;
        
        this.SetupInitializer(() =>
        {
            IDatabaseProvider<IGameDatabaseContext> provider = databaseProvider.Invoke();
            
            this.WorkerManager?.Stop();
            this.WorkerManager = new WorkerManager(this.Logger, this._dataStore!, provider);
            
            authProvider ??= new GameAuthenticationProvider(this._config!);

            this.InjectBaseServices(provider, authProvider, dataStore);
        });
    }

    private void InjectBaseServices(IDatabaseProvider<IGameDatabaseContext> databaseProvider, IAuthenticationProvider<Token> authProvider, IDataStore dataStore)
    {
        this.Server.UseDatabaseProvider(databaseProvider);
        this.Server.AddAuthenticationService(authProvider, true);
        this.Server.AddStorageService(dataStore);
    }

    protected override void Initialize()
    {
        base.Initialize();
        this.SetupWorkers();
    }

    protected override void SetupMiddlewares()
    {
        this.Server.AddMiddleware<ApiV2GoneMiddleware>();
        this.Server.AddMiddleware<LegacyAdapterMiddleware>();
        this.Server.AddMiddleware<WebsiteMiddleware>();
        this.Server.AddMiddleware<DigestMiddleware>();
        this.Server.AddMiddleware<CrossOriginMiddleware>();
        this.Server.AddMiddleware<PspVersionMiddleware>();
    }

    protected override void SetupConfiguration()
    {
        GameServerConfig config = Config.LoadFromJsonFile<GameServerConfig>("refreshGameServer.json", this.Server.Logger);
        this._config = config;

        IntegrationConfig integrationConfig = Config.LoadFromJsonFile<IntegrationConfig>("integrations.json", this.Server.Logger);
        this._integrationConfig = integrationConfig;
        
        this.Server.AddConfig(config);
        this.Server.AddConfig(integrationConfig);
        this.Server.AddConfigFromJsonFile<RichPresenceConfig>("rpc.json");
    }
    
    protected override void SetupServices()
    {
        this.Server.AddService<TimeProviderService>(this.GetTimeProvider());
        this.Server.AddRateLimitService(new RateLimitSettings(60, 400, 30, "global"));
        this.Server.AddService<CategoryService>();
        this.Server.AddService<MatchService>();
        this.Server.AddService<ImportService>();
        this.Server.AddService<DocumentationService>();
        this.Server.AddService<GuidCheckerService>();
        this.Server.AddAutoDiscover(serverBrand: $"{this._config!.InstanceName} (Refresh)",
            baseEndpoint: GameEndpointAttribute.BaseRoute.Substring(0, GameEndpointAttribute.BaseRoute.Length - 1),
            usesCustomDigestKey: true,
            serverDescription: this._config.InstanceDescription,
            bannerImageUrl: "https://github.com/LittleBigRefresh/Branding/blob/main/logos/refresh_type.png?raw=true");
        
        this.Server.AddHealthCheckService(this._databaseProvider, new []
        {
            typeof(RealmDatabaseHealthCheck),
        });
        
        this.Server.AddService<RoleService>();
        this.Server.AddService<SmtpService>();

        if (this._config!.TrackRequestStatistics)
            this.Server.AddService<RequestStatisticTrackingService>();
        
        this.Server.AddService<LevelListOverrideService>();
        
        this.Server.AddService<CommandService>();
        
        #if DEBUG
        this.Server.AddService<DebugService>();
        #endif
    }

    protected virtual void SetupWorkers()
    {
        if (this.WorkerManager == null) return;
        
        this.WorkerManager.AddWorker<PunishmentExpiryWorker>();
        this.WorkerManager.AddWorker<ExpiredObjectWorker>();
        this.WorkerManager.AddWorker<CoolLevelsWorker>();
        
        if ((this._integrationConfig?.DiscordWebhookEnabled ?? false) && this._config != null)
        {
            this.WorkerManager.AddWorker(new DiscordIntegrationWorker(this._integrationConfig, this._config));
        }
    }

    /// <inheritdoc/>
    public override void Start()
    {
        this.Server.Start();
        this.WorkerManager?.Start();

        if (this._config!.MaintenanceMode)
        {
            this.Logger.LogWarning(RefreshContext.Startup, "The server is currently in maintenance mode! " +
                                                            "Only administrators will be able to log in and interact with the server.");
        }
    }

    /// <inheritdoc/>
    public override void Stop()
    {
        this.Server.Stop();
        this.WorkerManager?.Stop();
    }

    private IGameDatabaseContext GetContext()
    {
        return this._databaseProvider.GetContext();
    }
    
    protected virtual IDateTimeProvider GetTimeProvider()
    {
        return new SystemDateTimeProvider();
    }

    public void ImportAssets(bool force = false)
    {
        using IGameDatabaseContext context = this.GetContext();
        
        AssetImporter importer = new();
        importer.ImportFromDataStore(context, this._dataStore);
    }

    public void ImportImages()
    {
        using IGameDatabaseContext context = this.GetContext();
        
        ImageImporter importer = new();
        importer.ImportFromDataStore(context, this._dataStore);
    }

    public void CreateUser(string username, string emailAddress)
    {
        using IGameDatabaseContext context = this.GetContext();
        GameUser user = context.CreateUser(username, emailAddress);
        context.VerifyUserEmail(user);
    }
    
    public void SetAdminFromUsername(string username)
    {
        using IGameDatabaseContext context = this.GetContext();

        GameUser? user = context.GetUserByUsername(username);
        if (user == null) throw new InvalidOperationException("Cannot find the user " + username);

        context.SetUserRole(user, GameUserRole.Admin);
    }
    
    public void SetAdminFromEmailAddress(string emailAddress)
    {
        using IGameDatabaseContext context = this.GetContext();

        GameUser? user = context.GetUserByEmailAddress(emailAddress);
        if (user == null) throw new InvalidOperationException("Cannot find a user by emailAddress " + emailAddress);

        context.SetUserRole(user, GameUserRole.Admin);
    }

    public override void Dispose()
    {
        this._databaseProvider.Dispose();
        base.Dispose();
    }
}