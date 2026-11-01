using System.ComponentModel.DataAnnotations;

namespace AtelieMarylu.Web.Configuracao;

// Seção "Loja" do appsettings. Os dados legais aparecem no rodapé e nas políticas,
// como exige o Decreto 7.962/2013 (comércio eletrônico).
public class LojaOptions
{
    public const string Secao = "Loja";

    [Required] public string Nome { get; set; } = "Ateliê Marylu";

    public string RazaoSocial { get; set; } = "";
    public string Documento { get; set; } = "";       // CNPJ ou CPF
    public string Endereco { get; set; } = "";
    public string EmailContato { get; set; } = "";
    public string Telefone { get; set; } = "";
    public string WhatsApp { get; set; } = "";        // só números, com DDI: 5521999999999
    public string Instagram { get; set; } = "ateliemarylurj";

    [Range(0, 50)] public decimal DescontoPixPercentual { get; set; } = 5;
    [Range(1, 12)] public int ParcelasSemJuros { get; set; } = 3;

    // Pedido feito no site sem pagamento nesse prazo é cancelado e o estoque volta
    [Range(10, 10080)] public int PrazoPagamentoMinutos { get; set; } = 120;

    // Pedido criado pelo painel (venda pelo Instagram/WhatsApp) costuma demorar mais para ser pago
    [Range(1, 720)] public int PrazoPagamentoPainelHoras { get; set; } = 72;

    // Impede que alguém reserve o estoque inteiro com pedidos que nunca paga
    [Range(1, 20)] public int MaxPedidosPendentesPorCliente { get; set; } = 3;

    public bool TemDadosLegais => !string.IsNullOrWhiteSpace(Documento);
}
