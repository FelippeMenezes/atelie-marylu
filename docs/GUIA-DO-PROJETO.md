# Guia do projeto

Este guia explica o código da loja para quem está começando em C# e .NET. Para cada parte, ele diz o que ela faz, por que existe e se ficou onde deveria.

Os nomes aqui são os atuais, em português. Na próxima fase, o código passa para o inglês, e o guia será atualizado junto.

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
4. O Blazor encontra a tela com esse endereço: `ProdutoDetalhe.razor`.
5. A tela chama o **serviço**: `ProdutoService.ObterAtivoAsync(id)`.
6. O serviço usa o **DbContext** para consultar o **PostgreSQL**.
7. O produto volta, e a tela vira HTML no navegador.

A regra é sempre **tela → serviço → banco**. Antes desta organização, 15 telas falavam direto com o banco.

## 3. Arquivos na raiz

- **`AtelieMarylu.slnx`**: a lista de projects. Fica.
- **`Directory.Build.props`** (novo): configurações iguais para todos os projects, como a versão do .NET. Antes, elas estavam repetidas em cada `.csproj`.
- **`Directory.Packages.props`** (novo): a versão de cada pacote NuGet, em um só lugar. Isso evita um project usar uma versão e outro usar outra.
- **`.editorconfig`** (novo): as regras de estilo, como "campo privado começa com `_`". O editor passa a avisar quando algo foge do padrão.
- **`dotnet-tools.json`**: ferramentas do projeto, como o `dotnet ef`, que cria migrations. Fica.
- **`.gitignore`**: o que o Git ignora, como senhas e arquivos gerados. Fica.
- **`.env.example`** (novo): modelo do `.env` local. O README mandava copiar esse arquivo, mas ele não existia. Quem baixasse o projeto ficaria travado.
- **`.env.producao.example`**: modelo das configurações do servidor, sem senhas. Fica.
- **`docker-compose.yml`**: sobe só o banco para desenvolver no seu computador. Fica.
- **`docker-compose.prod.yml`**: sobe o site completo no servidor. Fica.
- **`Dockerfile`**: a receita para montar a imagem do site. Fica, e passou a incluir o Infrastructure.
- **`deploy/`**: configuração do Caddy e script de backup. Fica.
- **`landing/`**: a primeira página estática, do GitHub Pages. Fica, separada do sistema.
- **`.github/`**: automações do GitHub, como rodar os testes a cada PR. Fica.

## 4. Projeto Domain: as regras do negócio

**O que entra aqui:** o que continuaria valendo se a loja fosse feita em papel. "Um pedido cancelado não pode ser pago" é regra do negócio, seja qual for a tecnologia.

- **`Pedidos/Pedido.cs`**: o pedido e suas regras (pagar, enviar, cancelar). É o arquivo mais importante do Domain.
  - Repare no `private set`: de fora, ninguém muda o `Status` direto. Quem quer mudar precisa chamar `Cancelar()`, e esse método confere a regra antes. Em cursos, isso é chamado de **encapsulamento**.
- **`Pedidos/ItemPedido.cs`**: uma linha do pedido. Copia nome e preço na hora da compra, para o pedido antigo não mudar se o preço do produto mudar depois.
- **`Pedidos/EnderecoEntrega.cs`**: o endereço guardado no pedido.
- **`Pedidos/StatusPedido.cs`**: as etapas do pedido. É um `enum`: uma lista fechada de opções.
- **`Pedidos/RegraDePedidoException.cs`**: o erro lançado quando uma regra é violada. A mensagem dele é mostrada ao usuário.
- **`Pagamentos/Pagamento.cs`**: uma tentativa de pagamento. Um pedido pode ter várias.
- **`Pagamentos/IGatewayPagamento.cs`**: um **contrato**. Interface é uma lista do que um provedor de pagamento precisa saber fazer, sem dizer como. Por convenção, o nome começa com `I`.
- **`Pagamentos/CalculadoraDescontoPix.cs`** (novo): uma única conta de desconto. Antes, a mesma conta estava escrita em três lugares, e os valores podiam divergir.
- **`Pagamentos/DadosCobranca.cs`, `InicioPagamento.cs`, `NotificacaoPagamento.cs`**: as informações trocadas com o provedor. São `record`: tipos simples, feitos para carregar dados.
- **`Frete/ICalculadoraFrete.cs`**: o contrato do cálculo de frete.
- **`Frete/OpcaoFrete.cs`, `PacoteFrete.cs`**: os dados do frete.
- **`Frete/Cep.cs`**: limpa e valida um CEP.
- **`Carrinho/Carrinho.cs`**: as regras do carrinho, como o limite de 20 unidades por item.
- **`Catalogo/Produto.cs`, `ProdutoImagem.cs`**: o produto e suas fotos.
- **`Auditoria/RegistroAuditoria.cs`**: quem fez o quê e quando.
- **`Auditoria/AcoesAuditoria.cs`, `EntidadesAuditoria.cs`** (novos): nomes como `"pedido.criado"` viraram constantes. Um texto solto com erro de digitação passa despercebido; uma constante errada não compila.
- **`Seguranca/Papeis.cs`, `Permissoes.cs`**: os perfis e as permissões do painel.
- **`Texto.cs`**: gera o endereço amigável e o código do pedido. **Vai mudar:** `Texto` é um nome vago, e cada função deveria morar perto de quem a usa. Fica para a fase 2.

