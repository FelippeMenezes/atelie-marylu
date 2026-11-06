namespace AtelieMarylu.Web.Services;

// Dados digitados no formulário do painel para criar ou editar um produto
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
    int ComprimentoCm);
