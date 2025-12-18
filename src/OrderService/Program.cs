using System.Reflection;
using Microsoft.EntityFrameworkCore;
using OrderService.DI;
using OrderService.Infrastructure.EF;
using OrderService.Handlers;
using OrderService.BackgroundServices;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOrderService(builder.Configuration);

// In-memory EF provider (fast for dev/tests). NOTE: no migrations with this provider.
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseInMemoryDatabase("OrderService_InMemory"));

// Register handler and Kafka consumer hosted service
builder.Services.AddScoped<UserStatusChangedHandler>();
builder.Services.AddHostedService<UserStatusConsumerService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Keep HTTP client to validate existing logic (still used in Create order).
builder.Services.AddHttpClient("userservice", client =>
{
    var baseUrl = builder.Configuration["USER_SERVICE_BASE_URL"] ?? "http://userservice:8080";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

var app = builder.Build();

// Ensure database (creates schema in memory)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.MapControllers();
app.Run();