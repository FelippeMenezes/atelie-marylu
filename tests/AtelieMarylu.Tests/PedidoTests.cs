using AtelieMarylu.Domain;
using AtelieMarylu.Domain.Frete;
using AtelieMarylu.Domain.Pagamentos;
using AtelieMarylu.Domain.Pedidos;

namespace AtelieMarylu.Tests;

public class PedidoTests
{
    private static readonly DateTime Agora = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    private static Pedido NovoPedido(decimal frete = 20m, decimal descontoPix = 0) => new(
        "cliente-1",
        [new ItemPedido(1, "Bolsa", 89.90m, 2), new ItemPedido(2, "Necessaire", 39.90m, 1)],
        new EnderecoEntrega { Cep = "20000000", Logradouro = "Rua A", Numero = "1", Bairro = "Centro", Cidade = "Rio de Janeiro", Uf = "RJ" },
        new OpcaoFrete("pac", "PAC", frete, 8),
        Agora.AddHours(2),
        descontoPix);

    [Fact]
    public void Total_soma_itens_e_frete()
    {
        var pedido = NovoPedido();

        Assert.Equal(219.70m, pedido.Subtotal);
        Assert.Equal(239.70m, pedido.Total);
        Assert.Equal(StatusPedido.AguardandoPagamento, pedido.Status);
    }

    [Fact]
    public void Codigo_publico_tem_formato_amigavel()
    {
        var codigo = NovoPedido().Codigo;

        Assert.Matches("^AML-[2-9A-HJ-NP-Z]{6}$", codigo);
        Assert.NotEqual(codigo, NovoPedido().Codigo);
    }

    [Fact]
    public void Desconto_pix_vale_so_sobre_os_produtos()
    {
        var pedido = NovoPedido(frete: 20m, descontoPix: 5);

        Assert.Equal(10.99m, pedido.DescontoPix);                          // 5% de 219,70 = 10,985 -> 10,99
        Assert.Equal(228.71m, pedido.ValorPara(MetodoPagamento.Pix));      // 219,70 - 10,99 + 20
        Assert.Equal(239.70m, pedido.ValorPara(MetodoPagamento.CartaoCredito));
    }

    [Fact]
    public void Pagamento_usa_o_valor_do_metodo()
    {
        var pedido = NovoPedido(descontoPix: 5);

        Assert.Equal(228.71m, pedido.IniciarPagamento(MetodoPagamento.Pix, "simulado").Valor);
        Assert.Equal(239.70m, pedido.IniciarPagamento(MetodoPagamento.CartaoCredito, "simulado").Valor);
    }

    [Fact]
    public void Pedido_sem_itens_nao_pode_ser_criado()
    {
        Assert.Throws<RegraDePedidoException>(() => new Pedido("c", [], new EnderecoEntrega(), new OpcaoFrete("x", "X", 0, null), Agora));
    }

    [Fact]
    public void Confirmar_pagamento_muda_status_para_pago()
    {
        var pedido = NovoPedido();
        var pagamento = pedido.IniciarPagamento(MetodoPagamento.Pix, "simulado");

        pedido.ConfirmarPagamento(pagamento);

        Assert.Equal(StatusPedido.Pago, pedido.Status);
        Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
        Assert.NotNull(pedido.PagoEm);
    }

    [Fact]
    public void Webhook_repetido_nao_gera_erro()
    {
        var pedido = NovoPedido();
        var pagamento = pedido.IniciarPagamento(MetodoPagamento.Pix, "simulado");
        pedido.ConfirmarPagamento(pagamento);

        pedido.ConfirmarPagamento(pagamento);

        Assert.Equal(StatusPedido.Pago, pedido.Status);
    }

    [Fact]
    public void Novo_pagamento_cancela_o_pendente_anterior()
    {
        var pedido = NovoPedido();
        var primeiro = pedido.IniciarPagamento(MetodoPagamento.CartaoCredito, "simulado");

        var segundo = pedido.IniciarPagamento(MetodoPagamento.Pix, "simulado");

        Assert.Equal(StatusPagamento.Cancelado, primeiro.Status);
        Assert.Equal(StatusPagamento.Pendente, segundo.Status);
    }

    [Fact]
    public void Editar_itens_invalida_o_pagamento_pendente()
    {
        var pedido = NovoPedido();
        var pagamento = pedido.IniciarPagamento(MetodoPagamento.Pix, "simulado");

        pedido.AlterarQuantidade(1, 1);

        Assert.Equal(StatusPagamento.Cancelado, pagamento.Status);
    }

    [Fact]
    public void Pagar_o_valor_antigo_depois_da_edicao_e_recusado()
    {
        var pedido = NovoPedido();
        var pagamentoAntigo = pedido.IniciarPagamento(MetodoPagamento.Pix, "simulado");
        pedido.AdicionarItem(new ItemPedido(3, "Porta-moedas", 24.90m, 1));

        var erro = Assert.Throws<RegraDePedidoException>(() => pedido.ConfirmarPagamento(pagamentoAntigo));

        Assert.Contains("diferente", erro.Message);
        Assert.Equal(StatusPedido.AguardandoPagamento, pedido.Status);
    }

