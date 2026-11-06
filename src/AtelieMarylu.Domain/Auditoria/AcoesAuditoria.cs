namespace AtelieMarylu.Domain.Auditoria;

// Nomes das ações registradas na auditoria. Ficam em constantes para um erro de digitação
// virar erro de compilação, e não um registro que nenhuma tela encontra.
public static class AcoesAuditoria
{
    public const string PedidoCriado = "pedido.criado";
    public const string PedidoCancelado = "pedido.cancelado";
    public const string PedidoExpirado = "pedido.expirado";
    public const string PedidoItens = "pedido.itens";
    public const string PedidoEntrega = "pedido.entrega";
    public const string PedidoPreparo = "pedido.preparo";
    public const string PedidoEnviado = "pedido.enviado";
    public const string PedidoEntregue = "pedido.entregue";

    public const string PagamentoNotificacao = "pagamento.notificacao";
    public const string PagamentoDivergente = "pagamento.divergente";
    public const string PagamentoManual = "pagamento.manual";

    public const string ProdutoCriado = "produto.criado";
    public const string ProdutoEditado = "produto.editado";

    public const string AcessoAlterado = "acesso.alterado";
    public const string GrupoCriado = "grupo.criado";
    public const string GrupoPermissoes = "grupo.permissoes";
    public const string GrupoExcluido = "grupo.excluido";
}
