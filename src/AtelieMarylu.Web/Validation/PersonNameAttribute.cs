using System.ComponentModel.DataAnnotations;
using AtelieMarylu.Domain.Text;

namespace AtelieMarylu.Web.Validation;

// Nome de pessoa (AML-0042): só letras, espaço, apóstrofo, ponto e hífen, até 80 caracteres. O nome vai nos
// e-mails da loja, e a regra impede que ele carregue link ou telefone de golpe (veja CustomerText).
// Vazio passa: quem exige o campo é o [Required], com a mensagem própria.
[AttributeUsage(AttributeTargets.Property)]
public sealed class PersonNameAttribute : ValidationAttribute
{
    public PersonNameAttribute() =>
        ErrorMessage = "Use only letters, spaces, apostrophes, periods and hyphens in the name (up to 80 characters). After a period, use a space or an initial, as in \"J. P. Souza\" or \"J.P. Souza\".";

    public override bool IsValid(object? value) => value is null or "" || CustomerText.IsValidName(value as string);
}
