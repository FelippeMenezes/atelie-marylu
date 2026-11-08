# Ateliê Marylu

Loja online do Ateliê Marylu, de costura criativa. Feita em .NET 10 (Blazor) com PostgreSQL.

## Como o código está organizado

A solution tem três projetos de código e dois de testes:

- `src/AtelieMarylu.Domain`: as regras do negócio. Não depende de nenhum outro projeto.
- `src/AtelieMarylu.Infrastructure`: banco de dados, migrations e integrações externas.
- `src/AtelieMarylu.Web`: as telas (Blazor), os serviços usados por elas e a configuração.
- `tests/AtelieMarylu.Tests`: testes automáticos.
- `tests/AtelieMarylu.E2E`: teste ponta a ponta, no navegador.

O caminho é sempre **tela → serviço → banco**: as telas não acessam o banco direto.

O site está em português do Brasil, espanhol e inglês. Para acrescentar um texto na tela, use `@L["Text in English"]`: o texto em inglês é a própria chave. Coloque a tradução em `Resources/SharedResource.pt-BR.resx` e `SharedResource.es.resx`. Os testes de `LocalizationTests` falham se faltar tradução.

## Pré-requisitos

- .NET SDK 10
- Docker (para o PostgreSQL)

## Como rodar

1. Suba o banco:

   ```bash
   cp .env.example .env        # edite e troque a senha
   docker compose up -d
   ```

2. Configure os segredos. Eles ficam fora do repositório, em `~/.microsoft/usersecrets`:

   ```bash
   cd src/AtelieMarylu.Web
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=127.0.0.1;Port=5440;Database=atelie_marylu;Username=atelie;Password=A_SENHA_DO_.ENV"
   dotnet user-secrets set "InitialAdmin:Email" "seu-email@exemplo.com"
   dotnet user-secrets set "InitialAdmin:Password" "uma-senha-forte"
   ```

   No primeiro login, o Admin precisa trocar essa senha.

3. Rode:

   ```bash
   dotnet run --project src/AtelieMarylu.Web --launch-profile https
   ```

   Abra https://localhost:7050. Em desenvolvimento, as migrations são aplicadas sozinhas e entram produtos de exemplo.

## Configuração da loja

Fica na seção `Store` do `appsettings.json`. Os dados legais (CNPJ ou CPF, endereço, contato) aparecem no rodapé e nas políticas, como exige a lei de comércio eletrônico. O WhatsApp vai só com números e DDI (ex.: `5521999999999`). Também ficam ali o desconto PIX, as parcelas e os prazos de pagamento.

Opcionais:
- `Turnstile:SiteKey` e `Turnstile:SecretKey`: anti-robô do Cloudflare no login e no cadastro.
- `GoogleLogin:ClientId` e `GoogleLogin:ClientSecret`: botão "Continuar com Google". As chaves são criadas no Google Cloud, com o endereço de retorno `/signin-google`.
- `Proxy:TrustedNetworks`: redes do proxy reverso em produção (ex.: `["10.0.0.0/8"]`).

Em produção, defina `AllowedHosts` com o domínio. A aplicação não sobe com `*`.

## Testes

```bash
dotnet test
```

Os testes de integração precisam de um Postgres. Sem a variável abaixo, eles são pulados:

```bash
ATELIE_TESTE_DB="Host=127.0.0.1;Port=5440;Username=atelie;Password=A_SENHA_DO_.ENV" dotnet test
```

Cada execução cria um banco temporário e o apaga no fim.

### Teste ponta a ponta (navegador)

Com a loja rodando em desenvolvimento, instale uma vez os navegadores do Playwright e rode:

```bash
dotnet build tests/AtelieMarylu.E2E
pwsh tests/AtelieMarylu.E2E/bin/Debug/net10.0/playwright.ps1 install chromium   # ou: ./playwright.sh install chromium
E2E_URL=https://localhost:7050 dotnet test tests/AtelieMarylu.E2E
```

## Migrations

```bash
dotnet tool restore
dotnet ef migrations add NameOfTheChange -p src/AtelieMarylu.Infrastructure -s src/AtelieMarylu.Web -o Data/Migrations
```

## Pagamento simulado

Até a integração com o Mercado Pago, o pagamento é simulado. Na página do pedido, em desenvolvimento, aparece o botão **Simular pagamento aprovado**. Ele usa o mesmo caminho que o webhook real vai usar.

## Produção

A loja está no ar em https://www.ateliemarylu.com.br, num VPS da Hostinger. Roda num único servidor com Docker: Caddy (HTTPS automático), a aplicação e o Postgres, definidos em `docker-compose.prod.yml`.

- **Configuração:** fica no `.env.producao`, só no servidor. O modelo, sem valores reais, é o `.env.producao.example`.
- **Migrations:** em produção, são aplicadas quando a aplicação sobe.
- **Publicação:** é feita à mão no servidor. O workflow `deploy-hostinger.yml` existe, mas só roda quando é disparado manualmente.
- **Backup:** o dia a dia fica com o backup automático semanal da Hostinger. Antes de cada publicação, o `deploy/backup.sh` salva o banco e as fotos.

## CI

A cada PR, o GitHub Actions faz o build (avisos viram erro), roda os testes com Postgres e bloqueia pacotes com vulnerabilidade conhecida. O CodeQL analisa a segurança do código.
