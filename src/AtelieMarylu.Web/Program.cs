using System.Globalization;
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

var culturaBrasil = new CultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = culturaBrasil;
CultureInfo.DefaultThreadCurrentUICulture = culturaBrasil;

var app = builder.Build();

app.EnsureAllowedHostsIsConfigured();

await SeedDados.ExecutarAsync(app.Services, app.Environment.IsDevelopment());

app.UseStorePipeline();
app.MapStoreEndpoints();

app.Run();
