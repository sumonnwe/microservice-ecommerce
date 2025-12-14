using EventBridge.BackgroundServices;
using EventBridge.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;

Log.Logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Console().CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext().WriteTo.Console());


// Allow the React frontend (dev at http://localhost:3000) to connect to SignalR
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
        policy.WithOrigins("http://localhost:3000") // adjust or add other origins for containers
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

builder.Services.AddSignalR();
//builder.Services.AddControllers();
// Ensure controllers from this assembly are discovered (prevents "No action descriptors found" when controller discovery fails)
builder.Services.AddControllers().AddApplicationPart(Assembly.GetExecutingAssembly());

builder.Services.AddHostedService<KafkaConsumerService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();

// Explicit routing/endpoints pipeline to ensure controllers and hubs are wired up correctly.
app.UseRouting();
// Use CORS before routing/hubs 
app.UseCors("CorsPolicy");

app.MapHub<EventHub>("/eventhub");
app.MapControllers();
app.Run();