# Guia do projeto

Este guia explica o código da loja para quem está começando em C# e .NET. Para cada parte, ele diz o que ela faz, por que existe e se ficou onde deveria.

O código está em inglês e os comentários em português. O site funciona em português do Brasil, espanhol e inglês.

## 1. Solution e project: "é um projeto só?"

No dia a dia, "projeto" é a loja inteira. No .NET, a palavra tem um sentido técnico:

- **Solution** (`AtelieMarylu.slnx`): o sistema inteiro. Só reúne as partes.
- **Project** (cada arquivo `.csproj`): uma parte do sistema. Cada um compila separado e gera um arquivo `.dll`.

Pense num restaurante. O restaurante é um só, mas tem salão, cozinha e despensa. O cliente vê um único restaurante, e cada área tem sua função.

A loja tem cinco projects:

- **`AtelieMarylu.Domain`** é a receita: as regras do negócio.
- **`AtelieMarylu.Infrastructure`** é a despensa e os fornecedores: banco de dados e serviços externos.
- **`AtelieMarylu.Web`** é o salão e os garçons: as telas e os serviços que as atendem.
- **`AtelieMarylu.Tests`** é o controle de qualidade: testes automáticos.
- **`AtelieMarylu.E2E`** é o cliente oculto: um robô que usa a loja num navegador de verdade.

Na publicação, Domain, Infrastructure e Web viram **um único site**, no mesmo servidor e no mesmo endereço. Os dois projects de teste não vão para o site.

### Por que separar?

- **O compilador impede a bagunça.** O Domain não conhece o banco de dados. Se alguém tentar usar o banco numa regra de negócio, o código nem compila.
- **Testar fica mais fácil.** As regras do pedido são testadas sem abrir site nem ligar banco.
- **Trocar uma peça é mais barato.** Quando o Mercado Pago entrar no lugar do pagamento simulado, as telas não mudam.

### Quem pode usar quem

```
Web ──────────────> Infrastructure ──> Domain
 └────────────────────────────────────> Domain
```

As setas têm um sentido só. O Domain não aponta para ninguém: as regras do negócio não dependem de tecnologia nenhuma. Essa é a ideia mais importante de arquitetura em .NET.

Isso é declarado no `.csproj`, com `ProjectReference`.

## 2. O caminho de um clique

Veja o que acontece quando a cliente abre um produto:

1. O navegador pede o endereço `/produto/1/bolsa-de-tecido-floral`.
2. O **Caddy**, na porta da internet, recebe o pedido e passa para a aplicação.
3. A requisição percorre o **pipeline** (`WebApplicationExtensions.cs`): segurança, login, limite de tentativas.
4. O Blazor encontra a tela com esse endereço: `ProductDetails.razor`.
5. A tela chama o **serviço**: `ProductService.GetActiveAsync(id)`.
6. O serviço usa o **DbContext** para consultar o **PostgreSQL**.
7. O produto volta, e a tela vira HTML no navegador.

A regra é sempre **tela → serviço → banco**. Uma tela nunca fala direto com o banco, e o compilador ajuda nisso: as telas não importam os comandos do EF Core.

## 3. Arquivos na raiz

- **`AtelieMarylu.slnx`**: a lista de projects.
- **`Directory.Build.props`**: configurações iguais para todos os projects, como a versão do .NET.
- **`Directory.Packages.props`**: a versão de cada pacote NuGet, em um só lugar. Isso evita um project usar uma versão e outro usar outra.
- **`.editorconfig`**: as regras de estilo, como "campo privado começa com `_`". O editor avisa quando algo foge do padrão.
- **`dotnet-tools.json`**: ferramentas do projeto, como o `dotnet ef`, que cria migrations.
- **`.gitignore`**: o que o Git ignora, como senhas e arquivos gerados.
- **`.env.example`**: modelo do `.env` local, usado para subir o banco no seu computador.
- **`.env.producao.example`**: modelo das configurações do servidor, sem senhas.
- **`docker-compose.yml`**: sobe só o banco para desenvolver no seu computador.
- **`docker-compose.prod.yml`**: sobe o site completo no servidor.
- **`Dockerfile`**: a receita para montar a imagem do site.
- **`deploy/`**: configuração do Caddy e script de backup.
- **`.github/`**: automações do GitHub: testes a cada PR, análise de segurança (CodeQL), atualização de pacotes (Dependabot) e o deploy pela Hostinger, que só roda quando alguém manda.
- **`docs/`**: este guia e o `DEPLOY.md`, com o passo a passo do servidor.

