using System.ComponentModel.DataAnnotations;
using AtelieMarylu.Domain.Pedidos;

namespace AtelieMarylu.Web.Components.Shared;

// Modelo de tela com as validações do formulário; vira EnderecoEntrega ao salvar.
public class FormularioEndereco
{
    [Required(ErrorMessage = "Informe o CEP.")]
    [RegularExpression(@"^\d{5}-?\d{3}$", ErrorMessage = "CEP inválido.")]
    public string Cep { get; set; } = "";

    [Required(ErrorMessage = "Informe a rua.")]
    [StringLength(150)]
    public string Logradouro { get; set; } = "";

    [Required(ErrorMessage = "Informe o número.")]
    [StringLength(20)]
    public string Numero { get; set; } = "";

    [StringLength(80)]
    public string? Complemento { get; set; }

    [Required(ErrorMessage = "Informe o bairro.")]
    [StringLength(80)]
    public string Bairro { get; set; } = "";

    [Required(ErrorMessage = "Informe a cidade.")]
    [StringLength(80)]
    public string Cidade { get; set; } = "";

    [Required(ErrorMessage = "Informe a UF.")]
    [RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "UF inválida.")]
    public string Uf { get; set; } = "";

    public EnderecoEntrega ParaEndereco() => new()
    {
        Cep = Cep.Trim(),
        Logradouro = Logradouro.Trim(),
        Numero = Numero.Trim(),
        Complemento = string.IsNullOrWhiteSpace(Complemento) ? null : Complemento.Trim(),
        Bairro = Bairro.Trim(),
        Cidade = Cidade.Trim(),
        Uf = Uf.Trim().ToUpperInvariant(),
    };

    public static FormularioEndereco De(EnderecoEntrega e) => new()
    {
        Cep = e.Cep,
        Logradouro = e.Logradouro,
        Numero = e.Numero,
        Complemento = e.Complemento,
        Bairro = e.Bairro,
        Cidade = e.Cidade,
        Uf = e.Uf,
    };
}
