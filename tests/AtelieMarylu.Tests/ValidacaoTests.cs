using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using AtelieMarylu.Web.Configuracao;

namespace AtelieMarylu.Tests;

public class ValidacaoTests
{
    // Um [Range(typeof(decimal), "0.01", ...)] lido em pt-BR (vírgula decimal) lança exceção ao validar
    // e derruba a página do Blazor. O teste valida todos os [Range] da aplicação nessa cultura.
    [Fact]
    public void Todos_os_Range_funcionam_na_cultura_pt_BR()
    {
        var culturaAnterior = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            const BindingFlags todas = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var propriedades = typeof(LojaOptions).Assembly.GetTypes()
                .SelectMany(t => t.GetProperties(todas))
                .Select(p => (Propriedade: p, Range: p.GetCustomAttribute<RangeAttribute>()))
                .Where(x => x.Range is not null)
                .ToList();

            Assert.NotEmpty(propriedades);
            foreach (var (propriedade, range) in propriedades)
            {
                // Valor não nulo do tipo da propriedade: com null o atributo nem chega a ler os limites
                var tipo = Nullable.GetUnderlyingType(propriedade.PropertyType) ?? propriedade.PropertyType;
                var valor = Activator.CreateInstance(tipo);
                var erro = Record.Exception(() => range!.IsValid(valor));
                Assert.True(erro is null, $"{propriedade.DeclaringType!.Name}.{propriedade.Name}: {erro?.Message}");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = culturaAnterior;
        }
    }
}
