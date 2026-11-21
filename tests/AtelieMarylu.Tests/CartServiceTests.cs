using AtelieMarylu.Domain.Carts;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace AtelieMarylu.Tests;

// Cada aba do navegador tem o seu circuito (e o seu CartService), mas todas dividem o mesmo localStorage.
public class CartServiceTests
{
    private readonly FakeLocalStorage _browser = new();
    private readonly EphemeralDataProtectionProvider _keys = new();

    private CartService NewTab() =>
        new(new ProtectedLocalStorage(_browser, _keys), NullLogger<CartService>.Instance);

    [Fact]
    public async Task Change_in_one_tab_does_not_bring_back_items_cleared_in_another()
    {
        var tabA = NewTab();
        await tabA.AddAsync(productId: 1);

        var tabB = NewTab();
        await tabB.LoadAsync();
        await tabA.RemoveOrderedAsync([new CartItem(1, 1)]); // a aba A finalizou o pedido

        await tabB.AddAsync(productId: 2); // a aba B, aberta antes, adiciona outro produto

        var reopened = NewTab();
        await reopened.LoadAsync();
        Assert.Equal([2], reopened.Current.Items.Select(i => i.ProductId));
    }

    [Fact]
    public async Task Finishing_an_order_keeps_what_another_tab_added_after_checkout_opened()
    {
        var checkoutTab = NewTab();
        await checkoutTab.AddAsync(productId: 1);
        var ordered = checkoutTab.Current.Items.ToList(); // o que o checkout mostrou e virou pedido

        var otherTab = NewTab();
        await otherTab.AddAsync(productId: 2); // acrescentado em outra aba com o checkout aberto

        await checkoutTab.RemoveOrderedAsync(ordered);

        var reopened = NewTab();
        await reopened.LoadAsync();
        Assert.Equal([2], reopened.Current.Items.Select(i => i.ProductId));
    }

    [Fact]
    public async Task Load_shows_what_another_tab_saved()
    {
        var tabA = NewTab();
        var tabB = NewTab();
        await tabB.LoadAsync();

        await tabA.AddAsync(productId: 1, quantity: 3);
        await tabB.LoadAsync(); // a aba B abre a página do carrinho

        Assert.Equal(3, tabB.Current.TotalUnits);
    }

    [Fact]
    public async Task Simultaneous_changes_in_the_same_tab_are_not_lost()
    {
        _browser.Delay = TimeSpan.FromMilliseconds(20);
        var tab = NewTab();

        await Task.WhenAll(tab.AddAsync(productId: 1), tab.AddAsync(productId: 2));

        var reopened = NewTab();
        await reopened.LoadAsync();
        Assert.Equal(2, reopened.Current.Items.Count);
    }

    [Fact]
    public async Task Load_that_ends_after_a_change_does_not_show_the_old_cart()
    {
        var tab = NewTab();

        var hold = _browser.HoldNextRead(); // a leitura do LoadAsync fica parada, já com a foto do storage vazio
        var load = tab.LoadAsync();
        var add = tab.AddAsync(productId: 1);
        hold.SetResult();
        await Task.WhenAll(load, add);

        Assert.Equal([1], tab.Current.Items.Select(i => i.ProductId));
    }

    [Fact]
    public async Task Unreadable_cart_is_deleted_so_it_is_not_read_again()
    {
        // Gravado com outras chaves (ex.: antes de as chaves de proteção mudarem no servidor)
        var otherKeys = new ProtectedLocalStorage(_browser, new EphemeralDataProtectionProvider());
        await otherKeys.SetAsync("cart", new[] { new CartItem(1, 1) });

        var tab = NewTab();
        await tab.LoadAsync();

        Assert.True(tab.Current.IsEmpty);
        Assert.False(_browser.Has("cart"));
    }

    [Fact]
    public async Task Blocked_storage_does_not_break_the_page_and_starts_empty()
    {
        _browser.Blocked = true;
        var tab = NewTab();

        await tab.LoadAsync();

        Assert.True(tab.IsLoaded);
        Assert.True(tab.Current.IsEmpty);
    }

    [Fact]
    public async Task With_blocked_storage_the_cart_works_in_memory_during_the_visit()
    {
        _browser.Blocked = true;
        var tab = NewTab();

        await tab.AddAsync(productId: 1);
        await tab.AddAsync(productId: 1); // a releitura falha, mas não pode zerar o que já estava em memória
        await tab.RemoveOrderedAsync([new CartItem(1, 1)]);

        Assert.Equal([new CartItem(1, 1)], tab.Current.Items);
    }

    [Fact]
    public async Task With_full_storage_changes_are_not_undone_by_the_next_read()
    {
        var tab = NewTab();
        await tab.AddAsync(productId: 1); // gravado antes de o storage encher
        _browser.WritesFail = true;       // storage cheio: lê, mas não grava

        await tab.AddAsync(productId: 2);
        await tab.AddAsync(productId: 2);

        Assert.Equal([new CartItem(1, 1), new CartItem(2, 2)], tab.Current.Items);
    }

    [Fact]
    public async Task Unreadable_cart_that_cannot_be_deleted_does_not_break_the_page()
    {
        var otherKeys = new ProtectedLocalStorage(_browser, new EphemeralDataProtectionProvider());
        await otherKeys.SetAsync("cart", new[] { new CartItem(1, 1) });
        _browser.RemovesFail = true;
        var tab = NewTab();

        await tab.LoadAsync();
        await tab.AddAsync(productId: 2); // já em memória: não relê o valor ilegível

        Assert.Equal([new CartItem(2, 1)], tab.Current.Items);
    }

    // Imita o localStorage que o ProtectedLocalStorage chama por JS interop
    private sealed class FakeLocalStorage : IJSRuntime
    {
        private readonly Dictionary<string, string> _values = [];
        private TaskCompletionSource? _heldRead;
        public TimeSpan Delay { get; set; }

        // Imita "bloquear todos os cookies e dados de sites": todo acesso ao localStorage falha
        public bool Blocked { get; set; }

        // Imita o storage cheio (QuotaExceededError): a leitura funciona, a gravação falha
        public bool WritesFail { get; set; }

        public bool RemovesFail { get; set; }

        public bool Has(string key) => _values.ContainsKey(key);

        // A próxima leitura tira a "foto" do valor na hora e só devolve quando o teste liberar
        public TaskCompletionSource HoldNextRead() => _heldRead = new TaskCompletionSource();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            await Task.Delay(Delay, cancellationToken);
            if (Blocked)
                throw new JSException("Failed to read the 'localStorage' property from 'Window': Access is denied for this document.");
            var key = (string)args![0]!;
            object? result = null;
            switch (identifier)
            {
                case "localStorage.getItem":
                    result = _values.GetValueOrDefault(key);
                    if (_heldRead is { } held)
                    {
                        _heldRead = null;
                        await held.Task;
                    }
                    break;
                case "localStorage.setItem" when WritesFail:
                    throw new JSException("Failed to execute 'setItem' on 'Storage': Setting the value of 'cart' exceeded the quota.");
                case "localStorage.setItem":
                    _values[key] = (string)args[1]!;
                    break;
                case "localStorage.removeItem" when RemovesFail:
                    throw new JSException("Access is denied for this document.");
                case "localStorage.removeItem":
                    _values.Remove(key);
                    break;
                default:
                    throw new NotSupportedException(identifier);
            }
            return (TValue)result!;
        }
    }
}
