using AtelieMarylu.Domain.Auditoria;
using AtelieMarylu.Domain.Seguranca;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AtelieMarylu.Tests.Integracao;

// Os serviços que substituíram as consultas feitas dentro das telas
public class ServicosDeConsultaTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private AuditoriaService Auditoria() =>
        new(new UsuarioAtual(new ServiceCollection().BuildServiceProvider(), new HttpContextAccessor()), banco);

    private ProdutoService Produtos() => new(banco, Auditoria());

    private static DadosProduto Dados(string nome = "Bolsa", int estoque = 3, bool ativo = true) =>
        new(nome, "Descrição", 80m, estoque, ativo, null, 300, 5, 20, 25);

    [FactComBanco]
    public async Task Salvar_cria_produto_com_fotos_e_audita()
    {
        var id = await Produtos().SalvarAsync(null, Dados(), [new ImagemSalva("/a.webp", "/a-mini.webp")]);

        var produto = await Produtos().ObterAsync(id!.Value);
        Assert.Equal("Bolsa", produto!.Nome);
        Assert.Single(produto.Imagens);

        await using var db = banco.CreateDbContext();
        Assert.True(await db.Auditoria.AnyAsync(a => a.Acao == AcoesAuditoria.ProdutoCriado && a.EntidadeId == id.Value.ToString()));
    }

    [FactComBanco]
    public async Task Salvar_edita_produto_e_troca_as_fotos()
    {
        var id = await Produtos().SalvarAsync(null, Dados(), [new ImagemSalva("/a.webp", "/a-mini.webp")]);

        await Produtos().SalvarAsync(id, Dados("Bolsa nova", estoque: 9), [new ImagemSalva("/b.webp", "/b-mini.webp")]);

        var produto = await Produtos().ObterAsync(id!.Value);
        Assert.Equal("Bolsa nova", produto!.Nome);
        Assert.Equal(9, produto.Estoque);
        Assert.Equal("/b.webp", Assert.Single(produto.Imagens).Url);
    }

    [FactComBanco]
    public async Task Salvar_produto_inexistente_devolve_null()
    {
        Assert.Null(await Produtos().SalvarAsync(int.MaxValue, Dados(), []));
    }

    [FactComBanco]
    public async Task Vitrine_nao_mostra_produto_inativo()
    {
        var inativo = await Produtos().SalvarAsync(null, Dados("Oculto", ativo: false), []);

        Assert.DoesNotContain(await Produtos().ListarVitrineAsync(), p => p.Id == inativo);
        Assert.Null(await Produtos().ObterAtivoAsync(inativo!.Value));
    }

    [FactComBanco]
    public async Task Lista_de_clientes_so_traz_quem_tem_o_papel_cliente()
    {
        var cliente = await banco.CriarClienteAsync();
        var semPapel = await banco.CriarClienteAsync();
        await using (var db = banco.CreateDbContext())
        {
            var papel = new IdentityRole(Papeis.Cliente) { NormalizedName = Papeis.Cliente.ToUpperInvariant() };
            db.Roles.Add(papel);
            db.UserRoles.Add(new IdentityUserRole<string> { RoleId = papel.Id, UserId = cliente.Id });
            await db.SaveChangesAsync();
        }

        var clientes = await new ClienteService(banco).ListarTodosAsync();

        Assert.Contains(clientes, c => c.Id == cliente.Id);
        Assert.DoesNotContain(clientes, c => c.Id == semPapel.Id);
    }
}