    [Fact]
    public void Valor_pago_informado_pelo_provedor_e_conferido()
    {
        var pedido = NovoPedido();
        var pagamento = pedido.IniciarPagamento(MetodoPagamento.Pix, "simulado");

        Assert.Throws<RegraDePedidoException>(() => pedido.ConfirmarPagamento(pagamento, valorPago: 1m));
        pedido.ConfirmarPagamento(pagamento, valorPago: pagamento.Valor);
        Assert.Equal(StatusPedido.Pago, pedido.Status);
    }

    [Fact]
    public void Prazo_de_pagamento_vence()
    {
        var pedido = NovoPedido();

        Assert.False(pedido.PrazoDePagamentoVencido(Agora.AddHours(1)));
        Assert.True(pedido.PrazoDePagamentoVencido(Agora.AddHours(3)));
    }

    [Fact]
    public void Fluxo_completo_ate_entrega()
    {
        var pedido = NovoPedido();
        pedido.ConfirmarPagamento(pedido.IniciarPagamento(MetodoPagamento.Pix, "simulado"));

        pedido.IniciarPreparo();
        pedido.MarcarEnviado(" BR123 ");
        pedido.MarcarEntregue();

        Assert.Equal(StatusPedido.Entregue, pedido.Status);
        Assert.Equal("BR123", pedido.CodigoRastreio);
        Assert.False(pedido.PrazoDePagamentoVencido(Agora.AddDays(10)));
    }

    [Fact]
    public void Nao_envia_pedido_sem_pagamento()
    {
        var pedido = NovoPedido();

        Assert.Throws<RegraDePedidoException>(() => pedido.MarcarEnviado("BR1"));
    }

    [Fact]
    public void Pedido_enviado_nao_pode_ser_cancelado()
    {
        var pedido = NovoPedido();
        pedido.ConfirmarPagamento(pedido.IniciarPagamento(MetodoPagamento.Pix, "simulado"));
        pedido.MarcarEnviado(null);

        Assert.False(pedido.PodeCancelar);
        Assert.Throws<RegraDePedidoException>(() => pedido.Cancelar());
    }

    [Fact]
    public void Cancelar_cancela_pagamento_pendente_e_guarda_motivo()
    {
        var pedido = NovoPedido();
        var pagamento = pedido.IniciarPagamento(MetodoPagamento.Pix, "simulado");

        pedido.Cancelar(" Cliente desistiu ");

        Assert.Equal(StatusPedido.Cancelado, pedido.Status);
        Assert.Equal(StatusPagamento.Cancelado, pagamento.Status);
        Assert.Equal("Cliente desistiu", pedido.MotivoCancelamento);
    }

    [Fact]
    public void Pagamento_em_pedido_cancelado_e_rejeitado()
    {
        var pedido = NovoPedido();
        var pagamento = pedido.IniciarPagamento(MetodoPagamento.Pix, "simulado");
        pedido.Cancelar();

        Assert.Throws<RegraDePedidoException>(() => pedido.ConfirmarPagamento(pagamento));
    }

    [Fact]
    public void Alterar_quantidade_retorna_diferenca_para_o_estoque()
    {
        var pedido = NovoPedido();

        Assert.Equal(3, pedido.AlterarQuantidade(1, 5));
        Assert.Equal(-1, pedido.AlterarQuantidade(2, 0));
        Assert.Single(pedido.Itens);
    }

    [Fact]
    public void Nao_remove_o_ultimo_item()
    {
        var pedido = NovoPedido();
        pedido.AlterarQuantidade(2, 0);

        Assert.Throws<RegraDePedidoException>(() => pedido.AlterarQuantidade(1, 0));
    }

    [Fact]
    public void Itens_nao_mudam_depois_do_pagamento()
    {
        var pedido = NovoPedido();
        pedido.ConfirmarPagamento(pedido.IniciarPagamento(MetodoPagamento.Pix, "simulado"));

        Assert.Throws<RegraDePedidoException>(() => pedido.AlterarQuantidade(1, 1));
        Assert.Throws<RegraDePedidoException>(() => pedido.AdicionarItem(new ItemPedido(3, "X", 1m, 1)));
    }

    [Fact]
    public void Adicionar_item_existente_soma_quantidade()
    {
        var pedido = NovoPedido();

        pedido.AdicionarItem(new ItemPedido(1, "Bolsa", 89.90m, 1));

        Assert.Equal(3, pedido.Itens.First(i => i.ProdutoId == 1).Quantidade);
    }

    [Theory]
    [InlineData("Bolsa de Tecido Floral", "bolsa-de-tecido-floral")]
    [InlineData("Necessaire média (22 x 14)", "necessaire-media-22-x-14")]
    [InlineData("  Ação & Coração!! ", "acao-coracao")]
    public void Slug_para_url_amigavel(string nome, string esperado)
    {
        Assert.Equal(esperado, Texto.Slug(nome));
    }
}
