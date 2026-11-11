using AtelieMarylu.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AtelieMarylu.Web.Security;

// Quanto tempo valem os links enviados por e-mail. Os textos dos e-mails mostram estes mesmos valores.
// Redefinição de senha dá acesso à conta, então vale pouco (recomendação da OWASP: prazo curto).
// Confirmar um e-mail é menos sensível e a pessoa pode demorar a abrir a caixa, então vale mais.
public static class AccountTokenLifespans
{
    public static readonly TimeSpan PasswordReset = TimeSpan.FromHours(2);
    public static readonly TimeSpan EmailConfirmation = TimeSpan.FromHours(24);
}

// O Identity usa um único gerador (e um único prazo) para todos os links. Este segundo gerador,
// com prazo próprio, cuida da confirmação de conta e da troca de e-mail. O padrão está na documentação
// do ASP.NET Core ("Change the email token lifespan").
public sealed class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public EmailConfirmationTokenProviderOptions()
    {
        // Nome diferente = chave de proteção diferente: um token de confirmação não serve para redefinir senha
        Name = "EmailConfirmationDataProtectorTokenProvider";
        TokenLifespan = AccountTokenLifespans.EmailConfirmation;
    }
}

public sealed class EmailConfirmationTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<EmailConfirmationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger)
{
    public const string ProviderName = "EmailConfirmation";
}
