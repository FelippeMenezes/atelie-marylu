using AtelieMarylu.Domain.Auditoria;
using AtelieMarylu.Domain.Catalogo;
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

    // Mesma operação da baixa de estoque de um pedido
    private async Task VenderAsync(int produtoId, int quantidade)
    {
        await using var db = banco.CreateDbContext();
        await db.Produtos.Where(p => p.Id == produtoId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estoque, p => p.Estoque - quantidade));
    }

    private async Task<string> CriarClienteDoPapelAsync(string nome)
    {
        var cliente = await banco.CriarClienteAsync();
        await using var db = banco.CreateDbContext();
        var papel = await db.Roles.FirstOrDefaultAsync(r => r.Name == Papeis.Cliente);
        if (papel is null)
        {
            papel = new IdentityRole(Papeis.Cliente) { NormalizedName = Papeis.Cliente.ToUpperInvariant() };
            db.Roles.Add(papel);
        }
        db.UserRoles.Add(new IdentityUserRole<string> { RoleId = papel.Id, UserId = cliente.Id });
        var usuario = await db.Users.SingleAsync(u => u.Id == cliente.Id);
        usuario.NomeCompleto = nome;
        await db.SaveChangesAsync();
        return cliente.Id;
    }

    private static DadosProduto Dados(string nome = "Bolsa", int estoque = 3, bool ativo = true, int estoqueAoAbrir = 0) =>
        new(nome, "Descrição", 80m, estoque, ativo, null, 300, 5, 20, 25, estoqueAoAbrir);

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

        await Produtos().SalvarAsync(id, Dados("Bolsa nova", estoque: 9, estoqueAoAbrir: 3), [new ImagemSalva("/b.webp", "/b-mini.webp")]);

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

    // Formulário aberto com 5, cliente compra 2 e o admin salva só uma mudança de nome
    [FactComBanco]
    public async Task Salvar_nao_desfaz_venda_feita_com_o_formulario_aberto()
    {
        var id = (await Produtos().SalvarAsync(null, Dados(estoque: 5), []))!.Value;
        await VenderAsync(id, 2);

        await Produtos().SalvarAsync(id, Dados("Outro nome", estoque: 5, estoqueAoAbrir: 5), []);

        Assert.Equal(3, await banco.EstoqueAsync(id));
    }

    [FactComBanco]
    public async Task Salvar_aplica_so_o_ajuste_do_admin_sobre_o_estoque_atual()
    {
        var id = (await Produtos().SalvarAsync(null, Dados(estoque: 5), []))!.Value;
        await VenderAsync(id, 2);

        await Produtos().SalvarAsync(id, Dados(estoque: 8, estoqueAoAbrir: 5), []);

        Assert.Equal(6, await banco.EstoqueAsync(id));
    }

    [FactComBanco]
    public async Task Salvar_recusa_ajuste_que_deixaria_o_estoque_negativo()
    {
        var id = (await Produtos().SalvarAsync(null, Dados(estoque: 5), []))!.Value;
        await VenderAsync(id, 4);

        await Assert.ThrowsAsync<RegraDeProdutoException>(() =>
            Produtos().SalvarAsync(id, Dados(estoque: 2, estoqueAoAbrir: 5), []));

        Assert.Equal(1, await banco.EstoqueAsync(id));
    }

    [FactComBanco]
    public async Task Busca_de_clientes_trata_curingas_como_texto()
    {
        var alvo = await CriarClienteDoPapelAsync("maria_souza");
        var parecido = await CriarClienteDoPapelAsync("mariaXsouza");

        var encontrados = await new ClienteService(banco).ListarAsync("maria_souza");

        Assert.Contains(encontrados, c => c.Id == alvo);
        Assert.DoesNotContain(encontrados, c => c.Id == parecido);
    }

    [FactComBanco]
    public async Task Anonimizar_remove_o_email_do_historico()
    {
        var usuarioId = Guid.NewGuid().ToString();
        var email = $"{usuarioId}@teste.local";
        await using (var db = banco.CreateDbContext())
        {
            await Auditoria().RegistrarAsync(db, AcoesAuditoria.PedidoCriado, EntidadesAuditoria.Pedido, 1, null, new Autor(usuarioId, email));
            await Auditoria().RegistrarAsync(db, AcoesAuditoria.AcessoAlterado, EntidadesAuditoria.Usuario, email, null, Autor.Sistema);
            await db.SaveChangesAsync();
        }

        await Auditoria().AnonimizarUsuarioAsync(usuarioId, email);

        await using var leitura = banco.CreateDbContext();
        Assert.False(await leitura.Auditoria.AnyAsync(a => a.Autor == email || a.EntidadeId == email));
        Assert.Equal(2, await leitura.Auditoria.CountAsync(a => a.UsuarioId == usuarioId || a.EntidadeId == usuarioId));
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
            var papel = await db.Roles.FirstOrDefaultAsync(r => r.Name == Papeis.Cliente);
            if (papel is null)
            {
                papel = new IdentityRole(Papeis.Cliente) { NormalizedName = Papeis.Cliente.ToUpperInvariant() };
                db.Roles.Add(papel);
            }
            db.UserRoles.Add(new IdentityUserRole<string> { RoleId = papel.Id, UserId = cliente.Id });
            await db.SaveChangesAsync();
        }

        var clientes = await new ClienteService(banco).ListarParaSelecaoAsync();

        Assert.Contains(clientes, c => c.Id == cliente.Id);
        Assert.DoesNotContain(clientes, c => c.Id == semPapel.Id);
    }
}
