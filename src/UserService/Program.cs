using System.Reflection;
using Microsoft.EntityFrameworkCore;
using UserService.DI;
using UserService.Infrastructure.EF;

var builder = WebApplication.CreateBuilder(args);

// Ensure appsettings.json is loaded (it's loaded by default, this is explicit)
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

// Register feature services and pass the configuration so DI registrations that need config can use it
builder.Services.AddUserService(builder.Configuration);

// In-memory EF provider (fast for dev/tests). NOTE: no migrations with this provider.
builder.Services.AddDbContext<UserDbContext>(options =>
    options.UseInMemoryDatabase("UserService_InMemory"));

builder.Services.AddControllers();
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