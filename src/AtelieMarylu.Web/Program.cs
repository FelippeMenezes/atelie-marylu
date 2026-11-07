using AtelieMarylu.Infrastructure;
using AtelieMarylu.Infrastructure.Data;
using AtelieMarylu.Web.Extensions;

// Ponto de partida da aplicação. Os detalhes de cada assunto ficam na pasta Extensions.
var builder = WebApplication.CreateBuilder(args);

builder.AddStructuredLogging();

builder.Services
    .AddInfrastructure(builder.Configuration)
    .AddWebInterface()
    .AddStoreAuthentication(builder.Configuration)
    .AddStoreAuthorization()
    .AddStoreRateLimiting()
    .AddReverseProxySupport(builder.Configuration)
    .AddStoreServices(builder.Configuration);

var app = builder.Build();

app.EnsureAllowedHostsIsConfigured();

await DatabaseSeeder.RunAsync(app.Services, app.Environment.IsDevelopment());

app.UseStorePipeline();
app.MapStoreEndpoints();

app.Run();
