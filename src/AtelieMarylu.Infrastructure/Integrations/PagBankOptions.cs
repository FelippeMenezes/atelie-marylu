namespace AtelieMarylu.Infrastructure.Integrations;

// Configurações do PagBank (seção "PagBank"). Em desenvolvimento ficam no user-secrets;
// em produção, no .env (PAGBANK_*). Sem token e endereço público, a loja usa o gateway simulado.
public class PagBankOptions
{
    public const string SectionName = "PagBank";

    // Token da conta. Sandbox: portaldev.pagbank.com.br, aba "Tokens".
    // Produção: pagbank.com.br → Vendas → Integrações → "Gerar Token" (só depois da homologação).
    public string Token { get; set; } = "";

    // true = ambiente de testes (sandbox.api.pagseguro.com). Só vira false em produção, depois da homologação.
    public bool Sandbox { get; set; } = true;

    // Endereço público da loja, sem barra no fim (ex.: https://www.ateliemarylu.com.br). O PagBank devolve
    // a cliente para cá e manda os avisos de pagamento para cá. Em desenvolvimento, o endereço do túnel.
    public string PublicBaseUrl { get; set; } = "";

    public bool IsActive => !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(PublicBaseUrl);

    public Uri ApiBaseUrl => new(Sandbox ? "https://sandbox.api.pagseguro.com/" : "https://api.pagseguro.com/");
}
