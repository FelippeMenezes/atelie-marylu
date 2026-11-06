namespace AtelieMarylu.Web.Services;

// Dados digitados no formulário do painel para criar ou editar um produto.
// EstoqueAoAbrir é o estoque que o formulário mostrou ao abrir: a diferença para Estoque é o ajuste feito pelo admin.
public record DadosProduto(
    string Nome,
    string Descricao,
    decimal Preco,
    int Estoque,
    bool Ativo,
    int? PrazoProducaoDias,
    int PesoGramas,
    int AlturaCm,
    int LarguraCm,
    int ComprimentoCm,
    int EstoqueAoAbrir = 0);
