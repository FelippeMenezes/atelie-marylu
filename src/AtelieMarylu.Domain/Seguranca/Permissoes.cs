namespace AtelieMarylu.Domain.Seguranca;

// Cada permissão vira um claim do tipo "permissao", concedido direto ao usuário
// (individual) ou ao grupo (role claim). O Admin tem todas automaticamente.
public static class Permissoes
{
    public const string TipoClaim = "permissao";

    public const string ProdutosGerenciar = "produtos.gerenciar";
    public const string PedidosVer = "pedidos.ver";
    public const string PedidosCriar = "pedidos.criar";
    public const string PedidosEditar = "pedidos.editar";
    public const string PedidosCancelar = "pedidos.cancelar";
    public const string ClientesVer = "clientes.ver";

    public static readonly IReadOnlyDictionary<string, string> Todas = new Dictionary<string, string>
    {
        [ProdutosGerenciar] = "Criar e editar produtos",
        [PedidosVer] = "Ver pedidos",
        [PedidosCriar] = "Criar pedidos pelo painel",
        [PedidosEditar] = "Editar pedidos e mudar status",
        [PedidosCancelar] = "Cancelar pedidos",
        [ClientesVer] = "Ver clientes cadastrados",
    };

    // Política que libera o painel para quem tem qualquer permissão
    public const string AcessarPainel = "painel.acessar";
}
