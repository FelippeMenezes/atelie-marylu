using System.ComponentModel.DataAnnotations;
using AtelieMarylu.Domain.Orders;

namespace AtelieMarylu.Web.Components.Shared;

// Modelo de tela com as validações do formulário; vira ShippingAddress ao salvar.
public class AddressForm
{
    [Required(ErrorMessage = "Enter the postal code.")]
    [RegularExpression(@"^\d{5}-?\d{3}$", ErrorMessage = "Invalid postal code.")]
    public string PostalCode { get; set; } = "";

    [Required(ErrorMessage = "Enter the street.")]
    [StringLength(150)]
    public string Street { get; set; } = "";

    [Required(ErrorMessage = "Enter the number.")]
    [StringLength(20)]
    public string Number { get; set; } = "";

    [StringLength(80)]
    public string? Complement { get; set; }

    [Required(ErrorMessage = "Enter the district.")]
    [StringLength(80)]
    public string District { get; set; } = "";

    [Required(ErrorMessage = "Enter the city.")]
    [StringLength(80)]
    public string City { get; set; } = "";

    [Required(ErrorMessage = "Enter the state (UF).")]
    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Invalid state.")]
    public string State { get; set; } = "";

    public ShippingAddress ToAddress() => new()
    {
        PostalCode = PostalCode.Trim(),
        Street = Street.Trim(),
        Number = Number.Trim(),
        Complement = string.IsNullOrWhiteSpace(Complement) ? null : Complement.Trim(),
        District = District.Trim(),
        City = City.Trim(),
        State = State.Trim().ToUpperInvariant(),
    };

    public static AddressForm FromAddress(ShippingAddress e) => new()
    {
        PostalCode = e.PostalCode,
        Street = e.Street,
        Number = e.Number,
        Complement = e.Complement,
        District = e.District,
        City = e.City,
        State = e.State,
    };
}
