using System.ComponentModel.DataAnnotations;
using AtelieMarylu.Domain.Text;

namespace AtelieMarylu.Web.Validation;

// Parte do endereço sem link, e-mail nem telefone (AML-0042). O endereço vai nos e-mails do pedido; veja
// CustomerText. A entidade Order confere de novo, com a mesma regra e a mesma mensagem.
[AttributeUsage(AttributeTargets.Property)]
public sealed class SafeAddressTextAttribute : ValidationAttribute
{
    public SafeAddressTextAttribute() => ErrorMessage = "Remove links, emails and phone numbers from the address (the contact phone number goes in the account).";

    public override bool IsValid(object? value) => CustomerText.IsSafeAddressPart(value as string);
}
