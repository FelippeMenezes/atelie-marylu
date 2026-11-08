using Microsoft.Playwright;

namespace AtelieMarylu.E2E;

// Teste ponta a ponta num navegador de verdade. Precisa da loja rodando em desenvolvimento
// (pagamento simulado) e dos navegadores do Playwright instalados. Veja o README.
//   E2E_URL=https://localhost:7050 dotnet test tests/AtelieMarylu.E2E
public sealed class FactE2EAttribute : FactAttribute
{
    public FactE2EAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("E2E_URL")))
            Skip = "Defina E2E_URL (ex.: https://localhost:7050) para rodar os testes ponta a ponta.";
    }
}

public class PurchaseFlowTests
{
    private static string Url => Environment.GetEnvironmentVariable("E2E_URL")!.TrimEnd('/');

    [FactE2E]
    public async Task Visitor_builds_cart_creates_account_and_pays()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        // Certificado de desenvolvimento do ASP.NET não é confiável para o navegador de teste
        var page = await (await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "pt-BR" })).NewPageAsync();

        // 1. Vitrine → produto → carrinho, sem login
        await page.GotoAsync(Url);
        await page.GetByRole(AriaRole.Link, new() { Name = "Necessaire média" }).First.ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Adicionar ao carrinho" }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Ver carrinho" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Necessaire média")).ToBeVisibleAsync();

        // 2. Finalizar compra exige conta: cadastro e volta para o checkout
        await page.GetByRole(AriaRole.Link, new() { Name = "Finalizar compra" }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Criar uma conta" }).ClickAsync();
        var email = $"e2e-{Guid.NewGuid():N}@teste.local";
        await page.GetByLabel("Nome completo").FillAsync("Cliente E2E");
        await page.GetByLabel("E-mail").FillAsync(email);
        await page.GetByLabel("Senha", new() { Exact = true }).FillAsync("Senha@1234");
        await page.GetByLabel("Confirme a senha").FillAsync("Senha@1234");
        await page.GetByRole(AriaRole.Button, new() { Name = "Criar conta" }).ClickAsync();
        await page.WaitForURLAsync("**/checkout");

        // 3. Endereço, frete, aceite e confirmação
        await page.GetByLabel("CEP").FillAsync("20040-020");
        await page.GetByLabel("CEP").BlurAsync();
        await page.GetByLabel("Rua").FillAsync("Rua da Assembleia");
        await page.GetByLabel("Número").FillAsync("10");
        await page.GetByLabel("Bairro").FillAsync("Centro");
        await page.GetByLabel("Cidade").FillAsync("Rio de Janeiro");
        await page.GetByLabel("UF").FillAsync("RJ");
        await page.GetByRole(AriaRole.Button, new() { Name = "Calcular frete" }).ClickAsync();
        await page.GetByLabel("Li e aceito").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirmar pedido" }).ClickAsync();

        // 4. Pedido criado com PIX; o pagamento simulado confirma e a página atualiza sozinha
        await Assertions.Expect(page.GetByText("Pedido recebido!")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByAltText("QR code do PIX")).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Simular pagamento aprovado" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Pagamento confirmado!")).ToBeVisibleAsync();
    }

    [FactE2E]
    public async Task Restricted_pages_require_login()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        var page = await (await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true })).NewPageAsync();

        foreach (var path in new[] { "/checkout", "/my-orders", "/admin" })
        {
            await page.GotoAsync(Url + path);
            Assert.Contains("/Account/Login", page.Url);
        }
    }
}
