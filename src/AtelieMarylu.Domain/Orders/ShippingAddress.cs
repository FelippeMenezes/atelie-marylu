using AtelieMarylu.Domain.Text;

namespace AtelieMarylu.Domain.Orders;

public class ShippingAddress
{
    public string PostalCode { get; set; } = "";
    public string Street { get; set; } = "";
    public string Number { get; set; } = "";
    public string? Complement { get; set; }
    public string District { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";

    // O endereço vai nos e-mails do pedido: os campos de texto livre não podem carregar link, e-mail nem telefone
    // (AML-0042, veja CustomerText). Um só lugar com a lista de campos: um campo novo entra aqui e vale para a
    // entidade Order e para os e-mails ao mesmo tempo. O CEP e a UF têm formato fixo e não precisam.
    public bool IsSafeForEmail() =>
        new[] { Street, Number, Complement, District, City }.All(CustomerText.IsSafeAddressPart);
}
