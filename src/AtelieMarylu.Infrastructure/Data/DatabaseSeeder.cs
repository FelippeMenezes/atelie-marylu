using AtelieMarylu.Domain.Catalog;
using AtelieMarylu.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AtelieMarylu.Infrastructure.Data;

// Prepara o banco ao iniciar: aplica migrations, cria os papéis do sistema,
// o primeiro Admin (a partir da configuração) e, em desenvolvimento, produtos de exemplo.
public static class DatabaseSeeder
{
    public static async Task RunAsync(IServiceProvider services, bool isDevelopment)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseSeeder));

        // Em produção, só aplica migrations se Database:ApplyMigrations=true (o docker-compose de produção liga).
        // Serve para um único servidor; o EF trava a tabela de histórico, então duas instâncias não aplicam juntas.
        if (isDevelopment || configuration.GetValue<bool>("Database:ApplyMigrations"))
            await db.Database.MigrateAsync();

        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
        var existing = await roleManager.Roles.Select(role => role.Name).ToListAsync();
        foreach (var role in new[] { AppRoles.Admin, AppRoles.Customer }.Where(p => !existing.Contains(p)))
            await roleManager.CreateAsync(new IdentityRole(role));

        await CreateInitialAdminAsync(provider, configuration, logger);

        if (isDevelopment && !await db.Products.AnyAsync())
        {
            db.Products.AddRange(
                new Product { Name = "Bolsa de tecido floral", Description = "Bolsa de ombro em tricoline floral, forrada, com bolso interno. Medidas: 30 x 25 x 10 cm.", Price = 89.90m, Stock = 5, WeightGrams = 350 },
                new Product { Name = "Necessaire média", Description = "Necessaire com zíper e forro impermeável. 22 x 14 cm.", Price = 39.90m, Stock = 10, WeightGrams = 150 },
                new Product { Name = "Porta-moedas", Description = "Porta-moedas com fecho de metal. Ótimo para presente.", Price = 24.90m, Stock = 15, WeightGrams = 80 },
                new Product { Name = "Ecobag estampada", Description = "Ecobag de algodão cru com estampa, alça reforçada.", Price = 49.90m, Stock = 0, WeightGrams = 200 },
                new Product { Name = "Bolsa sob medida", Description = "Escolha o tecido e a gente costura do seu jeito.", Price = 129.90m, Stock = 3, WeightGrams = 400, ProductionDays = 10 });
            await db.SaveChangesAsync();
        }
    }

    private static async Task CreateInitialAdminAsync(IServiceProvider provider, IConfiguration configuration, ILogger logger)
    {
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        if ((await userManager.GetUsersInRoleAsync(AppRoles.Admin)).Count > 0)
            return;

        // Nunca deixe senha no appsettings.json: use user-secrets (dev) ou variável de ambiente (produção)
        var email = configuration["InitialAdmin:Email"];
        var password = configuration["InitialAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Nenhum Admin cadastrado. Defina InitialAdmin:Email e InitialAdmin:Password para criar o primeiro.");
            return;
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FullName = "Administrador", MustChangePassword = true };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                logger.LogError("Falha ao criar o Admin inicial: {Erros}", string.Join("; ", result.Errors.Select(e => e.Description)));
                return;
            }
        }

        await userManager.AddToRoleAsync(user, AppRoles.Admin);
        // Não registra o e-mail: dado pessoal não deve ir para o log
        logger.LogInformation("Admin inicial criado. Ele deve trocar a senha no primeiro acesso.");
    }
}
