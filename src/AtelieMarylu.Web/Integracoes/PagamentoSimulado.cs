using System.Text.Json;
using AtelieMarylu.Domain.Pagamentos;

namespace AtelieMarylu.Web.Integracoes;

// Provedor de pagamento de mentira, para o fluxo inteiro funcionar antes do Mercado Pago.
// O pagamento fica "Pendente" até alguém aprovar pela tela do pedido (botão só em desenvolvimento)
// ou mandar um POST para /api/webhooks/pagamento, imitando o que o provedor real faz.
public class PagamentoSimulado : IGatewayPagamento
{
    public string Nome => "simulado";

    public Task<InicioPagamento> IniciarAsync(DadosCobranca cobranca, CancellationToken ct = default)
    {
        var idExterno = $"sim-{Guid.NewGuid():N}";
        var pix = cobranca.Metodo == MetodoPagamento.Pix
            ? $"00020126PIXSIMULADO-{cobranca.CodigoPedido}-{cobranca.Valor.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}"
            : null;
        return Task.FromResult(new InicioPagamento(idExterno, UrlPagamento: null, PixCopiaECola: pix));
    }

    public Task<NotificacaoPagamento?> LerNotificacaoAsync(string corpo, IReadOnlyDictionary<string, string> cabecalhos, CancellationToken ct = default)
    {
        // Formato esperado: { "idEvento": "opcional", "idExterno": "sim-...", "status": "Aprovado", "valorPago": 99.90 }
        // O Mercado Pago real exige validar a assinatura (x-signature) e consultar o pagamento na API deles.
        try
        {
            var dados = JsonSerializer.Deserialize<CorpoSimulado>(corpo, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (dados?.IdExterno is null || !Enum.TryParse<StatusPagamento>(dados.Status, true, out var status))
                return Task.FromResult<NotificacaoPagamento?>(null);
            var idEvento = string.IsNullOrWhiteSpace(dados.IdEvento) ? $"{dados.IdExterno}:{status}" : dados.IdEvento;
            return Task.FromResult<NotificacaoPagamento?>(new NotificacaoPagamento(idEvento, dados.IdExterno, status, dados.ValorPago));
        }
        catch (JsonException)
        {
            return Task.FromResult<NotificacaoPagamento?>(null);
        }
    }

    private sealed record CorpoSimulado(string? IdEvento, string? IdExterno, string? Status, decimal? ValorPago);
}
