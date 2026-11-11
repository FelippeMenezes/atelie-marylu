using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace AtelieMarylu.Web.Localization;

// As mensagens padrão do Identity são em inglês. Aqui ficam os erros que a cliente pode ver.
public class LocalizedIdentityErrorDescriber(IStringLocalizer<SharedResource> localizer) : IdentityErrorDescriber
{
    private IdentityError Translate(IdentityError error, string message, params object[] args) =>
        new() { Code = error.Code, Description = localizer[message, args] };

    public override IdentityError PasswordTooShort(int length) => Translate(base.PasswordTooShort(length), "The password must have at least {0} characters.", length);
    public override IdentityError PasswordRequiresDigit() => Translate(base.PasswordRequiresDigit(), "The password must have at least one number.");
    public override IdentityError PasswordRequiresLower() => Translate(base.PasswordRequiresLower(), "The password must have at least one lowercase letter.");
    public override IdentityError PasswordRequiresUpper() => Translate(base.PasswordRequiresUpper(), "The password must have at least one uppercase letter.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Translate(base.PasswordRequiresNonAlphanumeric(), "The password must have at least one symbol.");
    public override IdentityError PasswordMismatch() => Translate(base.PasswordMismatch(), "Wrong password.");
    public override IdentityError DuplicateEmail(string email) => Translate(base.DuplicateEmail(email), "This email is already registered.");
    public override IdentityError DuplicateUserName(string userName) => Translate(base.DuplicateUserName(userName), "This email is already registered.");
    public override IdentityError InvalidEmail(string? email) => Translate(base.InvalidEmail(email), "Invalid email.");
    public override IdentityError InvalidUserName(string? userName) => Translate(base.InvalidUserName(userName), "Invalid email.");
    public override IdentityError LoginAlreadyAssociated() => Translate(base.LoginAlreadyAssociated(), "This account is already connected to another store account.");
    // Link de redefinição de senha vencido, já usado ou adulterado
    public override IdentityError InvalidToken() => Translate(base.InvalidToken(), "This link is invalid, has expired or has already been used. Ask for a new one.");
    public override IdentityError DefaultError() => Translate(base.DefaultError(), "Could not complete. Try again.");
}
