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

public class FluxoCompraTests
{
    private static string Url => Environment.GetEnvironmentVariable("E2E_URL")!.TrimEnd('/');

    [FactE2E]
    public async Task Visitante_monta_carrinho_cria_conta_e_paga()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var navegador = await playwright.Chromium.LaunchAsync();
        // Certificado de desenvolvimento do ASP.NET não é confiável para o navegador de teste
        var pagina = await (await navegador.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = "pt-BR" })).NewPageAsync();

        // 1. Vitrine → produto → carrinho, sem login
        await pagina.GotoAsync(Url);
        await pagina.GetByRole(AriaRole.Link, new() { Name = "Necessaire média" }).First.ClickAsync();
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Adicionar ao carrinho" }).ClickAsync();
        await pagina.GetByRole(AriaRole.Link, new() { Name = "Ver carrinho" }).ClickAsync();
        await Assertions.Expect(pagina.GetByText("Necessaire média")).ToBeVisibleAsync();

        // 2. Finalizar compra exige conta: cadastro e volta para o checkout
        await pagina.GetByRole(AriaRole.Link, new() { Name = "Finalizar compra" }).ClickAsync();
        await pagina.GetByRole(AriaRole.Link, new() { Name = "Criar uma conta" }).ClickAsync();
        var email = $"e2e-{Guid.NewGuid():N}@teste.local";
        await pagina.GetByLabel("Nome completo").FillAsync("Cliente E2E");
        await pagina.GetByLabel("E-mail").FillAsync(email);
        await pagina.GetByLabel("Senha", new() { Exact = true }).FillAsync("Senha@1234");
        await pagina.GetByLabel("Confirme a senha").FillAsync("Senha@1234");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Criar conta" }).ClickAsync();
        await pagina.WaitForURLAsync("**/checkout");

        // 3. Endereço, frete, aceite e confirmação
        await pagina.GetByLabel("CEP").FillAsync("20040-020");
        await pagina.GetByLabel("CEP").BlurAsync();
        await pagina.GetByLabel("Rua").FillAsync("Rua da Assembleia");
        await pagina.GetByLabel("Número").FillAsync("10");
        await pagina.GetByLabel("Bairro").FillAsync("Centro");
        await pagina.GetByLabel("Cidade").FillAsync("Rio de Janeiro");
        await pagina.GetByLabel("UF").FillAsync("RJ");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Calcular frete" }).ClickAsync();
        await pagina.GetByLabel("Li e aceito").CheckAsync();
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Confirmar pedido" }).ClickAsync();

        // 4. Pedido criado com PIX; o pagamento simulado confirma e a página atualiza sozinha
        await Assertions.Expect(pagina.GetByText("Pedido recebido!")).ToBeVisibleAsync();
        await Assertions.Expect(pagina.GetByAltText("QR code do PIX")).ToBeVisibleAsync();
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Simular pagamento aprovado" }).ClickAsync();
        await Assertions.Expect(pagina.GetByText("Pagamento confirmado!")).ToBeVisibleAsync();
    }

    [FactE2E]
    public async Task Paginas_restritas_exigem_login()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var navegador = await playwright.Chromium.LaunchAsync();
        var pagina = await (await navegador.NewContextAsync(new() { IgnoreHTTPSErrors = true })).NewPageAsync();

        foreach (var caminho in new[] { "/checkout", "/meus-pedidos", "/painel" })
        {
            await pagina.GotoAsync(Url + caminho);
            Assert.Contains("/Account/Login", pagina.Url);
        }
    }
}
