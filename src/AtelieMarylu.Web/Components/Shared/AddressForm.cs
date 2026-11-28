using System.ComponentModel.DataAnnotations;
using AtelieMarylu.Domain.Orders;
using AtelieMarylu.Infrastructure.Integrations;
using AtelieMarylu.Domain.Text;
using AtelieMarylu.Web.Validation;

namespace AtelieMarylu.Web.Components.Shared;

// Modelo de tela com as validações do formulário; vira ShippingAddress ao salvar.
// [SafeAddressText]: sem link, e-mail nem telefone nos campos de texto livre, que vão nos e-mails do pedido (AML-0042).
public class AddressForm
{
    [Required(ErrorMessage = "Enter the postal code.")]
    [RegularExpression(@"^\d{5}-?\d{3}$", ErrorMessage = "Invalid postal code.")]
    public string PostalCode { get; set; } = "";

    [Required(ErrorMessage = "Enter the street.")]
    [StringLength(150)]
    [SafeAddressText]
    public string Street { get; set; } = "";

    [Required(ErrorMessage = "Enter the number.")]
    [StringLength(20)]
    [SafeAddressText]
    public string Number { get; set; } = "";

    [StringLength(80)]
    [SafeAddressText]
    public string? Complement { get; set; }

    [Required(ErrorMessage = "Enter the district.")]
    [StringLength(80)]
    [SafeAddressText]
    public string District { get; set; } = "";

    [Required(ErrorMessage = "Enter the city.")]
    [StringLength(80)]
    [SafeAddressText]
    public string City { get; set; } = "";

    [Required(ErrorMessage = "Enter the state (UF).")]
    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Invalid state.")]
    public string State { get; set; } = "";

    // Grava normalizado (NFC, sem espaços repetidos): o mesmo endereço digitado em teclados diferentes vira o
    // mesmo texto, e é esse texto que a regra de CustomerText conferiu
    public ShippingAddress ToAddress() => new()
    {
        PostalCode = PostalCode.Trim(),
        Street = CustomerText.Normalize(Street),
        Number = CustomerText.Normalize(Number),
        Complement = string.IsNullOrWhiteSpace(Complement) ? null : CustomerText.Normalize(Complement),
        District = CustomerText.Normalize(District),
        City = CustomerText.Normalize(City),
        State = State.Trim().ToUpperInvariant(),
    };

    // Rua, bairro, cidade e UF vindos do ViaCEP. Número e complemento ficam com a cliente.
    public void Fill(PostalCodeAddress found)
    {
        Street = found.Street;
        District = found.District;
        City = found.City;
        State = found.State;
    }

    // Só o CEP (ex.: o último cotado), já no formato da tela: "20560-040"
    public static AddressForm WithPostalCode(string postalCode) =>
        new() { PostalCode = Domain.Shipping.PostalCode.Normalize(postalCode) is { } d ? $"{d[..5]}-{d[5..]}" : "" };

    public static AddressForm FromAddress(ShippingAddress e) => new()
    {
        // O pedido guarda só os dígitos; a tela mostra "20560-040"
        PostalCode = WithPostalCode(e.PostalCode).PostalCode is { Length: > 0 } formatted ? formatted : e.PostalCode,
        Street = e.Street,
        Number = e.Number,
        Complement = e.Complement,
        District = e.District,
        City = e.City,
        State = e.State,
    };
}
