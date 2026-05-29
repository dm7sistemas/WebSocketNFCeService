using WebSocketNFCeService;
using WebSocketNFCeService.Infra.Configuration;
using WebSocketNFCeService.Infra.Services;
using WebSocketNFCeService.NFe.Services;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "AZFoodWebSocketNFCe";
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddEventLog(options =>
{
    options.SourceName = "AZFoodWebSocketNFCe";
    options.LogName = "Application";
});

builder.Services.Configure<NFeConfig>(
    builder.Configuration.GetSection(NFeConfig.SectionName));
builder.Services.Configure<WebSocketConfig>(
    builder.Configuration.GetSection(WebSocketConfig.SectionName));

builder.Services.AddSingleton<ConfigPersistenceService>();
builder.Services.AddSingleton<INFeService, NFeService>();
builder.Services.AddSingleton<INFePdfService, NFePdfService>();
builder.Services.AddSingleton<NFeWebSocketHandler>();

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

var wsConfig = app.Configuration
    .GetSection(WebSocketConfig.SectionName)
    .Get<WebSocketConfig>() ?? new WebSocketConfig();

app.Map(wsConfig.Path, async (HttpContext context) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var handler = context.RequestServices
            .GetRequiredService<NFeWebSocketHandler>();
        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await handler.HandleAsync(webSocket, context.RequestAborted);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket required");
    }
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "running",
    service = "AZFoodWebSocketNFCe",
    wsEndpoint = $"ws://localhost:{wsConfig.Porta}{wsConfig.Path}"
}));

app.Urls.Add($"http://0.0.0.0:{wsConfig.Porta}");

var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var loggerProgram = loggerFactory.CreateLogger<Program>();

try
{
    app.Run();
}
catch (Exception ex)
{
    loggerProgram.LogCritical(ex, "Falha ao iniciar serviço");
    throw;
}
