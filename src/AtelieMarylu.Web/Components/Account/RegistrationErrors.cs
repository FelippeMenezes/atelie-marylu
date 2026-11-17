using Microsoft.AspNetCore.Identity;

namespace AtelieMarylu.Web.Components.Account;

// Separa, nos erros do cadastro, o "e-mail já cadastrado" dos demais (senha fraca etc.).
//
// Mostrar "este e-mail já está cadastrado" deixa qualquer pessoa descobrir quem é cliente da loja
// (enumeração de contas), o que ajuda phishing direcionado e testes de senhas vazadas. Por isso o
// cadastro não repete esse erro na tela: avisa o dono por e-mail (AccountEmailSender) e mostra uma
// mensagem neutra. Os outros erros continuam aparecendo, porque não revelam nada sobre outras contas.
public static class RegistrationErrors
{
    // O nome de usuário da loja é o próprio e-mail, então o Identity pode acusar qualquer um dos dois
    private static readonly HashSet<string> DuplicateCodes =
    [
        nameof(IdentityErrorDescriber.DuplicateEmail),
        nameof(IdentityErrorDescriber.DuplicateUserName),
    ];

    public static (bool EmailTaken, IReadOnlyList<IdentityError> Others) Split(IEnumerable<IdentityError> errors)
    {
        var list = errors.ToList();
        var others = list.Where(error => !DuplicateCodes.Contains(error.Code)).ToList();
        return (others.Count < list.Count, others);
    }
}
