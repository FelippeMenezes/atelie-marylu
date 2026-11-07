using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace AtelieMarylu.Web.Localization;

// As mensagens padrão do Identity são em inglês. Aqui ficam os erros que a cliente pode ver.
public class LocalizedIdentityErrorDescriber(IStringLocalizer<SharedResource> localizer) : IdentityErrorDescriber
{
    private IdentityError Translate(IdentityError error, string message, params object[] args) =>
        new() { Code = error.Code, Description = localizer[message, args] };

    public override IdentityError PasswordTooShort(int length) => Translate(base.PasswordTooShort(length), "A senha precisa ter pelo menos {0} caracteres.", length);
    public override IdentityError PasswordRequiresDigit() => Translate(base.PasswordRequiresDigit(), "A senha precisa ter pelo menos um número.");
    public override IdentityError PasswordRequiresLower() => Translate(base.PasswordRequiresLower(), "A senha precisa ter pelo menos uma letra minúscula.");
    public override IdentityError PasswordRequiresUpper() => Translate(base.PasswordRequiresUpper(), "A senha precisa ter pelo menos uma letra maiúscula.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Translate(base.PasswordRequiresNonAlphanumeric(), "A senha precisa ter pelo menos um símbolo.");
    public override IdentityError PasswordMismatch() => Translate(base.PasswordMismatch(), "Senha incorreta.");
    public override IdentityError DuplicateEmail(string email) => Translate(base.DuplicateEmail(email), "Este e-mail já está cadastrado.");
    public override IdentityError DuplicateUserName(string userName) => Translate(base.DuplicateUserName(userName), "Este e-mail já está cadastrado.");
    public override IdentityError InvalidEmail(string? email) => Translate(base.InvalidEmail(email), "E-mail inválido.");
    public override IdentityError InvalidUserName(string? userName) => Translate(base.InvalidUserName(userName), "E-mail inválido.");
    public override IdentityError LoginAlreadyAssociated() => Translate(base.LoginAlreadyAssociated(), "Esta conta já está conectada a outro cadastro da loja.");
    public override IdentityError DefaultError() => Translate(base.DefaultError(), "Não foi possível concluir. Tente novamente.");
}
