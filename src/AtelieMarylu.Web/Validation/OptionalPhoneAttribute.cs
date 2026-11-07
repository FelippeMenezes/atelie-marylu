using System.ComponentModel.DataAnnotations;

namespace AtelieMarylu.Web.Validation;

// Telefone opcional. O [Phone] do .NET recusa texto vazio, e um formulário envia o campo não
// preenchido justamente como texto vazio: sem isto, o cadastro travaria em "Telefone inválido".
[AttributeUsage(AttributeTargets.Property)]
public sealed class OptionalPhoneAttribute : ValidationAttribute
{
    private static readonly PhoneAttribute Phone = new();

    public override bool IsValid(object? value) => value is null or "" || Phone.IsValid(value);
}
