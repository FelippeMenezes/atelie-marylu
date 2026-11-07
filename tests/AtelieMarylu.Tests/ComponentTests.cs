using System.Globalization;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Web.Components.Shared;
using AtelieMarylu.Web.Configuration;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Tests;

// Testes de componentes Blazor com bUnit: renderizam o componente em memória, sem navegador
public class ComponentTests : BunitContext
{
    [Fact]
    public void Quantity_respects_maximum()
    {
        var amount = 2;
        var cut = Render<QuantityPicker>(p => p
            .Add(x => x.Value, 2)
            .Add(x => x.Max, 3)
            .Add(x => x.ValueChanged, (int v) => amount = v));

        cut.Find("button[aria-label=Aumentar]").Click();
        Assert.Equal(3, amount);

        cut.Render(p => p.Add(x => x.Value, 3));
        Assert.True(cut.Find("button[aria-label=Aumentar]").HasAttribute("disabled"));
    }

    [Fact]
    public void Quantity_does_not_go_below_minimum()
    {
        var cut = Render<QuantityPicker>(p => p.Add(x => x.Value, 1));

        Assert.True(cut.Find("button[aria-label=Diminuir]").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(OrderStatus.AwaitingPayment, "Aguardando pagamento")]
    [InlineData(OrderStatus.Cancelled, "Cancelado")]
    public void Order_status_description(OrderStatus status, string text)
    {
        var cut = Render<OrderStatusBadge>(p => p.Add(x => x.Status, status));

        Assert.Equal(text, cut.Find("span").TextContent);
        Assert.Contains($"status-{status}", cut.Find("span").ClassName);
    }

    [Fact]
    public void Price_shows_pix_discount_and_installments()
    {
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        Services.AddSingleton(Options.Create(new ShopOptions { PixDiscountPercent = 10, InterestFreeInstallments = 3 }));

        var cut = Render<ProductPrice>(p => p.Add(x => x.Price, 90m));
        // O formato brasileiro usa espaço não separável entre "R$" e o valor
        var text = cut.Markup.Replace('\u00A0', ' ');

        Assert.Contains("R$ 90,00", text);
        Assert.Contains("R$ 81,00", text);       // 10% off no PIX
        Assert.Contains("3x de R$ 30,00", text);
    }

    [Fact]
    public void Photo_without_image_shows_logo()
    {
        var cut = Render<ProductPhoto>(p => p.Add(x => x.Name, "Bolsa"));

        Assert.Contains("img/logo.png", cut.Markup);
    }
}