### Um tipo por arquivo

Antes, `Pedido.cs` tinha cinco tipos dentro dele. O padrão do C# é **um tipo por arquivo, com o mesmo nome**. Assim, para achar `ItemPedido`, basta abrir `ItemPedido.cs`.

## 5. Projeto Infrastructure: o contato com o mundo de fora

**O que entra aqui:** o que depende de uma tecnologia específica, como PostgreSQL ou ViaCEP.

### Pasta `Data`

- **`ApplicationDbContext.cs`**: a ponte entre o C# e o banco. Cada `DbSet` representa uma tabela. É a principal classe do **Entity Framework Core (EF Core)**, a biblioteca que escreve o SQL para você.
- **`Configurations/`** (novo): como cada classe vira tabela: tamanho das colunas, índices, relacionamentos. Antes, era um único método gigante. Agora, cada tabela tem seu arquivo, no padrão `IEntityTypeConfiguration`.
- **`Migrations/`**: o histórico de mudanças do banco. Cada arquivo é um passo, como "criar a tabela" ou "apagar passkeys". Não se edita à mão. É gerado pelo `dotnet ef`.
- **`ApplicationUser.cs`**: o usuário, com os campos extras da loja, como o CPF. Fica aqui porque depende do Identity, o sistema de login do ASP.NET.
- **`NotificacaoProcessada.cs`**: lembra quais avisos de pagamento já foram tratados, para um aviso repetido não ser aplicado duas vezes.
- **`SeedDados.cs`**: prepara o banco ao iniciar: aplica migrations, cria os perfis e o primeiro Admin.

### Pasta `Integrations`

- **`PagamentoSimulado.cs`**: finge ser o provedor de pagamento. É uma implementação de `IGatewayPagamento`.
- **`FreteSimulado.cs`**: tabela fixa de frete. **Foi movido:** estava no Domain, mas é uma implementação, não uma regra. Só o contrato fica no Domain.
- **`ConsultaCep.cs`, `EnderecoCep.cs`**: buscam o endereço no ViaCEP.
- **`Turnstile.cs`, `TurnstileOptions.cs`**: a verificação anti-robô do Cloudflare.
- **`PixQrCode.cs`**: gera a imagem do QR code do PIX.

### `DependencyInjection.cs` (novo)

Registra tudo o que o project oferece, para o `Program.cs` só precisar chamar `AddInfrastructure`. O próximo capítulo explica o que é "registrar".

## 6. Projeto Web: telas e serviços

### Injeção de dependência

É o conceito mais usado no .NET. Em vez de a classe criar o que precisa, ela **pede** no construtor, e o .NET entrega:

```csharp
public class ProdutoService(IDbContextFactory<ApplicationDbContext> dbFactory, AuditoriaService auditoria)
```

É como pedir um prato: você não vai à cozinha buscar ingredientes. Isso permite trocar uma peça sem mexer em quem a usa, e é assim que `IGatewayPagamento` vai passar do simulado para o Mercado Pago.

Na tela, o pedido é feito com `@inject`.

Ao registrar um serviço, você escolhe por quanto tempo ele vive:

- **`AddSingleton`**: um só para o site inteiro.
- **`AddScoped`**: um por conexão do usuário. No Blazor Server, dura enquanto a aba estiver aberta.

### `Program.cs` e a pasta `Extensions`

O `Program.cs` é o ponto de partida. Ele tinha 238 linhas com todos os assuntos misturados. **Foi dividido:** agora tem cerca de 30 linhas e lê como um índice. Os detalhes ficam em:

- **`Extensions/ServiceCollectionExtensions.cs`**: registra os serviços, por assunto.
- **`Extensions/WebApplicationExtensions.cs`**: monta o pipeline e os endereços. Aqui, **a ordem das linhas importa**.

### Pasta `Components`: as telas

Cada arquivo `.razor` mistura HTML com C#. A parte de C# fica no bloco `@code`.

