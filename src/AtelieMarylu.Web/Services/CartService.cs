using System.Security.Cryptography;
using AtelieMarylu.Domain.Carts;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace AtelieMarylu.Web.Services;

// O carrinho fica no navegador (localStorage, criptografado pelo ASP.NET), então funciona
// sem login e sobrevive ao cadastro/login no meio da compra.
// Um por circuito Blazor: o cabeçalho e as páginas compartilham a mesma instância.
// O localStorage é a fonte da verdade, não a cópia em memória: cada aba tem o seu circuito,
// e outra aba pode ter mudado o carrinho (ex.: esvaziado depois de finalizar um pedido).
// Por isso, toda alteração relê o storage antes de aplicar a mudança e gravar (AML-0037).
// Se o navegador bloquear o localStorage (ex.: "bloquear todos os cookies e dados de sites") ou
// ele estiver cheio, o carrinho passa a valer só em memória até o fim da visita (deste circuito):
// a loja continua funcionando, em vez de o erro derrubar o circuito e travar a página inteira.
// Depois da primeira falha o storage não é mais lido: com ele cheio, a leitura funciona mas a
// gravação não, e reler traria de volta a versão antiga, apagando o que a cliente acabou de fazer.
public class CartService(ProtectedLocalStorage storage, ILogger<CartService> logger)
{
    private const string StorageKey = "cart";
    private Cart _cart = new();
    private bool _loaded;
    private bool _memoryOnly;

    // Dois cliques rápidos na mesma aba intercalariam nos awaits (ler, ler, gravar, gravar)
    // e um apagaria o outro. A trava enfileira as leituras e alterações deste circuito.
    private readonly SemaphoreSlim _lock = new(1, 1);

    public event Action? Changed;

    public Cart Current => _cart;
    public bool IsLoaded => _loaded;

    // Relê o carrinho do navegador. As páginas chamam ao abrir, para não mostrar uma cópia velha.
    // Só pode ser chamado depois da primeira renderização (OnAfterRenderAsync):
    // durante o prerender o servidor ainda não fala com o navegador.
    public async Task LoadAsync()
    {
        // Também passa pela trava: uma leitura que começou antes de uma alteração e terminou depois
        // trocaria o carrinho em memória pela versão velha (o storage ficaria certo, a tela não).
        await _lock.WaitAsync();
        try
        {
            await ReadAsync();
        }
        finally
        {
            _lock.Release();
        }
        Changed?.Invoke();
    }

    public Task AddAsync(int productId, int quantity = 1) => ChangeAsync(c => c.Add(productId, quantity));

    public Task ChangeQuantityAsync(int productId, int quantity) => ChangeAsync(c => c.ChangeQuantity(productId, quantity));

    public Task RemoveAsync(int productId) => ChangeAsync(c => c.Remove(productId));

    public Task RemoveOrderedAsync(IEnumerable<CartItem> ordered) => ChangeAsync(c => c.RemoveOrdered(ordered));

    private async Task ChangeAsync(Action<Cart> change)
    {
        await _lock.WaitAsync();
        try
        {
            await ReadAsync();
            change(_cart);
            await WriteAsync();
        }
        finally
        {
            _lock.Release();
        }
        Changed?.Invoke();
    }

    // Chamar só com a trava (_lock) já obtida.
    private async Task ReadAsync()
    {
        if (_memoryOnly) return;
        try
        {
            var stored = await storage.GetAsync<List<CartItem>>(StorageKey);
            _cart = new Cart(stored.Success && stored.Value is not null ? stored.Value : []);
        }
        catch (JSException ex)
        {
            // Storage bloqueado: mantém a cópia em memória (vazia no começo da visita). Zerar aqui
            // apagaria, a cada clique, o que a cliente acabou de pôr no carrinho.
            SwitchToMemoryOnly(ex);
        }
        catch (CryptographicException ex)
        {
            // Acontece se as chaves de proteção mudarem (ex.: outro servidor). Começa um carrinho novo
            // e apaga o valor ilegível, para o aviso sair uma vez só, e não a cada página aberta.
            logger.LogWarning(ex, "Carrinho salvo não pôde ser lido; iniciando vazio.");
            _cart = new Cart();
            try
            {
                await storage.DeleteAsync(StorageKey);
            }
            catch (JSException jsEx)
            {
                // Mesma regra do resto do serviço: falha do storage não derruba a página
                SwitchToMemoryOnly(jsEx);
            }
        }
        _loaded = true;
    }

    // Chamar só com a trava (_lock) já obtida.
    private async Task WriteAsync()
    {
        if (_memoryOnly) return;
        try
        {
            await storage.SetAsync(StorageKey, _cart.Items.ToList());
        }
        catch (JSException ex)
        {
            // A mudança fica em memória: vale até a cliente fechar a aba
            SwitchToMemoryOnly(ex);
        }
    }

    // Uma vez por circuito: daqui em diante o carrinho não lê nem grava mais o storage
    private void SwitchToMemoryOnly(JSException ex)
    {
        if (_memoryOnly) return;
        _memoryOnly = true;
        logger.LogWarning(ex, "localStorage indisponível no navegador; o carrinho vale só nesta visita.");
    }
}
