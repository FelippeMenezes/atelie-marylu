using AtelieMarylu.Domain.Frete;
using AtelieMarylu.Domain.Pagamentos;

namespace AtelieMarylu.Domain.Pedidos;

public enum StatusPedido
{
    AguardandoPagamento = 1,
    Pago = 2,
    EmPreparo = 3,
    Enviado = 4,
    Entregue = 5,
    Cancelado = 6,
}

public class EnderecoEntrega
{
    public string Cep { get; set; } = "";
    public string Logradouro { get; set; } = "";
    public string Numero { get; set; } = "";
    public string? Complemento { get; set; }
    public string Bairro { get; set; } = "";
    public string Cidade { get; set; } = "";
    public string Uf { get; set; } = "";
}

// Nome e preço são copiados do produto no momento da compra:
// se o preço mudar depois, o pedido continua com o valor que o cliente pagou.
public class ItemPedido
{
    private ItemPedido() { } // EF Core

    public ItemPedido(int produtoId, string nomeProduto, decimal precoUnitario, int quantidade)
    {
        if (quantidade <= 0) throw new RegraDePedidoException("A quantidade deve ser maior que zero.");
        ProdutoId = produtoId;
        NomeProduto = nomeProduto;
        PrecoUnitario = precoUnitario;
        Quantidade = quantidade;
    }

    public int Id { get; private set; }
    public int ProdutoId { get; private set; }
    public string NomeProduto { get; private set; } = "";
    public decimal PrecoUnitario { get; private set; }
    public int Quantidade { get; internal set; }
    public decimal Subtotal => PrecoUnitario * Quantidade;
}

public class RegraDePedidoException(string mensagem) : Exception(mensagem);

public class Pedido
{
    private readonly List<ItemPedido> _itens = [];
    private readonly List<Pagamento> _pagamentos = [];

    private Pedido() { } // EF Core

    public Pedido(string clienteId, IEnumerable<ItemPedido> itens, EnderecoEntrega endereco, OpcaoFrete frete,
        DateTime pagarAte, decimal percentualDescontoPix = 0, string? criadoPor = null)
    {
        if (percentualDescontoPix is < 0 or >= 100) throw new RegraDePedidoException("Desconto PIX inválido.");
        ClienteId = clienteId;
        _itens.AddRange(itens);
        if (_itens.Count == 0) throw new RegraDePedidoException("O pedido precisa ter pelo menos um item.");
        Endereco = endereco;
        DefinirFrete(frete);
        PagarAte = pagarAte;
        PercentualDescontoPix = percentualDescontoPix;
        CriadoPor = criadoPor;
    }

    public int Id { get; private set; }

    // Código mostrado ao cliente; o Id interno não aparece para ele
    public string Codigo { get; private set; } = Texto.NovoCodigoPedido();

    public string ClienteId { get; private set; } = "";
    public StatusPedido Status { get; private set; } = StatusPedido.AguardandoPagamento;
    public EnderecoEntrega Endereco { get; private set; } = new();

    public string FreteCodigo { get; private set; } = "";
    public string FreteNome { get; private set; } = "";
    public decimal FreteValor { get; private set; }
    public int? FretePrazoDias { get; private set; }
    public string? CodigoRastreio { get; private set; }

    // Desconto sobre os produtos (não sobre o frete) quando o pagamento é por PIX
    public decimal PercentualDescontoPix { get; private set; }

    public string? Observacao { get; set; }

    // Preenchido quando um funcionário/admin cria o pedido pelo painel
    public string? CriadoPor { get; private set; }

    public DateTime CriadoEm { get; private set; } = DateTime.UtcNow;

    // Sem pagamento até aqui, o pedido é cancelado e o estoque volta para a loja
    public DateTime PagarAte { get; private set; }
    public DateTime? PagoEm { get; private set; }
    public DateTime? CanceladoEm { get; private set; }
    public string? MotivoCancelamento { get; private set; }

    public IReadOnlyList<ItemPedido> Itens => _itens;
    public IReadOnlyList<Pagamento> Pagamentos => _pagamentos;

    public decimal Subtotal => _itens.Sum(i => i.Subtotal);
    public decimal Total => Subtotal + FreteValor;
    public decimal DescontoPix => Math.Round(Subtotal * PercentualDescontoPix / 100m, 2, MidpointRounding.AwayFromZero);

    public decimal ValorPara(MetodoPagamento metodo) => metodo == MetodoPagamento.Pix ? Total - DescontoPix : Total;

    public bool PodeAlterarItens => Status == StatusPedido.AguardandoPagamento;
    public bool PodeCancelar => Status is StatusPedido.AguardandoPagamento or StatusPedido.Pago or StatusPedido.EmPreparo;
    public bool PodeAlterarEntrega => Status is not (StatusPedido.Enviado or StatusPedido.Entregue or StatusPedido.Cancelado);
    public bool PrazoDePagamentoVencido(DateTime agoraUtc) => Status == StatusPedido.AguardandoPagamento && agoraUtc > PagarAte;

    public void DefinirFrete(OpcaoFrete frete)
    {
        if (!PodeAlterarItens) throw new RegraDePedidoException("O frete só pode ser alterado antes do pagamento.");
        if (frete.Valor < 0) throw new RegraDePedidoException("O frete não pode ser negativo.");
        var mudouValor = frete.Valor != FreteValor;
        FreteCodigo = frete.Codigo;
        FreteNome = frete.Nome;
        FreteValor = frete.Valor;
        FretePrazoDias = frete.PrazoDias;
        if (mudouValor) InvalidarPagamentosPendentes();
    }

