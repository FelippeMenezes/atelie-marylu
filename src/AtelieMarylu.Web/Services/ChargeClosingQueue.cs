using System.Threading.Channels;

namespace AtelieMarylu.Web.Services;

// Fila em memória das cobranças a fechar no provedor. Quem enfileira (OrderService) não espera o provedor.
// Limitações conscientes, ok para um servidor só: se a loja reiniciar com itens na fila, eles se perdem, e o
// checkout vence sozinho no prazo do pedido (um pagamento nele vira "pagamento divergente" no painel).
public class ChargeClosingQueue(ILogger<ChargeClosingQueue> logger) : IChargeCloser
{
    // Limitada: um volume anormal não pode crescer a memória sem fim
    private readonly Channel<ChargeToClose> channel = Channel.CreateBounded<ChargeToClose>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public ChannelReader<ChargeToClose> Reader => channel.Reader;

    public void Enqueue(ChargeToClose charge)
    {
        if (!channel.Writer.TryWrite(charge))
            logger.LogError("ALERTA: fila de inativação cheia; a cobrança {Referencia} do pedido {PedidoId} não foi fechada no provedor e aceita pagamento até {Prazo}.",
                charge.ProviderReference, charge.OrderId, charge.PayBy);
    }
}
