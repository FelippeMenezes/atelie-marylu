using System.Security.Cryptography;
using AtelieMarylu.Domain.Carrinho;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace AtelieMarylu.Web.Services;

// O carrinho fica no navegador (localStorage, criptografado pelo ASP.NET), então funciona
// sem login e sobrevive ao cadastro/login no meio da compra.
// Um por circuito Blazor: o cabeçalho e as páginas compartilham a mesma instância.
public class CarrinhoService(ProtectedLocalStorage storage, ILogger<CarrinhoService> logger)
{
    private const string Chave = "carrinho";
    private Carrinho _carrinho = new();
    private bool _carregado;

    public event Action? Mudou;

    public Carrinho Atual => _carrinho;
    public bool Carregado => _carregado;

    // Só pode ser chamado depois da primeira renderização (OnAfterRenderAsync):
    // durante o prerender o servidor ainda não fala com o navegador.
    public async Task CarregarAsync()
    {
        if (_carregado) return;
        try
        {
            var salvo = await storage.GetAsync<List<ItemCarrinho>>(Chave);
            _carrinho = new Carrinho(salvo.Success && salvo.Value is not null ? salvo.Value : []);
        }
        catch (CryptographicException ex)
        {
            // Acontece se as chaves de proteção mudarem (ex.: outro servidor). Começa um carrinho novo.
            logger.LogWarning(ex, "Carrinho salvo não pôde ser lido; iniciando vazio.");
            _carrinho = new Carrinho();
        }
        _carregado = true;
        Mudou?.Invoke();
    }

    public Task AdicionarAsync(int produtoId, int quantidade = 1) => AlterarAsync(c => c.Adicionar(produtoId, quantidade));

    public Task AlterarQuantidadeAsync(int produtoId, int quantidade) => AlterarAsync(c => c.AlterarQuantidade(produtoId, quantidade));

    public Task RemoverAsync(int produtoId) => AlterarAsync(c => c.Remover(produtoId));

    public Task LimparAsync() => AlterarAsync(c => c.Limpar());

    private async Task AlterarAsync(Action<Carrinho> alteracao)
    {
        await CarregarAsync();
        alteracao(_carrinho);
        await storage.SetAsync(Chave, _carrinho.Itens.ToList());
        Mudou?.Invoke();
    }
}
