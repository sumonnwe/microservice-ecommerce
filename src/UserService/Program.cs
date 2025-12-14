using System.Reflection;
using Microsoft.EntityFrameworkCore;
using UserService.DI;
using UserService.Infrastructure.EF;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
var kafka = builder.Configuration["KAFKA_BOOTSTRAP_SERVERS"] ?? "kafka:9092";
builder.Services.AddUserService(kafka);

// In-memory EF provider (fast for dev/tests). NOTE: no migrations with this provider.
builder.Services.AddDbContext<UserDbContext>(options =>
    options.UseInMemoryDatabase("UserService_InMemory"));

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

var app = builder.Build();

// Ensure database (creates schema in memory)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapControllers();
app.Run();