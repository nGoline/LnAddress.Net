// Due to updated ECDSA generated tls.cert we need to let gprc know that
// we need to use that cipher suite otherwise there will be a handshake
// error when we communicate with the lnd rpc server.

using LnAddress.Net.Interfaces;
using LnAddress.Net.Services;

Environment.SetEnvironmentVariable("GRPC_SSL_CIPHER_SUITES", "HIGH+ECDSA");

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Make controller routes lowercase
builder.Services.Configure<RouteOptions>(options =>
{
    options.LowercaseUrls = true;
});

// Select the Lightning backend. Defaults to LND for backwards compatibility.
var lightningBackend = builder.Configuration["Lightning:Backend"] ?? "lnd";
switch (lightningBackend.Trim().ToLowerInvariant())
{
    case "lnd":
        builder.Services.AddSingleton<ILightningService, LndService>();
        break;
    case "clnrest":
        builder.Services.AddSingleton<ILightningService, ClnRestService>();
        break;
    case "cln":
        builder.Services.AddSingleton<ILightningService, ClnService>();
        break;
    default:
        throw new Exception($"Unknown Lightning:Backend '{lightningBackend}'. Supported values: lnd, clnrest, cln");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("*");
    });
});

var app = builder.Build();

// Enable CORS before other middleware
app.UseCors("AllowAll");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();