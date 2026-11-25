using System.Globalization;
using AtelieMarylu.Web;
using AtelieMarylu.Web.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace AtelieMarylu.Tests;

// O detalhe da auditoria é gravado em inglês e traduzido só na hora de mostrar (AML-0040)
public class AuditDetailsTranslatorTests
{
    private static readonly IStringLocalizer<SharedResource> L =
        new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources").BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<SharedResource>>();

    private static string? In(string culture, string? details)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try { return AuditDetailsTranslator.Translate(L, details); }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData("pt-BR", "CAN-6AYBHB · Pago")]
    [InlineData("es", "CAN-6AYBHB · Pagado")]
    [InlineData("en", "CAN-6AYBHB · Paid")]
    public void Order_status_after_the_protocol_follows_the_language(string culture, string expected) =>
        Assert.Equal(expected, In(culture, "CAN-6AYBHB · Paid"));

    [Fact]
    public void Payment_notice_and_manual_payment_are_translated()
    {
        Assert.Equal("Aprovado via pagbank (ORDE_1)", In("pt-BR", "Approved via pagbank (ORDE_1)"));
        Assert.Equal("Cartão · R$ 39,90", In("pt-BR", "CreditCard · R$ 39,90"));
        Assert.Equal("PIX · R$ 39,90", In("pt-BR", "Pix · R$ 39,90"));
    }

    [Theory]
    [InlineData("AML-Z89784 · R$ 39,90")]
    [InlineData("Cancelado pela cliente: Comprei a cor errada")]
    [InlineData("5 via algo")]
    [InlineData(null)]
    public void Codes_free_text_and_old_records_stay_as_written(string? details) =>
        Assert.Equal(details, In("pt-BR", details));
}
