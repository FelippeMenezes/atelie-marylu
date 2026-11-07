namespace AtelieMarylu.Web.Services;

// Dados digitados no formulário do painel para criar ou editar um produto.
// StockWhenOpened é o estoque que o formulário mostrou ao abrir: a diferença para Estoque é o ajuste feito pelo admin.
public record ProductInput(
    string Name,
    string Description,
    decimal Price,
    int Stock,
    bool IsActive,
    int? ProductionDays,
    int WeightGrams,
    int HeightCm,
    int WidthCm,
    int LengthCm,
    int StockWhenOpened = 0);
