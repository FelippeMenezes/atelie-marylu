using AtelieMarylu.Domain.Carrinho;
using AtelieMarylu.Domain.Frete;
using AtelieMarylu.Domain.Pagamentos;
using AtelieMarylu.Domain.Pedidos;
using AtelieMarylu.Web.Configuracao;
using AtelieMarylu.Web.Integracoes;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests.Integracao;

// Relógio controlável: permite testar a expiração sem esperar horas
public class RelogioFalso : TimeProvider
{
    // Sem frações de segundo: o Postgres guarda microssegundos e a comparação exata falharia
    public DateTimeOffset Agora { get; set; } = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    public override DateTimeOffset GetUtcNow() => Agora;
}

public class PedidoServiceTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private readonly RelogioFalso relogio = new();
    private readonly LojaOptions loja = new() { DescontoPixPercentual = 5, PrazoPagamentoMinutos = 120, MaxPedidosPendentesPorCliente = 3 };

    private PedidoService Servico()
    {
        var auditoria = new AuditoriaService(new UsuarioAtual(new ServiceCollection().BuildServiceProvider(), new HttpContextAccessor()));
        return new PedidoService(banco, new FreteSimulado(), new PagamentoSimulado(), auditoria, new PedidoEventos(),
            Options.Create(loja), relogio, NullLogger<PedidoService>.Instance);
    }

    private static EnderecoEntrega Endereco() => new()
    {
        Cep = "20000-000", Logradouro = "Rua A", Numero = "1", Bairro = "Centro", Cidade = "Rio de Janeiro", Uf = "RJ",
    };

    [FactComBanco]
    public async Task Criar_pedido_usa_preco_do_banco_baixa_estoque_e_audita()
    {
        var cliente = await banco.CriarClienteAsync();
        var produto = await banco.CriarProdutoAsync(estoque: 5, preco: 89.90m);

        var pedido = await Servico().CriarAsync(cliente.Id, [new ItemCarrinho(produto.Id, 2)], Endereco(), "pac");

        Assert.Equal(179.80m, pedido.Subtotal);
        Assert.Equal("20000000", pedido.Endereco.Cep);
        Assert.Equal(5m, pedido.PercentualDescontoPix);
        Assert.Equal(relogio.Agora.UtcDateTime.AddMinutes(120), pedido.PagarAte);
        Assert.Equal(3, await banco.EstoqueAsync(produto.Id));
        await using var db = banco.CreateDbContext();
        Assert.True(await db.Auditoria.AnyAsync(a => a.Acao == "pedido.criado" && a.EntidadeId == pedido.Id.ToString()));
    }

    [FactComBanco]
    public async Task Nao_vende_mais_que_o_estoque()
    {
        var cliente = await banco.CriarClienteAsync();
        var produto = await banco.CriarProdutoAsync(estoque: 1);

        await Assert.ThrowsAsync<RegraDePedidoException>(() =>
            Servico().CriarAsync(cliente.Id, [new ItemCarrinho(produto.Id, 2)], Endereco(), "pac"));
        Assert.Equal(1, await banco.EstoqueAsync(produto.Id));
    }

    [FactComBanco]
    public async Task Duas_compras_simultaneas_da_ultima_peca_so_uma_passa()
    {
        var produto = await banco.CriarProdutoAsync(estoque: 1);
        var clientes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => banco.CriarClienteAsync()));

        var tentativas = clientes.Select(c => Task.Run(async () =>
        {
            try
            {
                await Servico().CriarAsync(c.Id, [new ItemCarrinho(produto.Id, 1)], Endereco(), "pac");
                return true;
            }
            catch (RegraDePedidoException)
            {
                return false;
            }
        }));
        var resultados = await Task.WhenAll(tentativas);

        Assert.Equal(1, resultados.Count(ok => ok));
        Assert.Equal(0, await banco.EstoqueAsync(produto.Id));
    }

    [FactComBanco]
    public async Task Limita_pedidos_pendentes_por_cliente()
    {
        var cliente = await banco.CriarClienteAsync();
        var produto = await banco.CriarProdutoAsync(estoque: 10);
        var servico = Servico();
        for (var i = 0; i < 3; i++)
            await servico.CriarAsync(cliente.Id, [new ItemCarrinho(produto.Id, 1)], Endereco(), "pac");

        await Assert.ThrowsAsync<RegraDePedidoException>(() =>
            servico.CriarAsync(cliente.Id, [new ItemCarrinho(produto.Id, 1)], Endereco(), "pac"));

        // Pedido lançado pelo painel não entra no limite
        await servico.CriarAsync(cliente.Id, [new ItemCarrinho(produto.Id, 1)], Endereco(), "pac", criadoPor: "admin");
    }

    [FactComBanco]
    public async Task Frete_invalido_e_rejeitado()
    {
        var cliente = await banco.CriarClienteAsync();
        var produto = await banco.CriarProdutoAsync(estoque: 3);

        await Assert.ThrowsAsync<RegraDePedidoException>(() =>
            Servico().CriarAsync(cliente.Id, [new ItemCarrinho(produto.Id, 1)], Endereco(), "frete-inventado"));
    }

    [FactComBanco]
    public async Task Webhook_aprovado_marca_pedido_como_pago_com_desconto_pix()
    {
        var cliente = await banco.CriarClienteAsync();
        var produto = await banco.CriarProdutoAsync(estoque: 3, preco: 100m);
        var servico = Servico();
        var pedido = await servico.CriarAsync(cliente.Id, [new ItemCarrinho(produto.Id, 1)], Endereco(), "sedex");

        var pagamento = await servico.IniciarPagamentoAsync(pedido.Id, MetodoPagamento.Pix);
        Assert.Equal(pedido.Total - 5m, pagamento.Valor);
        Assert.NotNull(pagamento.PixCopiaECola);

        var encontrado = await servico.ProcessarNotificacaoAsync(new NotificacaoPagamento("evt-1", pagamento.IdExterno!, StatusPagamento.Aprovado, pagamento.Valor));

        Assert.True(encontrado);
        await using var db = banco.CreateDbContext();
        var salvo = await db.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedido.Id);
        Assert.Equal(StatusPedido.Pago, salvo.Status);
        Assert.Equal(StatusPagamento.Aprovado, salvo.Pagamentos.Single().Status);
    }

    [FactComBanco]
    public async Task Webhook_repetido_e_processado_uma_vez()
    {
        var cliente = await banco.CriarClienteAsync();
        var produto = await banco.CriarProdutoAsync(estoque: 3);
        var servico = Servico();
        var pedido = await servico.CriarAsync(cliente.Id, [new ItemCarrinho(produto.Id, 1)], Endereco(), "pac");
        var pagamento = await servico.IniciarPagamentoAsync(pedido.Id, MetodoPagamento.Pix);
        var notificacao = new NotificacaoPagamento("evt-repetido", pagamento.IdExterno!, StatusPagamento.Aprovado);

        await servico.ProcessarNotificacaoAsync(notificacao);
        await servico.ProcessarNotificacaoAsync(notificacao);
        await servico.ProcessarNotificacaoAsync(notificacao);

        await using var db = banco.CreateDbContext();
        var registros = await db.Auditoria.CountAsync(a => a.Acao == "pagamento.notificacao" && a.EntidadeId == pedido.Id.ToString());
        Assert.Equal(1, registros);
    }

    [FactComBanco]
    public async Task Pagamento_de_valor_antigo_vira_alerta_e_nao_confirma()
    {
        var cliente = await banco.CriarClienteAsync();
        var a = await banco.CriarProdutoAsync(estoque: 5);
        var b = await banco.CriarProdutoAsync(estoque: 5);
        var servico = Servico();
        var pedido = await servico.CriarAsync(cliente.Id, [new ItemCarrinho(a.Id, 1)], Endereco(), "pac", criadoPor: "admin");
        var pixAntigo = await servico.IniciarPagamentoAsync(pedido.Id, MetodoPagamento.Pix);

        await servico.AdicionarItemAsync(pedido.Id, b.Id, 1);
        await servico.ProcessarNotificacaoAsync(new NotificacaoPagamento("evt-antigo", pixAntigo.IdExterno!, StatusPagamento.Aprovado, pixAntigo.Valor));

        await using var db = banco.CreateDbContext();
        var salvo = await db.Pedidos.SingleAsync(p => p.Id == pedido.Id);
        Assert.Equal(StatusPedido.AguardandoPagamento, salvo.Status);
        Assert.True(await db.Auditoria.AnyAsync(x => x.Acao == "pagamento.divergente" && x.EntidadeId == pedido.Id.ToString()));
    }

    [FactComBanco]
    public async Task Webhook_de_pagamento_desconhecido_retorna_falso()
    {
        Assert.False(await Servico().ProcessarNotificacaoAsync(new NotificacaoPagamento("x", "nao-existe", StatusPagamento.Aprovado)));
    }

    [FactComBanco]
    public async Task Cancelar_devolve_estoque()
    {
        var cliente = await banco.CriarClienteAsync();
        var produto = await banco.CriarProdutoAsync(estoque: 4);
        var servico = Servico();
        var pedido = await servico.CriarAsync(cliente.Id, [new ItemCarrinho(produto.Id, 3)], Endereco(), "pac");

        await servico.CancelarAsync(pedido.Id, "teste");

        Assert.Equal(4, await banco.EstoqueAsync(produto.Id));
    }

    [FactComBanco]
    public async Task Pedidos_vencidos_sao_cancelados_e_o_estoque_volta()
    {
        var cliente = await banco.CriarClienteAsync();
        var produto = await banco.CriarProdutoAsync(estoque: 4);
        var servico = Servico();
        var vencido = await servico.CriarAsync(cliente.Id, [new ItemCarrinho(produto.Id, 2)], Endereco(), "pac");
        var pago = await servico.CriarAsync(cliente.Id, [new ItemCarrinho(produto.Id, 1)], Endereco(), "pac");
        await servico.RegistrarPagamentoManualAsync(pago.Id, MetodoPagamento.Pix);

        relogio.Agora = relogio.Agora.AddHours(3);
        await servico.ExpirarVencidosAsync();

        await using var db = banco.CreateDbContext();
        Assert.Equal(StatusPedido.Cancelado, (await db.Pedidos.SingleAsync(p => p.Id == vencido.Id)).Status);
        Assert.Equal(StatusPedido.Pago, (await db.Pedidos.SingleAsync(p => p.Id == pago.Id)).Status);
        Assert.Equal(3, await banco.EstoqueAsync(produto.Id));
    }

    [FactComBanco]
    public async Task Edicao_simultanea_do_mesmo_pedido_e_detectada()
    {
        var cliente = await banco.CriarClienteAsync();
        var produto = await banco.CriarProdutoAsync(estoque: 4);
        var pedido = await Servico().CriarAsync(cliente.Id, [new ItemCarrinho(produto.Id, 1)], Endereco(), "pac");

        await using var db1 = banco.CreateDbContext();
        await using var db2 = banco.CreateDbContext();
        var copia1 = await db1.Pedidos.SingleAsync(p => p.Id == pedido.Id);
        var copia2 = await db2.Pedidos.SingleAsync(p => p.Id == pedido.Id);

        copia1.Observacao = "primeira";
        await db1.SaveChangesAsync();
        copia2.Observacao = "segunda";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }

    [FactComBanco]
    public async Task Editar_quantidade_ajusta_estoque_e_remove_item()
    {
        var cliente = await banco.CriarClienteAsync();
        var a = await banco.CriarProdutoAsync(estoque: 10);
        var b = await banco.CriarProdutoAsync(estoque: 10);
        var servico = Servico();
        var pedido = await servico.CriarAsync(cliente.Id, [new ItemCarrinho(a.Id, 2), new ItemCarrinho(b.Id, 1)], Endereco(), "pac");

        await servico.AlterarQuantidadeAsync(pedido.Id, a.Id, 5);
        await servico.AlterarQuantidadeAsync(pedido.Id, b.Id, 0);

        Assert.Equal(5, await banco.EstoqueAsync(a.Id));
        Assert.Equal(10, await banco.EstoqueAsync(b.Id));
        await using var db = banco.CreateDbContext();
        var salvo = await db.Pedidos.Include(p => p.Itens).SingleAsync(p => p.Id == pedido.Id);
        Assert.Single(salvo.Itens);
    }

    [FactComBanco]
    public async Task Pagamento_manual_confirma_pedido()
    {
        var cliente = await banco.CriarClienteAsync();
        var produto = await banco.CriarProdutoAsync(estoque: 2);
        var servico = Servico();
        var pedido = await servico.CriarAsync(cliente.Id, [new ItemCarrinho(produto.Id, 1)], Endereco(), FreteSimulado.CodigoRetirada, criadoPor: "admin");

        await servico.RegistrarPagamentoManualAsync(pedido.Id, MetodoPagamento.Pix);

        await using var db = banco.CreateDbContext();
        var salvo = await db.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedido.Id);
        Assert.Equal(StatusPedido.Pago, salvo.Status);
        Assert.Equal("manual", salvo.Pagamentos.Single().Provedor);
        Assert.Equal("admin", salvo.CriadoPor);
        Assert.Equal(relogio.Agora.UtcDateTime.AddHours(loja.PrazoPagamentoPainelHoras), salvo.PagarAte);
    }
}
