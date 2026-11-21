using AtelieMarylu.Tests.Integration;
using AtelieMarylu.Web.Services;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace AtelieMarylu.Tests;

// O checkout abre com o último CEP cotado. É conveniência: nenhuma falha do navegador pode virar erro na tela.
public class LastPostalCodeServiceTests
{
    private readonly FakeLocalStorage browser = new();
    private readonly EphemeralDataProtectionProvider keys = new();
    private readonly FakeClock clock = new();

    private LastPostalCodeService Service(IDataProtectionProvider? otherKeys = null) =>
        new(new ProtectedLocalStorage(browser, otherKeys ?? keys), clock, NullLogger<LastPostalCodeService>.Instance);

    [Fact]
    public async Task Without_a_quote_there_is_no_postal_code()
    {
        Assert.Null(await Service().GetAsync());
    }

    [Fact]
    public async Task Remembers_the_last_quoted_postal_code_normalized()
    {
        await Service().SaveAsync("20560-040");
        await Service().SaveAsync("22452-694");

        // Outra página (outro circuito) lê o mesmo navegador
        Assert.Equal("22452694", await Service().GetAsync());
    }

    [Fact]
    public async Task Invalid_postal_code_is_not_saved()
    {
        await Service().SaveAsync("123");

        Assert.Null(await Service().GetAsync());
    }

    [Fact]
    public async Task Forgets_after_30_days()
    {
        await Service().SaveAsync("22452694");

        clock.Now = clock.Now.AddDays(31);

        Assert.Null(await Service().GetAsync());
    }

    [Fact]
    public async Task Blocked_storage_means_empty_field_not_an_error()
    {
        browser.Blocked = true;

        await Service().SaveAsync("22452694");
        Assert.Null(await Service().GetAsync());
    }

    [Fact]
    public async Task Unreadable_value_means_empty_field_not_an_error()
    {
        // Chaves de proteção trocadas (ex.: outro servidor): o valor guardado vira lixo
        await Service().SaveAsync("22452694");

        Assert.Null(await Service(new EphemeralDataProtectionProvider()).GetAsync());
    }

    // Imita o localStorage que o ProtectedLocalStorage chama por JS interop
    private sealed class FakeLocalStorage : IJSRuntime
    {
        private readonly Dictionary<string, string> values = [];

        public bool Blocked { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (Blocked) throw new JSException("Access is denied for this document.");
            var key = (string)args![0]!;
            object? result = null;
            switch (identifier)
            {
                case "localStorage.getItem": result = values.GetValueOrDefault(key); break;
                case "localStorage.setItem": values[key] = (string)args[1]!; break;
                case "localStorage.removeItem": values.Remove(key); break;
                default: throw new NotSupportedException(identifier);
            }
            return ValueTask.FromResult((TValue)result!);
        }
    }
}
