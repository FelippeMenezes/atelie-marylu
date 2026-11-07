using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using AtelieMarylu.Web.Configuration;
using AtelieMarylu.Web.Validation;

namespace AtelieMarylu.Tests;

public class ValidationTests
{
    // Um [Range(typeof(decimal), "0.01", ...)] lido em pt-BR (vírgula decimal) lança exceção ao validar
    // e derruba a página do Blazor. O teste valida todos os [Range] da aplicação nessa cultura.
    [Fact]
    public void All_ranges_work_in_pt_BR_culture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var properties = typeof(ShopOptions).Assembly.GetTypes()
                .SelectMany(t => t.GetProperties(all))
                .Select(p => (Property: p, Range: p.GetCustomAttribute<RangeAttribute>()))
                .Where(x => x.Range is not null)
                .ToList();

            Assert.NotEmpty(properties);
            foreach (var (property, range) in properties)
            {
                // Valor não nulo do tipo da propriedade: com null o atributo nem chega a ler os limites
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var amount = Activator.CreateInstance(type);
                var isError = Record.Exception(() => range!.IsValid(amount));
                Assert.True(isError is null, $"{property.DeclaringType!.Name}.{property.Name}: {isError?.Message}");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("(21) 99999-9999", true)]
    [InlineData("não é telefone", false)]
    public void Optional_phone_accepts_empty_and_rejects_invalid(string? value, bool valid)
    {
        Assert.Equal(valid, new OptionalPhoneAttribute().IsValid(value));
    }
}
