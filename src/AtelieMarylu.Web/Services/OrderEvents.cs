namespace AtelieMarylu.Web.Services;

// Avisa as telas abertas que um pedido mudou (ex.: o webhook confirmou o PIX).
// Como o Blazor Server mantém uma conexão com o navegador, a tela do cliente se atualiza sozinha.
// Funciona com um servidor; com vários, troque por um backplane (Redis, Postgres LISTEN/NOTIFY).
public class OrderEvents
{
    public event Action<int>? OrderChanged;

    public void Notify(int orderId) => OrderChanged?.Invoke(orderId);
}
