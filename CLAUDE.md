# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Loja online do Ateliê Marylu (costura criativa), feita em .NET 10 com Blazor Server e PostgreSQL.

## Convenções

- O código (tipos, membros, tabelas e colunas do banco) e as URLs/rotas são em inglês (ex.: `/admin/orders`, `/my-orders`, `/api/webhooks/payment`). Os comentários do código são em português. Ao renomear uma rota pública, mantenha o endereço antigo redirecionando com 301 em `Endpoints/OldAddressRedirectEndpoints.cs`.
- As branches seguem o padrão `AML-NNNN` (só o número).
- E-mail: o texto é um componente Razor em `Components/Emails` (com `L["..."]`, renderizado pelo `HtmlRenderer`), montado por um serviço do Web e entregue pelo `IEmailClient` (Domain), implementado pelo Resend na Infrastructure. Toda falha de entrega vira `EmailDeliveryException` (Domain), e as telas capturam só ela: sem `catch (Exception)` e sem exceções de HTTP/SMTP nas telas. As telas do Identity usam `IEmailSender<ApplicationUser>` (`AccountEmailSender`), e os links com token vêm do `Components/Account/AccountEmailLinks`. Fora de Development, a aplicação não sobe sem `Resend:ApiKey`/`Resend:From`. A loja **não exige confirmar o e-mail** (`RequireConfirmedAccount = false`, decisão do AML-0027): o cadastro manda só boas-vindas (`AccountEmailSender.TrySendWelcomeAsync`). Só a troca de e-mail e o "esqueci minha senha" usam link com token.

## Comandos

O SDK do .NET fica em `~/.dotnet` (está no PATH do zsh). O Postgres de desenvolvimento roda no Docker, e a porta vem de `POSTGRES_PORT` no `.env` (padrão 5440).

```bash
docker compose up -d                                              # Postgres de desenvolvimento
dotnet run --project src/AtelieMarylu.Web --launch-profile https  # https://localhost:7050
dotnet build -warnaserror                                         # o CI trata avisos como erro
dotnet test                                                       # sem ATELIE_TESTE_DB, os testes de integração são pulados
ATELIE_TESTE_DB="Host=127.0.0.1;Port=<porta>;Username=atelie;Password=<do .env>" dotnet test
dotnet test --filter "FullyQualifiedName~OrderTests"              # uma classe; use ~Classe.Metodo para um teste só
```

- Os testes de integração (`[FactWithDatabase]`, em `tests/AtelieMarylu.Tests/Integration/TestDatabase.cs`) criam um banco temporário para cada classe de teste, aplicam as migrations e apagam o banco no fim. A connection string não pode ter `Database`.
- O teste ponta a ponta (Playwright) precisa da loja rodando: `E2E_URL=https://localhost:7050 dotnet test tests/AtelieMarylu.E2E`. Instale o Chromium uma vez, pelo `playwright.ps1` ou `playwright.sh` gerado no build.
- Migrations: `dotnet tool restore`, depois `dotnet ef migrations add <Nome> -p src/AtelieMarylu.Infrastructure -s src/AtelieMarylu.Web -o Data/Migrations`. Elas são aplicadas sozinhas quando a aplicação sobe (`DatabaseSeeder.RunAsync`), inclusive em produção. Por isso, uma migration que mexe em dados de produção precisa de PR próprio e de backup (`deploy/backup.sh`).
- Os segredos (connection string, `InitialAdmin:*`) ficam no `dotnet user-secrets` do `src/AtelieMarylu.Web`. As versões dos pacotes ficam centralizadas no `Directory.Packages.props`. Não coloque `Version` nos `.csproj`.

## Arquitetura

São três projetos, mais os de testes:

- `AtelieMarylu.Domain`: entidades e regras de negócio, sem depender de nenhum outro projeto. As entidades protegem as próprias regras com setters privados e métodos (ex.: `Order.ConfirmPayment`, `Order.Cancel`, `ChangeQuantity`) e lançam `OrderRuleException` ou `ProductRuleException`. As telas capturam essas exceções e mostram a mensagem. Os contratos das integrações, `IPaymentGateway`, `IShippingCalculator` e `IEmailClient`, ficam aqui.
- `AtelieMarylu.Infrastructure`: `ApplicationDbContext`, configurações do EF (`Data/Configurations`), migrations e integrações. O `AddInfrastructure` registra o gateway de pagamento e o cálculo de frete **simulados**. Para passar a usar Mercado Pago e Melhor Envio, basta trocar esses dois registros, sem mudar nenhuma tela.
- `AtelieMarylu.Web`: componentes Blazor, serviços da aplicação (`Services/`), endpoints e configuração. O `Program.cs` é só um índice: cada assunto é um método de extensão em `Extensions/`.

