using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WestMarch.Application.Adventures;
using WestMarch.Application.Announcements;
using WestMarch.Application.Characters;
using WestMarch.Application.Common;
using WestMarch.Application.Ddb;
using WestMarch.Application.Sessions;
using WestMarch.Application.Storage;
using WestMarch.Application.Users;
using WestMarch.Infrastructure.Ddb;
using WestMarch.Infrastructure.Identity;
using WestMarch.Infrastructure.Persistence;
using WestMarch.Infrastructure.Persistence.Repositories;
using WestMarch.Infrastructure.Storage;

namespace WestMarch.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence, repositories, application services, and adapters.
    /// Auth/Identity wiring stays in the web host (it is HTTP-pipeline concerns).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        // SQL Server is the default provider; swapping providers is contained to this call
        // because everything upstream depends on the repository/service interfaces only.
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        // Repositories
        services.AddScoped<ICharacterRepository, CharacterRepository>();
        services.AddScoped<IAdventureRepository, AdventureRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IAnnouncementRepository, AnnouncementRepository>();

        // Application services
        services.AddScoped<ICharacterService, CharacterService>();
        services.AddScoped<IAdventureService, AdventureService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IAnnouncementService, AnnouncementService>();
        services.AddScoped<IPeopleService, PeopleService>();
        services.AddScoped<IUserDirectory, UserDirectory>();

        // Item catalog, inventory, marketplace (Phase 2)
        services.AddScoped<Application.Items.ICatalogRepository, Items.CatalogRepository>();
        services.AddScoped<Application.Items.IInventoryRepository, Items.InventoryRepository>();
        services.AddScoped<Application.Items.IMarketRepository, Items.MarketRepository>();
        services.AddScoped<Application.Items.ICatalogFileParser, Items.CatalogJsonParser>();
        services.AddScoped<Application.Items.ICatalogService, Application.Items.CatalogService>();
        services.AddScoped<Application.Items.IInventoryService, Application.Items.InventoryService>();
        services.AddScoped<Application.Items.IAuditService, Application.Items.AuditService>();
        services.AddScoped<Application.Items.IMarketplaceService, Application.Items.MarketplaceService>();
        services.AddScoped<Application.Items.IRewardClaimService, Application.Items.RewardClaimService>();

        // Bestiary + encounters (Phase 3)
        services.AddScoped<Application.Bestiary.IMonsterRepository, Bestiary.MonsterRepository>();
        services.AddScoped<Application.Bestiary.IMonsterFileParser, Bestiary.MonsterJsonParser>();
        services.AddScoped<Application.Bestiary.IMonsterService, Application.Bestiary.MonsterService>();

        // Feature flags + optional DDB adapter (isolated; returns null on any failure)
        services.Configure<FeatureFlags>(configuration.GetSection(FeatureFlags.SectionName));
        services.Configure<DdbAdapterOptions>(configuration.GetSection(DdbAdapterOptions.SectionName));
        services.AddMemoryCache();
        services.AddHttpClient<IDdbCharacterAdapter, DdbCharacterAdapter>();

        // Image storage: local disk in dev, Azure Blob in production — chosen by config.
        services.Configure<ImageStoreOptions>(configuration.GetSection(ImageStoreOptions.SectionName));
        var provider = configuration.GetSection(ImageStoreOptions.SectionName)["Provider"];
        if (string.Equals(provider, "AzureBlob", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IImageStore, AzureBlobImageStore>();
        }
        else
        {
            services.AddSingleton<IImageStore, LocalDiskImageStore>();
        }

        return services;
    }
}
