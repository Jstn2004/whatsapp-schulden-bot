using DebtBot.Core.Application;
using DebtBot.Core.Messaging;
using DebtBot.Infrastructure.Gateway;
using DebtBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DebtBot.Infrastructure;

public static class DependencyInjection
{
    /// <param name="databasePath">Path of the SQLite file; its folder is created if needed.</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        services.AddDbContext<BotDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<IBotDb>(sp => sp.GetRequiredService<BotDbContext>());

        var gateway = config.GetSection("Gateway").Get<GatewayOptions>() ?? new GatewayOptions();
        services.AddHttpClient<IMessagingGateway, GatewayClient>(http =>
        {
            http.BaseAddress = new Uri(gateway.Url);
            http.Timeout = TimeSpan.FromSeconds(30);
            if (gateway.Token.Length > 0) http.DefaultRequestHeaders.Add("X-Gateway-Token", gateway.Token);
        });

        return services;
    }
}
