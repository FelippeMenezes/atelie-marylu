using System.ComponentModel.DataAnnotations;

namespace AtelieMarylu.Web.Configuration;

// Seção "Store" do appsettings. Os dados legais aparecem no rodapé e nas políticas,
// como exige o Decreto 7.962/2013 (comércio eletrônico).
public class ShopOptions
{
    public const string SectionName = "Store";

    [Required] public string Name { get; set; } = "Ateliê Marylu";

    public string LegalName { get; set; } = "";
    public string TaxDocument { get; set; } = "";       // CNPJ ou CPF
    public string Address { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string Phone { get; set; } = "";
    public string WhatsApp { get; set; } = "";        // só números, com DDI: 5521999999999
    public string Instagram { get; set; } = "ateliemarylurj";

    [Range(0, 50)] public decimal PixDiscountPercent { get; set; } = 5;
    [Range(1, 12)] public int InterestFreeInstallments { get; set; } = 3;

    // Pedido feito no site sem pagamento nesse prazo é cancelado e o estoque volta
    [Range(10, 10080)] public int PaymentDeadlineMinutes { get; set; } = 30;

    // Pedido criado pelo painel (venda pelo Instagram/WhatsApp) costuma demorar mais para ser pago
    [Range(1, 720)] public int AdminPaymentDeadlineHours { get; set; } = 72;

    // Impede que alguém reserve o estoque inteiro com pedidos que nunca paga
    [Range(1, 20)] public int MaxPendingOrdersPerCustomer { get; set; } = 3;

    public bool HasLegalData => !string.IsNullOrWhiteSpace(TaxDocument);
}
