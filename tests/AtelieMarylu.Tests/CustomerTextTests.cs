using AtelieMarylu.Domain.Text;
using AtelieMarylu.Web.Validation;

namespace AtelieMarylu.Tests;

// Texto livre que vai para os e-mails da loja (AML-0042): nomes e endereços reais passam; link, e-mail e telefone,
// que transformariam um e-mail da loja em golpe para quem teve o e-mail cadastrado por outra pessoa, não.
public class CustomerTextTests
{
    [Theory]
    [InlineData("Maria")]
    [InlineData("Maria José da Silva")]
    [InlineData("D'Ávila")]
    [InlineData("D’Ávila")]          // o apóstrofo que o celular põe sozinho
    [InlineData("Ana-Lúcia")]
    [InlineData("J. P. Souza")]
    [InlineData("Souza Jr.")]
    [InlineData("Ñuñez")]
    [InlineData("Zoë Müller")]
    [InlineData("  Maria  ")]        // espaços nas pontas são ignorados
    [InlineData("J.P. Souza")]       // iniciais coladas: depois do ponto vem uma letra só
    [InlineData("A.B.C. Lima")]
    [InlineData("Jose\u0301")]      // "José" com o acento solto (NFD), como alguns teclados mandam
    public void Real_names_pass(string name)
    {
        Assert.True(CustomerText.IsValidName(name));
    }

