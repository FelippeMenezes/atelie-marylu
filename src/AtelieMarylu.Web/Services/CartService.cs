using System.Security.Cryptography;
using AtelieMarylu.Domain.Carts;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace AtelieMarylu.Web.Services;

// O carrinho fica no navegador (localStorage, criptografado pelo ASP.NET), então funciona
// sem login e sobrevive ao cadastro/login no meio da compra.
// Um por circuito Blazor: o cabeçalho e as páginas compartilham a mesma instância.
public class CartService(ProtectedLocalStorage storage, ILogger<CartService> logger)
{
    private const string StorageKey = "cart";
    private Cart _cart = new();
    private bool _loaded;

    public event Action? Changed;

    public Cart Current => _cart;
    public bool IsLoaded => _loaded;

    // Só pode ser chamado depois da primeira renderização (OnAfterRenderAsync):
    // durante o prerender o servidor ainda não fala com o navegador.
    public async Task LoadAsync()
    {
        if (_loaded) return;
        try
        {
            var stored = await storage.GetAsync<List<CartItem>>(StorageKey);
            _cart = new Cart(stored.Success && stored.Value is not null ? stored.Value : []);
        }
        catch (CryptographicException ex)
        {
            // Acontece se as chaves de proteção mudarem (ex.: outro servidor). Começa um carrinho novo.
            logger.LogWarning(ex, "Carrinho salvo não pôde ser lido; iniciando vazio.");
            _cart = new Cart();
        }
        _loaded = true;
        Changed?.Invoke();
    }

    public Task AddAsync(int productId, int quantity = 1) => ChangeAsync(c => c.Add(productId, quantity));

    public Task ChangeQuantityAsync(int productId, int quantity) => ChangeAsync(c => c.ChangeQuantity(productId, quantity));

    public Task RemoveAsync(int productId) => ChangeAsync(c => c.Remove(productId));

    public Task ClearAsync() => ChangeAsync(c => c.Clear());

    private async Task ChangeAsync(Action<Cart> change)
    {
        await LoadAsync();
        change(_cart);
        await storage.SetAsync(StorageKey, _cart.Items.ToList());
        Changed?.Invoke();
    }
}
