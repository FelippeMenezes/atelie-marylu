namespace AtelieMarylu.Domain.Carts;

// O carrinho guarda só ProductId e Quantidade. Preço e nome são sempre lidos do banco,
// para o cliente não conseguir alterar o valor editando o navegador.
public record CartItem(int ProductId, int Quantity);
