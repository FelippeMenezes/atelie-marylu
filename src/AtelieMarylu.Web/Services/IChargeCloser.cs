namespace AtelieMarylu.Web.Services;

// Uma cobrança aberta no provedor que deixou de valer e precisa ser fechada lá (ex.: o checkout do PagBank).
// PayBy: o prazo do pedido. O checkout vence sozinho nele, então depois disso não há o que fechar.
public record ChargeToClose(string ProviderReference, int OrderId, DateTime PayBy);

// Fecha cobranças no provedor DEPOIS de a alteração já estar gravada. Separado do OrderService para o webhook
// responder rápido ("confirma rápido, processa depois"): na loja, quem faz isso é a fila em segundo plano
// (ChargeClosingQueue + ChargeClosingService); nos testes, uma versão que fecha na hora.
public interface IChargeCloser
{
    void Enqueue(ChargeToClose charge);
}