## 4. Projeto Domain: as regras do negócio

**O que entra aqui:** o que continuaria valendo se a loja fosse feita em papel. "Um pedido cancelado não pode ser pago" é regra do negócio, seja qual for a tecnologia.

- **`Orders/Order.cs`**: o pedido e suas regras (pagar, enviar, cancelar). É o arquivo mais importante do Domain.
  - Repare no `private set`: de fora, ninguém muda o `Status` direto. Quem quer mudar precisa chamar `Cancel()`, e esse método confere a regra antes. Em cursos, isso é chamado de **encapsulamento**.
- **`Orders/OrderItem.cs`**: uma linha do pedido. Copia nome e preço na hora da compra, para o pedido antigo não mudar se o preço do produto mudar depois.
- **`Orders/ShippingAddress.cs`**: o endereço guardado no pedido.
- **`Orders/OrderStatus.cs`**: as etapas do pedido. É um `enum`: uma lista fechada de opções.
- **`Orders/OrderRuleException.cs`**: o erro lançado quando uma regra é violada. Guarda o texto e os valores separados, para a tela poder traduzir a mensagem.
- **`Payments/Payment.cs`**: uma tentativa de pagamento. Um pedido pode ter várias.
- **`Payments/IPaymentGateway.cs`**: um **contrato**. Interface é uma lista do que um provedor de pagamento precisa saber fazer, sem dizer como. Por convenção, o nome começa com `I`.
- **`Payments/PixDiscountCalculator.cs`**: uma única conta de desconto, usada pela vitrine, pelo checkout e pelo pedido.
- **`Payments/MoneyExtensions.cs`**: `ToBrl()`, que escreve o preço sempre em reais, em qualquer idioma.
- **`Payments/ChargeRequest.cs`, `PaymentStart.cs`, `PaymentNotification.cs`**: as informações trocadas com o provedor. São `record`: tipos simples, feitos para carregar dados.
- **`Shipping/IShippingCalculator.cs`**: o contrato do cálculo de frete.
- **`Shipping/ShippingOption.cs`, `ShippingPackage.cs`**: os dados do frete.
- **`Shipping/PostalCode.cs`**: limpa e valida um CEP.
- **`Carts/Cart.cs`**: as regras do carrinho, como o limite de 20 unidades por item.
- **`Catalog/Product.cs`, `ProductImage.cs`**: o produto e suas fotos.
- **`Catalog/ProductRuleException.cs`**: o erro de regra ao salvar um produto.
- **`Auditing/AuditEntry.cs`**: quem fez o quê e quando.
- **`Auditing/AuditActions.cs`, `AuditEntities.cs`**: os nomes das ações, como `"order.created"`, em constantes. Um texto solto com erro de digitação passa despercebido; uma constante errada não compila.
- **`Security/AppRoles.cs`, `Permissions.cs`**: os perfis e as permissões do painel.
- **`Catalog/SlugGenerator.cs`**: gera o endereço amigável do produto.
- **`Orders/OrderCodeGenerator.cs`**: gera o código público do pedido, como `AML-7K3F9Q`.

### Um tipo por arquivo

O padrão do C# é **um tipo por arquivo, com o mesmo nome**. Assim, para achar `OrderItem`, basta abrir `OrderItem.cs`.

