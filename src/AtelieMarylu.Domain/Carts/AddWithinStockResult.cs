namespace AtelieMarylu.Domain.Carts;

// Quantas unidades entraram no carrinho e por quê. A tela só traduz o motivo em mensagem:
// o motivo vem junto com o número para ninguém precisar adivinhar o que limitou (AML-0038).
public readonly record struct AddWithinStockResult(int Added, AddWithinStockOutcome Outcome);

public enum AddWithinStockOutcome
{
    // Entrou tudo o que a cliente pediu
    Added,

    // Esgotado (ou saiu da loja): nada entrou
    SoldOut,

    // O estoque não dava para tudo: entrou o que havia (Added > 0)
    Partial,

    // O carrinho já tem todas as unidades em estoque: nada entrou
    CartHasAllStock,

    // O estoque caiu abaixo do que já está no carrinho: nada entrou, e o carrinho precisa ser ajustado
    CartAboveStock,

    // Quem limitou foi o máximo por item (MaxQuantityPerItem), não o estoque. Added pode ser 0 ou mais
    LimitPerItem,
}