    public void AlterarEndereco(EnderecoEntrega endereco)
    {
        if (!PodeAlterarEntrega) throw new RegraDePedidoException("O endereço não pode mais ser alterado.");
        Endereco = endereco;
    }

    // Retorna a diferença de quantidade (positiva = precisa baixar estoque; negativa = devolver)
    public int AlterarQuantidade(int produtoId, int novaQuantidade)
    {
        if (!PodeAlterarItens) throw new RegraDePedidoException("Os itens só podem ser alterados antes do pagamento.");
        var item = _itens.FirstOrDefault(i => i.ProdutoId == produtoId)
            ?? throw new RegraDePedidoException("Item não encontrado no pedido.");

        int diferenca;
        if (novaQuantidade <= 0)
        {
            if (_itens.Count == 1) throw new RegraDePedidoException("O pedido precisa ter pelo menos um item. Cancele o pedido.");
            _itens.Remove(item);
            diferenca = -item.Quantidade;
        }
        else
        {
            diferenca = novaQuantidade - item.Quantidade;
            item.Quantidade = novaQuantidade;
        }

        if (diferenca != 0) InvalidarPagamentosPendentes();
        return diferenca;
    }

    public void AdicionarItem(ItemPedido novo)
    {
        if (!PodeAlterarItens) throw new RegraDePedidoException("Os itens só podem ser alterados antes do pagamento.");
        var existente = _itens.FirstOrDefault(i => i.ProdutoId == novo.ProdutoId);
        if (existente is not null) existente.Quantidade += novo.Quantidade;
        else _itens.Add(novo);
        InvalidarPagamentosPendentes();
    }

    public Pagamento IniciarPagamento(MetodoPagamento metodo, string provedor)
    {
        if (Status != StatusPedido.AguardandoPagamento)
            throw new RegraDePedidoException("Este pedido não está aguardando pagamento.");

        InvalidarPagamentosPendentes();
        var pagamento = new Pagamento(metodo, ValorPara(metodo), provedor);
        _pagamentos.Add(pagamento);
        return pagamento;
    }

    // Chamado pelo webhook do provedor (ou pelo admin, para pagamento manual)
    public void ConfirmarPagamento(Pagamento pagamento, decimal? valorPago = null)
    {
        if (!_pagamentos.Contains(pagamento)) throw new RegraDePedidoException("Pagamento não pertence a este pedido.");
        // Webhooks podem chegar repetidos: confirmar duas vezes não deve dar erro
        if (pagamento.Status == StatusPagamento.Aprovado && Status != StatusPedido.AguardandoPagamento) return;
        if (Status != StatusPedido.AguardandoPagamento)
            throw new RegraDePedidoException($"Pedido está {Descrever(Status)} e não pode receber pagamento.");

        // Protege contra pagar um valor antigo (ex.: PIX gerado antes de o pedido ser editado)
        var esperado = ValorPara(pagamento.Metodo);
        if (pagamento.Valor != esperado || (valorPago is not null && valorPago != pagamento.Valor))
            throw new RegraDePedidoException($"Valor pago ({(valorPago ?? pagamento.Valor):C}) diferente do valor do pedido ({esperado:C}).");

        pagamento.AtualizarStatus(StatusPagamento.Aprovado);
        Status = StatusPedido.Pago;
        PagoEm = DateTime.UtcNow;
    }

    public void IniciarPreparo()
    {
        if (Status != StatusPedido.Pago) throw new RegraDePedidoException("Só pedidos pagos podem entrar em preparo.");
        Status = StatusPedido.EmPreparo;
    }

    public void MarcarEnviado(string? codigoRastreio)
    {
        if (Status is not (StatusPedido.Pago or StatusPedido.EmPreparo))
            throw new RegraDePedidoException("Só pedidos pagos ou em preparo podem ser enviados.");
        CodigoRastreio = string.IsNullOrWhiteSpace(codigoRastreio) ? null : codigoRastreio.Trim();
        Status = StatusPedido.Enviado;
    }

    public void MarcarEntregue()
    {
        if (Status != StatusPedido.Enviado) throw new RegraDePedidoException("Só pedidos enviados podem ser marcados como entregues.");
        Status = StatusPedido.Entregue;
    }

    public void Cancelar(string? motivo = null)
    {
        if (!PodeCancelar) throw new RegraDePedidoException("Este pedido não pode mais ser cancelado.");
        InvalidarPagamentosPendentes();
        Status = StatusPedido.Cancelado;
        CanceladoEm = DateTime.UtcNow;
        MotivoCancelamento = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        // Pagamento já aprovado precisa de estorno no provedor; isso entra na fase de pagamentos.
    }

    private void InvalidarPagamentosPendentes()
    {
        foreach (var pendente in _pagamentos.Where(p => p.Status == StatusPagamento.Pendente))
            pendente.AtualizarStatus(StatusPagamento.Cancelado);
    }

    public static string Descrever(StatusPedido status) => status switch
    {
        StatusPedido.AguardandoPagamento => "Aguardando pagamento",
        StatusPedido.Pago => "Pago",
        StatusPedido.EmPreparo => "Em preparo",
        StatusPedido.Enviado => "Enviado",
        StatusPedido.Entregue => "Entregue",
        StatusPedido.Cancelado => "Cancelado",
        _ => status.ToString(),
    };
}