## 5. Projeto Infrastructure: o contato com o mundo de fora

**O que entra aqui:** o que depende de uma tecnologia específica, como PostgreSQL ou ViaCEP.

### Pasta `Data`

- **`ApplicationDbContext.cs`**: a ponte entre o C# e o banco. Cada `DbSet` representa uma tabela. É a principal classe do **Entity Framework Core (EF Core)**, a biblioteca que escreve o SQL para você.
- **`Configurations/`**: como cada classe vira tabela: tamanho das colunas, índices, relacionamentos. Cada tabela tem seu arquivo, no padrão `IEntityTypeConfiguration`.
- **`Migrations/`**: o histórico de mudanças do banco. Cada arquivo é um passo, como "criar a tabela" ou "apagar passkeys". Normalmente é gerado pelo `dotnet ef`. **Sempre leia o que foi gerado antes de aplicar:** ao renomear tabelas, o EF propôs apagá-las e criá-las de novo, o que destruiria os pedidos. Por isso, a migration `TranslateSchemaToEnglish` foi escrita à mão, só com renomeações.
- **`ApplicationUser.cs`**: o usuário, com os campos extras da loja, como o CPF. Fica aqui porque depende do Identity, o sistema de login do ASP.NET.
- **`ProcessedNotification.cs`**: lembra quais avisos de pagamento já foram tratados, para um aviso repetido não ser aplicado duas vezes.
- **`DatabaseSeeder.cs`**: prepara o banco ao iniciar: aplica migrations, cria os perfis e o primeiro Admin.

### Pasta `Integrations`

- **`SimulatedPaymentGateway.cs`**: finge ser o provedor de pagamento. É uma implementação de `IPaymentGateway`.
- **`SimulatedShippingCalculator.cs`**: tabela fixa de frete. Fica aqui, e não no Domain, porque é uma implementação. O Domain guarda só o contrato.
- **`PostalCodeLookup.cs`, `PostalCodeAddress.cs`**: buscam o endereço no ViaCEP.
- **`Turnstile.cs`, `TurnstileOptions.cs`**: a verificação anti-robô do Cloudflare.
- **`PixQrCodeGenerator.cs`**: gera a imagem do QR code do PIX.

### `DependencyInjection.cs`

Registra tudo o que o project oferece, para o `Program.cs` só precisar chamar `AddInfrastructure`. O próximo capítulo explica o que é "registrar".

## 6. Projeto Web: telas e serviços

### Injeção de dependência

É o conceito mais usado no .NET. Em vez de a classe criar o que precisa, ela **pede** no construtor, e o .NET entrega:

```csharp
public class ProductService(IDbContextFactory<ApplicationDbContext> dbFactory, AuditService audit)
```

É como pedir um prato: você não vai à cozinha buscar ingredientes. Isso permite trocar uma peça sem mexer em quem a usa, e é assim que `IPaymentGateway` vai passar do simulado para o Mercado Pago.

Na tela, o pedido é feito com `@inject`.

Ao registrar um serviço, você escolhe por quanto tempo ele vive:

- **`AddSingleton`**: um só para o site inteiro.
- **`AddScoped`**: um por conexão do usuário. No Blazor Server, dura enquanto a aba estiver aberta.

### `Program.cs` e a pasta `Extensions`

O `Program.cs` é o ponto de partida. É curto de propósito e lê como um índice. Os detalhes ficam em:

- **`Extensions/ServiceCollectionExtensions.cs`**: registra os serviços, por assunto.
- **`Extensions/WebApplicationExtensions.cs`**: monta o pipeline e os endereços. Aqui, **a ordem das linhas importa**.

### Pasta `Components`: as telas

Cada arquivo `.razor` mistura HTML com C#. A parte de C# fica no bloco `@code`.

