namespace AtelieMarylu.Domain.Pagamentos;

public record DadosCobranca(int PedidoId, string CodigoPedido, decimal Valor, MetodoPagamento Metodo, string EmailCliente, string NomeCliente, string Descricao, DateTime ExpiraEm);