- **`App.razor`**: a casca do HTML, com o `<head>` e os scripts.
- **`Routes.razor`**: decide qual tela abrir para cada endereço.
- **`_Imports.razor`**: os `@using` válidos para todas as telas da pasta.
- **`Layout/`**: o que se repete em toda página: cabeçalho, rodapé, botão do WhatsApp.
- **`Pages/`**: as páginas, cada uma com seu `@page "/endereco"`.
  - **`Pages/Loja/`**: o que a cliente usa: produto, carrinho, checkout, pedidos.
  - **`Pages/Painel/`**: o que a loja usa. Fica protegido por permissões.
  - **`Pages/Legal/`**: política de privacidade, termos e trocas.
- **`Shared/`**: pedaços reutilizados, como `PrecoProduto.razor`. Se aparece em duas telas, vira componente.
- **`Account/`**: login, cadastro e "Minha conta". A maior parte veio do modelo do .NET.
  - As páginas `ForgotPassword`, `ResendEmailConfirmation`, `ConfirmEmail` e parecidas dependem de e-mail, que a loja ainda não tem. **Ficam** para quando houver um serviço de e-mail.

### Pasta `Services`: os atendentes

As telas mostram; os serviços fazem. Um serviço pode ser usado por várias telas e ser testado sem navegador.

- **`PedidoService.cs`**: criar pedido, pagar, cancelar, baixar estoque.
- **`ProdutoService.cs`** (novo): vitrine, consultas e o cadastro de produto, que antes estava dentro da tela.
- **`ClienteService.cs`** (novo): consultas de clientes para o painel.
- **`PainelService.cs`** (novo): os números da página inicial do painel.
- **`AuditoriaService.cs`**: grava e consulta o histórico.
- **`UsuarioAtual.cs`**: descobre quem está logado.
- **`AcessoService.cs`**: permissões e grupos de funcionários.
- **`CarrinhoService.cs`**: guarda o carrinho no navegador.
- **`ImagemService.cs`**: valida e converte as fotos enviadas.
- **`DadosPessoaisService.cs`**: exportar e excluir dados, como exige a LGPD.
- **`ExpiracaoPedidosService.cs`**: tarefa que roda sozinha a cada 5 minutos e cancela pedidos não pagos.
- **`PedidoEventos.cs`**: avisa as telas abertas quando um pedido muda.
- **`ResumoCliente.cs`, `ResumoPainel.cs`, `DadosProduto.cs`** e parecidos: `record` que levam dados entre o serviço e a tela. Em cursos, esse tipo é chamado de **DTO**.

### Outras pastas

- **`Endpoints/`** (novo): endereços que não são telas, como `/robots.txt`, `/sitemap.xml` e o aviso de pagamento.
- **`Configuracao/`**: classes que leem o `appsettings.json`. Esse uso de classes para configuração é o **Options pattern**.
- **`Seguranca/`**: cabeçalhos de proteção do site e a troca obrigatória de senha.
- **`wwwroot/`**: arquivos entregues direto ao navegador: CSS, imagens e JavaScript.
- **`appsettings.json`**: as configurações sem segredos. Senhas nunca entram aqui: ficam nos *user-secrets* no seu computador e no `.env.producao` no servidor.

## 7. Projetos de teste

- **`AtelieMarylu.Tests`** usa xUnit, e bUnit para testar componentes.
  - **Testes unitários** verificam uma peça isolada, sem banco.
  - **Testes de integração** (pasta `Integracao`) usam um PostgreSQL de verdade.
- **`AtelieMarylu.E2E`** usa Playwright para abrir um navegador e fazer uma compra.

Para rodar, use `dotnet test`. Sem a variável `ATELIE_TESTE_DB`, os testes de integração são pulados.

## 8. Convenções de nomes do C#

- **PascalCase** para classes, métodos e propriedades: `PedidoService`, `CalcularFrete`.
- **camelCase** para variáveis e parâmetros: `pedidoId`.
- **`_camelCase`** para campos privados: `_itens`.
- **`I` no começo** de interfaces: `IGatewayPagamento`.
- **`Async` no fim** de métodos assíncronos: `SalvarAsync`.
- **Nomes por extenso:** `quantidade` em vez de `qtd`. Letras soltas só em expressões curtas, como `p => p.Ativo`.

## 9. O que ainda vai mudar

- **Fase 2 (`AML-0014`):** o código passa para o inglês, incluindo tabelas e colunas do banco, com migration e backup. Os nomes de variáveis curtas e de métodos sem `Async` são corrigidos nessa fase. O nome `Carrinho` também muda, porque hoje a pasta e a classe têm o mesmo nome, o que o C# desaconselha.
- **Fase 3 (`AML-0015`):** textos para clientes em português do Brasil, com opção de espanhol e inglês. O preço continua em reais em qualquer idioma.