- **`App.razor`**: a casca do HTML, com o `<head>` e os scripts.
- **`Routes.razor`**: decide qual tela abrir para cada endereço.
- **`_Imports.razor`**: os `@using` válidos para todas as telas da pasta.
- **`Layout/`**: o que se repete em toda página: cabeçalho, rodapé, botão do WhatsApp.
- **`Pages/`**: as páginas, cada uma com seu `@page "/cart"`.
  - **`Pages/Store/`**: o que a cliente usa: produto, carrinho, checkout, pedidos.
  - **`Pages/Admin/`**: o que a loja usa. Fica protegido por permissões.
  - **`Pages/Legal/`**: política de privacidade, termos e trocas.
- **`Shared/`**: pedaços reutilizados, como `ProductPrice.razor`. Se aparece em duas telas, vira componente.
- **`Account/`**: login, cadastro e "Minha conta". A maior parte veio do modelo do .NET.
  - **Removidas de propósito:** o login por passkey, a verificação em duas etapas e as páginas que dependem de e-mail, como "esqueci minha senha". Sem serviço de e-mail, essas páginas prometiam um envio que não acontecia. O .NET pode gerá-las de novo quando a loja tiver e-mail.
  - Quem esquece a senha precisa falar com a loja. Ainda não existe um botão no painel para redefini-la.

### Pasta `Services`: os atendentes

As telas mostram; os serviços fazem. Um serviço pode ser usado por várias telas e ser testado sem navegador.

- **`OrderService.cs`**: criar pedido, pagar, cancelar, baixar estoque.
- **`ProductService.cs`**: vitrine, consultas e o cadastro de produto.
- **`CustomerService.cs`**: consultas de clientes para o painel.
- **`DashboardService.cs`**: os números da página inicial do painel.
- **`AuditService.cs`**: grava e consulta o histórico.
- **`CurrentUserService.cs`**: descobre quem está logado.
- **`AccessService.cs`**: permissões e grupos de funcionários.
- **`CartService.cs`**: guarda o carrinho no navegador.
- **`ImageService.cs`**: valida e converte as fotos enviadas.
- **`PersonalDataService.cs`**: exportar e excluir dados, como exige a LGPD.
- **`OrderExpirationService.cs`**: tarefa que roda sozinha a cada 5 minutos e cancela pedidos não pagos.
- **`OrderEvents.cs`**: avisa as telas abertas quando um pedido muda.
- **`SearchPattern.cs`**: prepara o texto das buscas do painel, para `%` e `_` serem procurados como texto normal.
- **`CustomerData.cs`, `CustomerSummary.cs`, `ProductInput.cs`** e parecidos: `record` que levam só os dados necessários entre o serviço e a tela. Em cursos, esse tipo é chamado de **DTO**.

### Outras pastas

- **`Endpoints/`**: endereços que não são telas, como `/robots.txt`, `/sitemap.xml`, o aviso de pagamento e a troca de idioma.
- **`Configuration/`**: classes que leem o `appsettings.json`. Esse uso de classes para configuração é o **Options pattern**.
- **`Security/`**: cabeçalhos de proteção do site e a troca obrigatória de senha.
- **`Localization/`**: a lista de idiomas e a tradução das mensagens de validação e de login.
- **`Resources/`**: os arquivos de tradução. `SharedResource.cs`, na raiz do project, é o "endereço" deles.
- **`Validation/`**: validações próprias. `OptionalPhoneAttribute` existe porque o `[Phone]` do .NET recusa campo vazio, e isso travava o cadastro.
- **`wwwroot/`**: arquivos entregues direto ao navegador: CSS, imagens e JavaScript.
- **`appsettings.json`**: as configurações sem segredos. Senhas nunca entram aqui: ficam nos *user-secrets* no seu computador e no `.env.producao` no servidor.

## 7. Projetos de teste

- **`AtelieMarylu.Tests`** usa xUnit, e bUnit para testar componentes.
  - **Testes unitários** verificam uma peça isolada, sem banco.
  - **Testes de integração** (pasta `Integration`) usam um PostgreSQL de verdade.
