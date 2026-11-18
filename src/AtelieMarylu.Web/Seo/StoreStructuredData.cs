using System.Text.Json;
using AtelieMarylu.Web.Configuration;

namespace AtelieMarylu.Web.Seo;

// Dados estruturados (schema.org) da loja, para a página inicial. Dizem ao Google, num formato que ele lê
// sem adivinhar, o nome, o logo, o contato e as redes sociais da loja. Ajudam o painel de informações
// da marca na busca e ligam o site ao Instagram.
// "OnlineStore" é um tipo de Organization aceito pelo Google: https://developers.google.com/search/docs/appearance/structured-data/organization
public static class StoreStructuredData
{
    public static string Build(ShopOptions store, string baseUrl)
    {
        baseUrl = baseUrl.TrimEnd('/');
        var data = new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "OnlineStore",
            ["name"] = store.Name,
            ["url"] = $"{baseUrl}/",
            ["logo"] = $"{baseUrl}/img/logo.png",
        };

        // Campos vazios ficam de fora: o Google reclama de valor em branco
        if (!string.IsNullOrWhiteSpace(store.LegalName)) data["legalName"] = store.LegalName;
        if (!string.IsNullOrWhiteSpace(store.ContactEmail)) data["email"] = store.ContactEmail;
        if (!string.IsNullOrWhiteSpace(store.Phone)) data["telephone"] = store.Phone;
        if (!string.IsNullOrWhiteSpace(store.Address)) data["address"] = store.Address;
        if (!string.IsNullOrWhiteSpace(store.Instagram)) data["sameAs"] = new[] { $"https://www.instagram.com/{store.Instagram}" };

        // Espelha a página /returns-policy: direito de arrependimento de 7 dias (CDC, art. 49), devolução
        // pelo correio, sem custo para a cliente. O Google exige a categoria e, com prazo, o número de dias.
        // Se a política mudar, mude aqui também.
        data["hasMerchantReturnPolicy"] = new Dictionary<string, object>
        {
            ["@type"] = "MerchantReturnPolicy",
            ["applicableCountry"] = "BR",
            ["returnPolicyCategory"] = "https://schema.org/MerchantReturnFiniteReturnWindow",
            ["merchantReturnDays"] = 7,
            ["returnMethod"] = "https://schema.org/ReturnByMail",
            ["returnFees"] = "https://schema.org/FreeReturn",
            ["merchantReturnLink"] = $"{baseUrl}/returns-policy",
        };

        // O encoder padrão escapa <, > e &: um texto da configuração não consegue fechar a tag <script>
        return JsonSerializer.Serialize(data);
    }
}
