using AtelieMarylu.Domain.Shipping;

namespace AtelieMarylu.Infrastructure.Integrations;

// Configurações do Melhor Envio (seção "MelhorEnvio"). Em desenvolvimento ficam no user-secrets;
// em produção, no .env (MELHORENVIO_*). Sem token, a loja não cota frete por transportadora:
// em Development usa o simulado; fora dele, oferece só a retirada no ateliê.
public class MelhorEnvioOptions
{
    public const string SectionName = "MelhorEnvio";

    // Token de acesso pessoal, gerado no painel do Melhor Envio (Integrações → Permissões de acesso → Gerar token),
    // com a permissão "shipping-calculate". O do sandbox sai em sandbox.melhorenvio.com.br, com login próprio.
    public string Token { get; set; } = "";

    // true = ambiente de testes (sandbox.melhorenvio.com.br). Em produção precisa ser false, ou a loja não sobe.
    public bool Sandbox { get; set; } = true;

    // CEP de onde o pacote sai (o ateliê). Fica na configuração, e não no código, porque é dado da Marylu.
    public string OriginPostalCode { get; set; } = "";

    // O Melhor Envio exige um e-mail de contato técnico no User-Agent de cada chamada. Use o da loja (contato@...).
    public string ContactEmail { get; set; } = "";

    // Ids das modalidades a cotar, separados por vírgula (ex.: "1,2,3,4,17"). Vazio = todas as que o Melhor Envio
    // oferecer para o trecho. Serve para tirar uma modalidade sem mexer no código (ex.: uma que vive atrasando).
    public string Services { get; set; } = "";

    public bool IsActive => !string.IsNullOrWhiteSpace(Token);

    public string? NormalizedOriginPostalCode => PostalCode.Normalize(OriginPostalCode);

    public bool HasValidServices =>
        string.IsNullOrWhiteSpace(Services) || Services.Split(',').All(s => int.TryParse(s.Trim(), out var id) && id > 0);

    public Uri ApiBaseUrl => new(Sandbox ? "https://sandbox.melhorenvio.com.br/" : "https://melhorenvio.com.br/");

    // Painel do mesmo ambiente da cotação: com o sandbox ligado, o botão do pedido não pode abrir a produção,
    // onde uma etiqueta gerada por engano seria cobrada de verdade
    public Uri PanelUrl => new(ApiBaseUrl, "painel");
}
