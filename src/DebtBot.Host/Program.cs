using System.Threading.Channels;
using DebtBot.Core.Application;
using DebtBot.Core.Messaging;
using DebtBot.Host;
using DebtBot.Infrastructure;
using DebtBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var databasePath = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Database:Path"] ?? "data/debtbot.db");
builder.Services.AddInfrastructure(builder.Configuration, databasePath);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new RateLimiter(builder.Configuration.GetValue("RateLimit:CommandsPerMinute", 10)));
builder.Services.AddScoped<BotEngine>();
builder.Services.AddSingleton(Channel.CreateUnbounded<IncomingMessage>(new UnboundedChannelOptions { SingleReader = true }));
builder.Services.AddHostedService<MessageProcessor>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<BotDbContext>().Database.MigrateAsync();
}

var gatewayToken = app.Configuration["Gateway:Token"] ?? "";

// The gateway posts every "/" message here; it is queued and handled in order.
app.MapPost("/api/messages", (IncomingMessage message, HttpRequest request, Channel<IncomingMessage> queue) =>
{
    if (gatewayToken.Length > 0 && request.Headers["X-Gateway-Token"] != gatewayToken) return Results.Unauthorized();
    queue.Writer.TryWrite(message);
    return Results.Accepted();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
