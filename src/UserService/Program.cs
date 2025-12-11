using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UserService.DI;
using Serilog;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// bootstrap Serilog
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// OpenTelemetry tracing
builder.Services.AddOpenTelemetryTracing(b =>
{
    b.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("UserService"));
    b.AddAspNetCoreInstrumentation();
    b.AddHttpClientInstrumentation();
    var jaeger = builder.Configuration["OTEL_EXPORTER_JAEGER_ENDPOINT"];
    if (!string.IsNullOrEmpty(jaeger))
    {
        b.AddJaegerExporter(o => o.Endpoint = new Uri(jaeger));
    }
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Correlation ID middleware
builder.Services.AddSingleton<CorrelationIdMiddleware>();

builder.Services.AddUserService(builder.Configuration);

// JWT Auth
var jwtKey = builder.Configuration["JWT_SECRET"] ?? "supersecret_jwt_key_for_demo";
var key = System.Text.Encoding.ASCII.GetBytes(jwtKey);
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(opt =>
{
    opt.RequireHttpsMetadata = false;
    opt.SaveToken = true;
    opt.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false
    };
});

var app = builder.Build();

// Apply migrations automatically
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UserService.Infrastructure.EF.UserDbContext>();
    db.Database.Migrate();
}

app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ctx.Request.Headers.ContainsKey("X-Correlation-ID"))
        {
            ctx.Request.Headers["X-Correlation-ID"] = System.Guid.NewGuid().ToString();
        }
        ctx.Response.Headers["X-Correlation-ID"] = ctx.Request.Headers["X-Correlation-ID"];
        await _next(ctx);
    }
}
