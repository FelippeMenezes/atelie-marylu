using AtelieMarylu.Domain.Catalogo;
using AtelieMarylu.Domain.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Data;

// Prepara o banco ao iniciar: aplica migrations, cria os papéis do sistema,
// o primeiro Admin (a partir da configuração) e, em desenvolvimento, produtos de exemplo.
public static class SeedDados
{
    public static async Task ExecutarAsync(IServiceProvider services, bool desenvolvimento)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(SeedDados));

        // Em produção, só aplica migrations se Banco:AplicarMigrations=true (o docker-compose de produção liga).
        // Serve para um único servidor; o EF trava a tabela de histórico, então duas instâncias não aplicam juntas.
        if (desenvolvimento || config.GetValue<bool>("Banco:AplicarMigrations"))
            await db.Database.MigrateAsync();

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var existentes = await roleManager.Roles.Select(r => r.Name).ToListAsync();
        foreach (var papel in new[] { Papeis.Admin, Papeis.Cliente }.Where(p => !existentes.Contains(p)))
            await roleManager.CreateAsync(new IdentityRole(papel));

        await CriarAdminInicialAsync(sp, config, logger);

        if (desenvolvimento && !await db.Produtos.AnyAsync())
        {
            db.Produtos.AddRange(
                new Produto { Nome = "Bolsa de tecido floral", Descricao = "Bolsa de ombro em tricoline floral, forrada, com bolso interno. Medidas: 30 x 25 x 10 cm.", Preco = 89.90m, Estoque = 5, PesoGramas = 350 },
                new Produto { Nome = "Necessaire média", Descricao = "Necessaire com zíper e forro impermeável. 22 x 14 cm.", Preco = 39.90m, Estoque = 10, PesoGramas = 150 },
                new Produto { Nome = "Porta-moedas", Descricao = "Porta-moedas com fecho de metal. Ótimo para presente.", Preco = 24.90m, Estoque = 15, PesoGramas = 80 },
                new Produto { Nome = "Ecobag estampada", Descricao = "Ecobag de algodão cru com estampa, alça reforçada.", Preco = 49.90m, Estoque = 0, PesoGramas = 200 },
                new Produto { Nome = "Bolsa sob medida", Descricao = "Escolha o tecido e a gente costura do seu jeito.", Preco = 129.90m, Estoque = 3, PesoGramas = 400, PrazoProducaoDias = 10 });
            await db.SaveChangesAsync();
        }
    }

    private static async Task CriarAdminInicialAsync(IServiceProvider sp, IConfiguration config, ILogger logger)
    {
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        if ((await userManager.GetUsersInRoleAsync(Papeis.Admin)).Count > 0)
            return;

        // Nunca deixe senha no appsettings.json: use user-secrets (dev) ou variável de ambiente (produção)
        var email = config["AdminInicial:Email"];
        var senha = config["AdminInicial:Senha"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(senha))
        {
            logger.LogWarning("Nenhum Admin cadastrado. Defina AdminInicial:Email e AdminInicial:Senha para criar o primeiro.");
            return;
        }

        var usuario = await userManager.FindByEmailAsync(email);
        if (usuario is null)
        {
            usuario = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, NomeCompleto = "Administrador", TrocarSenhaObrigatoria = true };
            var resultado = await userManager.CreateAsync(usuario, senha);
            if (!resultado.Succeeded)
            {
                logger.LogError("Falha ao criar o Admin inicial: {Erros}", string.Join("; ", resultado.Errors.Select(e => e.Description)));
                return;
            }
        }

        await userManager.AddToRoleAsync(usuario, Papeis.Admin);
        // Não registra o e-mail: dado pessoal não deve ir para o log
        logger.LogInformation("Admin inicial criado. Ele deve trocar a senha no primeiro acesso.");
    }
}