Regras que envolvem vários arquivos:

- **Tela → serviço → banco.** Os componentes Razor nunca acessam o `ApplicationDbContext`. Os serviços recebem `IDbContextFactory<ApplicationDbContext>` e criam um contexto curto por operação (`await using var db = await dbFactory.CreateDbContextAsync(ct)`), porque um mesmo circuito do Blazor Server pode rodar operações em paralelo. As leituras usam `AsNoTracking()`.
- **Auditoria:** o registro é feito no mesmo `DbContext` da alteração (`AuditService.RecordAsync(db, ...)` antes do `SaveChangesAsync`), então grava na mesma transação. Os nomes das ações e entidades ficam em `Domain/Auditing`.
- **Concorrência:** o `Order` usa a coluna `xmin` do Postgres como versão da linha. O `OrderService` tenta de novo quando dá `DbUpdateConcurrencyException`. Nos outros casos, mostra a mensagem "alterado por outra pessoa".
- **Fluxo de pagamento:** quem marca o pedido como pago é o webhook, não a tela de "obrigado". O caminho é `PaymentWebhookEndpoints` → `IPaymentGateway.ReadNotificationAsync` → `OrderService.ProcessNotificationAsync`, com duplicatas barradas pelo `ProcessedNotification`. O webhook simulado só existe em desenvolvimento, e o botão "Simular pagamento aprovado" passa pelo mesmo caminho. Depois de uma mudança, o `OrderEvents` (evento em memória, singleton) avisa as telas abertas, como `MyOrder.razor`, para se atualizarem sozinhas. Isso só funciona com um único servidor. O `OrderExpirationService` (hosted service) cancela pedidos não pagos fora do prazo.
- **Autorização:** cada chave de `Domain/Security/Permissions.All` vira uma policy, que libera quem tem o papel `Admin` ou o claim `permission` correspondente (direto no usuário ou herdado de um grupo). As telas do painel herdam `[Authorize(Policy = Permissions.AccessAdmin)]` de `Pages/Admin/_Imports.razor`, e cada tela acrescenta a sua permissão específica.
- **Tradução:** o idioma padrão é pt-BR, mas **o texto em inglês é a própria chave**: `@L["Add to cart"]` (`IStringLocalizer<SharedResource>`, injetado em `_Imports.razor`). Não existe `.resx` de inglês. Todo texto novo precisa de tradução em `Resources/SharedResource.pt-BR.resx` e `SharedResource.es.resx`. O `LocalizationTests` procura os `L["..."]` nas telas e falha se faltar tradução ou se os `{0}` não baterem, mas não olha os `.cs`. Por isso, as mensagens de exceção (`OrderRuleException`, `AccessException`), os `ErrorMessage` de validação, `Order.Describe`, `Permissions.All` e os nomes de frete também são chaves em inglês e precisam de tradução. Os dados gravados no banco (ex.: `Order.ShippingName` de pedidos antigos) podem estar em português e aparecem sem tradução. O idioma vem só do cookie de cultura, definido em `/language/{code}`. O preço sempre aparece em reais (`ToBrl()`), em qualquer idioma. O site inteiro muda de idioma, inclusive o painel.
- As configurações da loja (seção `Store` → `ShopOptions`) são validadas quando a aplicação sobe. Em produção, a aplicação não sobe com `AllowedHosts=*`.

## CI e deploy

A cada PR, o GitHub Actions roda `dotnet build -c Release -warnaserror`, os testes com um Postgres de serviço, e falha se o `dotnet list package --vulnerable` encontrar algum pacote vulnerável. O CodeQL também roda. A produção é um único VPS da Hostinger rodando `docker-compose.prod.yml` (Caddy, aplicação e Postgres). A publicação é feita à mão no servidor: o `deploy-hostinger.yml` só roda quando disparado manualmente.
