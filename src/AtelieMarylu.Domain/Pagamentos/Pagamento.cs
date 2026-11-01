namespace AtelieMarylu.Domain.Pagamentos;

public enum MetodoPagamento
{
    Pix = 1,
    CartaoCredito = 2,
}

public enum StatusPagamento
{
    Pendente = 1,
    Aprovado = 2,
    Recusado = 3,
    Cancelado = 4,
    Estornado = 5,
}

// Uma tentativa de pagamento de um pedido. Um pedido pode ter várias
// (ex.: cartão recusado e depois pago com PIX).
public class Pagamento
{
    private Pagamento() { } // EF Core

    public Pagamento(MetodoPagamento metodo, decimal valor, string provedor)
    {
        Metodo = metodo;
        Valor = valor;
        Provedor = provedor;
    }

    public int Id { get; private set; }
    public int PedidoId { get; private set; }
    public MetodoPagamento Metodo { get; private set; }
    public decimal Valor { get; private set; }
    public StatusPagamento Status { get; private set; } = StatusPagamento.Pendente;

    // Quem processou: "simulado", "manual" e, no futuro, "mercadopago"
    public string Provedor { get; private set; } = "";

    // Id do pagamento no provedor. É por ele que o webhook encontra este registro.
    public string? IdExterno { get; private set; }

    // Para onde mandar o cliente (checkout do provedor) ou o código PIX copia e cola
    public string? UrlPagamento { get; private set; }
    public string? PixCopiaECola { get; private set; }

    public DateTime CriadoEm { get; private set; } = DateTime.UtcNow;
    public DateTime? AtualizadoEm { get; private set; }

    public void RegistrarInicio(string idExterno, string? urlPagamento, string? pixCopiaECola)
    {
        IdExterno = idExterno;
        UrlPagamento = urlPagamento;
        PixCopiaECola = pixCopiaECola;
        AtualizadoEm = DateTime.UtcNow;
    }

    public void AtualizarStatus(StatusPagamento novo)
    {
        Status = novo;
        AtualizadoEm = DateTime.UtcNow;
    }
}