    [Theory]
    [InlineData("Seu cartão foi bloqueado, ligue 0800 123 4567")]
    [InlineData("Maria acesse golpe.com")]
    [InlineData("https://golpe.com")]
    [InlineData("Maria www.golpe")]
    [InlineData("maria@golpe.com")]
    [InlineData("Maria golpe.ru")]   // ponto seguido de 2+ letras é cara de domínio, qualquer terminação
    [InlineData("Maria t.me")]
    [InlineData("Maria golpe.online")]
    [InlineData("M\u0337\u0327\u0337aria")] // acentos empilhados ("Zalgo")
    [InlineData("Maria 2")]
    [InlineData("<b>Maria</b>")]
    [InlineData("-Maria")]
    [InlineData("Maria'")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Names_with_links_digits_or_symbols_fail(string? name)
    {
        Assert.False(CustomerText.IsValidName(name));
    }

    [Fact]
    public void Name_has_a_length_limit()
    {
        Assert.True(CustomerText.IsValidName(new string('a', CustomerText.NameMaxLength)));
        Assert.False(CustomerText.IsValidName(new string('a', CustomerText.NameMaxLength + 1)));
    }

    [Theory]
    [InlineData("Maria Silva", "Maria Silva")]
    [InlineData("João (Trabalho)", "João Trabalho")]
    [InlineData("🎉 Ana 2026", "Ana")]
    [InlineData("J.P. Souza", "J. P. Souza")]
    [InlineData("Ana golpe.ru", "Ana golpe. ru")]   // o ponto colado ganha espaço e deixa de virar link
    [InlineData("12345", "")]
    [InlineData(null, "")]
    public void Google_name_is_cleaned(string? raw, string expected)
    {
        var cleaned = CustomerText.CleanName(raw);

        Assert.Equal(expected, cleaned);
        Assert.True(cleaned == "" || CustomerText.IsValidName(cleaned));
    }

    [Fact]
    public void Cleaned_google_name_never_has_a_link_or_digits()
    {
        foreach (var cleaned in new[] { "Ana https://golpe.com", "Ligue 0800 123 4567", "Ana@golpe.com", "www.golpe.com", "Ana golpe.ru" }
                     .Select(CustomerText.CleanName))
        {
            Assert.DoesNotContain("://", cleaned);
            Assert.DoesNotContain("@", cleaned);
            Assert.DoesNotContain(cleaned, char.IsDigit);
            Assert.DoesNotMatch(@"\.\S", cleaned); // nenhum ponto colado em letra: nada vira link
            Assert.True(cleaned == "" || CustomerText.IsValidName(cleaned));
        }
    }

    [Fact]
    public void Cleaned_google_name_fits_the_limit()
    {
        var cleaned = CustomerText.CleanName(string.Join(' ', Enumerable.Repeat("Maria", 30)));

        Assert.InRange(cleaned.Length, 1, CustomerText.NameMaxLength);
        Assert.True(CustomerText.IsValidName(cleaned));
    }

    [Theory]
    [InlineData("Rua Sete de Setembro")]
    [InlineData("R.Sete de Setembro")]       // digitado sem espaço
    [InlineData("Av.Brasil")]
    [InlineData("Av. Nossa Sra. de Copacabana")]
    [InlineData("Rod. BR-116, km 23")]
    [InlineData("Rod.BR 116")]
    [InlineData("Ed.Top Life")]              // abreviação conhecida antes do ponto: é endereço
    [InlineData("Estr.do Rio Grande")]
    [InlineData("Pça.XV de Novembro")]
    [InlineData("Sto.Antônio")]
    [InlineData("N.Sra. de Fátima")]
    [InlineData("Dr.Satamini")]
    [InlineData("Apto 101/102")]
    [InlineData("1200")]
    [InlineData("s/n")]
    [InlineData("Apto 101 - Bloco 2")]
    [InlineData("Quadra 12 Lote 34")]
    [InlineData("Rio de Janeiro")]
    [InlineData("Estr. do Rio Grande")]
    [InlineData("")]
    [InlineData(null)]
    public void Real_address_parts_pass(string? text)
    {
        Assert.True(CustomerText.IsSafeAddressPart(text));
    }

    [Theory]
    [InlineData("https://golpe.com")]
    [InlineData("acesse golpe.com")]
    [InlineData("golpe.com.br")]
    [InlineData("bit.ly/abc")]
    [InlineData("www.golpe")]
    [InlineData("suporte@golpe")]
    [InlineData("ligue 0800 123 4567")]
    [InlineData("(21) 99999-9999")]
    [InlineData("21.99999.9999")]
    [InlineData("0800/123/4567")]            // qualquer separador entre os dígitos
    [InlineData("0800,123,4567")]
    [InlineData("0800_123_4567")]
    [InlineData("21|99999|9999")]
    [InlineData("０８００１２３４５６７")]       // dígitos de largura total
    [InlineData("golpe.ru")]                  // qualquer terminação de domínio
    [InlineData("golpe.vip")]
    [InlineData("golpe.club")]
    [InlineData("acesse golpe.pro agora")]
    [InlineData("t.me/golpe")]
    [InlineData("Rua A\u0337\u0327\u0337")]  // acentos empilhados
    public void Address_parts_with_links_emails_or_phones_fail(string text)
    {
        Assert.False(CustomerText.IsSafeAddressPart(text));
    }

    [Theory]
    [InlineData(" Maria ", "Maria")]
    [InlineData("Maria José da Silva", "Maria")]                         // a saudação usa só o primeiro nome
    [InlineData("Seu pedido foi bloqueado procure o atendimento", "Seu")] // uma palavra não forma frase de golpe
    [InlineData("Ligue 0800 123 4567", null)]                             // nome inválido: "Olá!", sem nome
    [InlineData(null, null)]
    public void Emails_greet_with_the_first_name_only(string? name, string? expected)
    {
        Assert.Equal(expected, CustomerText.SafeFirstNameOrNull(name));
    }

    [Fact]
    public void Text_is_stored_in_one_form()
    {
        // NFC e sem espaços repetidos: o mesmo nome de teclados diferentes vira o mesmo texto no banco
        Assert.Equal("Jos\u00e9 da Silva", CustomerText.Normalize("  Jose\u0301   da  Silva "));
    }

    [Fact]
    public void Shipping_address_checks_every_free_text_field()
    {
        var address = new AtelieMarylu.Domain.Orders.ShippingAddress
        {
            PostalCode = "20000000", Street = "Rua A", Number = "1", District = "Centro", City = "Rio de Janeiro", State = "RJ",
        };
        Assert.True(address.IsSafeForEmail());

        foreach (var set in new Action<AtelieMarylu.Domain.Orders.ShippingAddress>[]
                 {
                     a => a.Street = "golpe.ru", a => a.Number = "08001234567", a => a.Complement = "a@b",
                     a => a.District = "www.golpe", a => a.City = "https://golpe",
                 })
        {
            var copy = new AtelieMarylu.Domain.Orders.ShippingAddress
            {
                PostalCode = address.PostalCode, Street = address.Street, Number = address.Number, District = address.District,
                City = address.City, State = address.State,
            };
            set(copy);
            Assert.False(copy.IsSafeForEmail());
        }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]       // quem exige o campo é o [Required]
    [InlineData("Maria José", true)]
    [InlineData("Maria acesse golpe.com", false)]
    public void Person_name_attribute(string? value, bool valid)
    {
        Assert.Equal(valid, new PersonNameAttribute().IsValid(value));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("Rua A", true)]
    [InlineData("golpe.com", false)]
    public void Safe_address_attribute(string? value, bool valid)
    {
        Assert.Equal(valid, new SafeAddressTextAttribute().IsValid(value));
    }
}