- **`AtelieMarylu.E2E`** usa Playwright para abrir um navegador e fazer uma compra. Ele pegou erros que o compilador não vê, como um parâmetro renomeado numa tela e o cadastro travado pelo telefone vazio.

Para rodar, use `dotnet test`. Sem a variável `ATELIE_TESTE_DB`, os testes de integração são pulados. Sem `E2E_URL`, os testes de navegador também.

Teste pode falhar por dois motivos: o código está errado ou o próprio teste está errado. Ler a mensagem antes de mexer evita "consertar" o lado certo.

## 8. Convenções de nomes do C#

- **PascalCase** para classes, métodos e propriedades: `OrderService`, `CalculateShippingAsync`.
- **camelCase** para variáveis e parâmetros: `orderId`.
- **`_camelCase`** para campos privados: `_items`.
- **`I` no começo** de interfaces: `IPaymentGateway`.
- **`Async` no fim** de métodos assíncronos: `SaveAsync`.
- **Nomes por extenso:** `quantity` em vez de `qty`. Letras soltas só em expressões curtas, como `p => p.IsActive`.

## 9. Por que os nomes estão em inglês

Classes, métodos, tabelas e colunas estão em inglês, o padrão internacional do desenvolvimento. O que a cliente vê continua em português, e os comentários também, porque ajudam quem está aprendendo.

Alguns valores continuam em português de propósito:

- **Endereços do site**, como `/meus-pedidos` e `/painel`. Eles aparecem para a cliente e já são conhecidos pelo Google. O único que mudou foi o carrinho: `/carrinho` virou `/cart` e o antigo redireciona.
- **A pasta `uploads/produtos`**, porque as fotos já gravadas apontam para ela.
- **As variáveis do `.env.producao`**, como `LOJA_TELEFONE`. O `docker-compose.prod.yml` as traduz para os nomes novos, então o arquivo do servidor não precisa mudar.

## 10. Idiomas do site

O site inteiro, inclusive o painel, existe em português do Brasil, espanhol e inglês. O visitante escolhe em **PT · ES · EN**, no topo. Sem escolha, o site abre em português.

- **Como funciona:** nas telas, `@L["Carrinho"]` pede a tradução de "Carrinho". `L` é um `IStringLocalizer`, o tradutor padrão do .NET.
- **Onde ficam as traduções:** `Resources/SharedResource.es.resx` e `SharedResource.en.resx`. O texto em português é a própria chave. Se faltar tradução, aparece o português, e a tela não quebra.
- **Onde fica a escolha:** num cookie. A rota `/language/{código}` grava o cookie e volta para a mesma página.
- **Por que `ToBrl()`:** o formato de moeda comum (`ToString("C")`) seguiria o idioma, e R$ 39,90 viraria $39.90 em inglês. A loja vende em reais, então o preço usa sempre o formato brasileiro.
- **O que não é traduzido:** o que a loja cadastra ou o sistema grava, como nome e descrição de produto, nome de cliente e o histórico da auditoria.
- **Páginas legais:** têm texto completo nos três idiomas, dentro do próprio arquivo. As versões em espanhol e inglês avisam que, em caso de dúvida, vale o português.

**Para acrescentar um texto novo:** escreva `@L["Texto em português"]` na tela e adicione a tradução nos dois arquivos `.resx`. Os testes de `LocalizationTests` falham se a tradução faltar.

## 11. O que ainda falta

- **Pagamento e frete reais:** hoje os dois são simulados. Antes de integrar, faltam o estorno atualizar o pedido, a proteção contra cobrança dupla, o CPF e os dados do destinatário.
- **Serviço de e-mail:** para confirmar pedidos e recuperar senha.
- **Redefinir senha pelo painel:** para o atendimento resolver quem esqueceu a senha.
