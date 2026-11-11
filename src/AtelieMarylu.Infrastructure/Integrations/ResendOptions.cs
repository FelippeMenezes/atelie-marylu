namespace AtelieMarylu.Infrastructure.Integrations;

// Configurações do Resend (seção "Resend"). A chave fica no user-secrets em desenvolvimento
// e no .env em produção (Resend__ApiKey). Sem chave, a loja não envia e-mail de verdade.
public class ResendOptions
{
    public const string SectionName = "Resend";

    public string ApiKey { get; set; } = "";

    // Remetente, ex.: "Ateliê Marylu <loja@ateliemarylu.com.br>". Precisa ser do domínio verificado no Resend.
    public string From { get; set; } = "";

    // Para onde vão as respostas do cliente (ex.: contato@, que o ImprovMX encaminha ao Gmail)
    public string ReplyTo { get; set; } = "";

    public bool IsActive => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(From);
}
