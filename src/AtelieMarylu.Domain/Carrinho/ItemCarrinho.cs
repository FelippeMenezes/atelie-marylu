namespace AtelieMarylu.Domain.Carrinho;

// O carrinho guarda só ProdutoId e Quantidade. Preço e nome são sempre lidos do banco,
// para o cliente não conseguir alterar o valor editando o navegador.
public record ItemCarrinho(int ProdutoId, int Quantidade);
