# Plano do Ateliê Marylu

Transformar o Instagram [@ateliemarylurj](https://www.instagram.com/ateliemarylurj/) em uma loja online, com frete e pagamento por PIX e cartão, e depois em um app.

## Decisões

- **Tudo em .NET 10.** O front-end é **Blazor Web App**, sem React.
  - As páginas públicas (vitrine, produto, políticas) são **SSR estático**: rápidas, indexáveis pelo Google e sem conexão aberta com o servidor.
  - Só carrinho, checkout, página do pedido e painel são **interativos** (Interactive Server), além de pequenas "ilhas" como o botão "adicionar ao carrinho".
- **PostgreSQL + EF Core** (migrations em `src/AtelieMarylu.Web/Data/Migrations`).
- **ASP.NET Core Identity** para contas, senhas, bloqueio por tentativas e permissões.
- **Integrações por interface:** `IGatewayPagamento` e `ICalculadoraFrete`. Hoje rodam implementações simuladas; trocar pelo Mercado Pago e pelo Melhor Envio não muda nenhuma tela.
- **Sem serviço de e-mail por enquanto.** Nada no sistema depende de envio de e-mail; os alertas ficam no painel e no log.

## Estrutura

- `src/AtelieMarylu.Domain`: regras de negócio em C# puro (Pedido, Carrinho, Pagamento, Frete, Permissões, Auditoria). Não conhece banco nem web.
- `src/AtelieMarylu.Web`: Blazor, banco, Identity, serviços e integrações.
  - `Services/PedidoService.cs`: pedido, estoque, frete, pagamento, webhook, expiração.
  - `Services/AcessoService.cs`: grupos e permissões de funcionários.
  - `Services/AuditoriaService.cs`: registro de quem fez o quê.
  - `Services/ExpiracaoPedidosService.cs`: tarefa que cancela pedidos não pagos.
  - `Integracoes/`: pagamento simulado, webhook, ViaCEP, Turnstile, QR code PIX, SEO.
  - `Seguranca/`: cabeçalhos HTTP e troca de senha obrigatória.
  - `Configuracao/`: dados da loja (`LojaOptions`) e fuso horário.
- `tests/AtelieMarylu.Tests`: domínio, componentes (bUnit), imagens e integração com Postgres real.
- `tests/AtelieMarylu.E2E`: fluxo de compra num navegador real (Playwright).
- `landing/`: landing page estática (GitHub Pages).

## Perfis de acesso

- **Visitante:** vê produtos e monta o carrinho sem cadastro (o carrinho fica no navegador, criptografado).
- **Cliente:** criado no cadastro. Finaliza compras e acompanha os próprios pedidos. Ao clicar em "Finalizar compra" sem login, vai para o login/cadastro e volta para o checkout.
- **Funcionário:** um cliente que recebeu permissões do Admin, **individualmente** ou por **grupo**.
- **Admin:** tudo. Produtos, clientes e pedidos, usuários, grupos, permissões e auditoria. O Admin inicial precisa trocar a senha no primeiro acesso.

## Fluxo do pedido

1. Carrinho (sem login) → checkout (exige login) → endereço (preenchido pelo CEP) → frete → PIX (com desconto) ou cartão → aceite dos termos.
2. O pedido é criado como **Aguardando pagamento**, com código público (ex.: `AML-7K3F9Q`) e prazo para pagar. O estoque é reservado na hora.
3. O provedor de pagamento avisa pelo **webhook**: o pedido fica **Pago** e a tela do cliente se atualiza sozinha.
4. Sem pagamento no prazo (padrão 2 h no site, 72 h para pedidos do painel), o pedido é cancelado e o estoque volta.
5. Painel: **Em preparo** → **Enviado** (com rastreio) → **Entregue**.
6. Qualquer edição de itens ou frete invalida o pagamento pendente. Se o cliente pagar um valor antigo, o pedido **não** é confirmado e aparece um alerta no painel.

## Fases

### Fase 0: Landing page
- [x] Landing com a identidade visual do logo
- [x] Publicação no GitHub Pages

### Fase 1: MVP da loja
- [x] Solução .NET 10 (Domain, Web, Tests) com PostgreSQL
- [x] Vitrine, produto, carrinho sem login; cadastro só ao finalizar
- [x] Checkout com endereço, frete simulado, PIX/cartão
- [x] Pagamento simulado com webhook
- [x] Painel: produtos, pedidos, clientes, usuários, permissões individuais e grupos
- [x] Testes de domínio e integração

### Fase 1.1: Revisão de UX, segurança e boas práticas
Bugs corrigidos:
- [x] Pagamento com valor antigo depois de editar o pedido (pagamento invalidado + conferência de valor)
- [x] Disputa entre webhook e funcionário (concorrência otimista com `xmin` + nova tentativa no webhook)
- [x] Estoque preso por pedidos não pagos (expiração automática + limite de 3 pendentes por cliente)
- [x] Horário exibido no fuso do servidor (agora sempre Brasília)
- [x] Exportação LGPD sem nome, CPF e pedidos

Segurança:
- [x] Acesso revogado vale em até 1 minuto (antes: 30)
- [x] Proxy confiável configurável (`Proxy:RedesConfiaveis`) para o limite por IP funcionar atrás de proxy
- [x] `AllowedHosts` obrigatório em produção (a aplicação não sobe com `*`)
- [x] Fotos regravadas do zero (SkiaSharp): valida o conteúdo, remove EXIF/GPS, limita dimensões
- [x] Turnstile (anti-robô do Cloudflare) no login e no cadastro, ativado por configuração
- [x] Auditoria de pedidos, produtos, acessos e pagamentos, com tela para o Admin
- [x] Troca de senha obrigatória do Admin inicial
- [x] Webhook idempotente (mesmo evento nunca é aplicado duas vezes)
- [x] CI com build, testes e bloqueio de pacotes vulneráveis; CodeQL; Dependabot

UX:
- [x] SSR estático nas páginas públicas
- [x] Open Graph (prévia com foto no Instagram/WhatsApp), URL amigável, dados estruturados, sitemap e robots
- [x] Frete calculado no produto e no carrinho; endereço preenchido pelo CEP (ViaCEP)
- [x] PIX com QR code, botão copiar, prazo e atualização automática; tela "Pedido recebido"
- [x] Desconto no PIX e parcelamento exibidos; várias fotos por produto; prazo de produção (sob encomenda)
- [x] Selos "Últimas unidades" e "Esgotado"; seletor − / +; esqueleto de carregamento
- [x] Menu para celular; botão de WhatsApp; contraste WCAG AA
- [x] Rodapé com dados legais, trocas e devoluções (CDC), privacidade (LGPD), termos e aceite no checkout

Boas práticas:
- [x] Logs estruturados (Serilog), `/health`, código de pedido não sequencial
- [x] Testes de componentes (bUnit) e ponta a ponta (Playwright)

### Fase 2: Pagamentos reais (Mercado Pago)
- [ ] Credenciais de teste (sandbox) e `MercadoPagoGateway : IGatewayPagamento`
- [ ] PIX (QR code real) e Checkout Pro para cartão (parcelamento)
- [ ] Webhook: validar `x-signature` e consultar o pagamento na API antes de confirmar
- [ ] Estorno pelo painel (hoje o alerta pede estorno manual)

### Fase 3: Frete real (Melhor Envio)
- [ ] `MelhorEnvioFrete : ICalculadoraFrete`
- [ ] CPF obrigatório no checkout (exigido para etiqueta)
- [ ] Gerar etiqueta pelo painel e preencher o rastreio automaticamente

### Fase 4: E-mails (quando houver provedor)
- [ ] Provedor (Resend, Brevo, SendGrid ou SMTP)
- [ ] Confirmação de conta (`RequireConfirmedAccount = true`) e "esqueci minha senha" (hoje o link está escondido)
- [ ] Avisos de pedido: recebido, pago, enviado, cancelado
- [ ] "Avise-me quando chegar" para produtos esgotados
- [ ] Alertas de pagamento divergente por e-mail
- [ ] No cadastro com e-mail já existente, avisar o dono por e-mail em vez de mostrar na tela

### Fase 5: Produção
- [ ] Hospedagem e Postgres gerenciado com backup diário
- [ ] Domínio próprio com HTTPS e HSTS de longa duração
- [ ] Variáveis de ambiente: connection string, `AdminInicial__*`, `AllowedHosts`, `Loja__*`, `Turnstile__*`, `Proxy__RedesConfiaveis__0`
- [ ] Migrations no deploy (bundle de migrations)
- [ ] Criptografar as chaves de Data Protection (certificado) e mover fotos para storage externo (R2/S3)
- [ ] Com mais de um servidor: backplane para os avisos em tempo real (`PedidoEventos`) e sticky sessions
- [ ] Ativar proteção de branch na `main` exigindo CI verde
- [ ] Retenção de logs de acesso por 6 meses (Marco Civil)

### Fase 6: Melhorias da loja
- [ ] Categorias e busca; variações (cor/tecido) com estoque próprio
- [ ] Cupons; relatórios de vendas
- [ ] Limpeza de fotos órfãs (enviadas e não salvas)

### Fase 7: App
- [ ] API REST (Minimal APIs) reaproveitando os serviços
- [ ] App em .NET MAUI

## Checklist de segurança

- Senhas com no mínimo 8 caracteres, com hash; bloqueio de 15 min após 5 erros; Turnstile opcional
- Limite de 10 tentativas por minuto por IP no login/cadastro/webhook (respeitando proxy confiável)
- Cookie de login `HttpOnly`, `Secure`, `SameSite=Lax`; antiforgery nos formulários
- CSP restritiva, `X-Frame-Options: DENY`, `nosniff`, `Referrer-Policy`, `Permissions-Policy`, HSTS em produção
- Autorização por permissão em cada página e nova checagem antes de cada ação; revogação em 1 min
- Cliente só vê os próprios pedidos; código de pedido não sequencial
- Preço, desconto, frete e estoque calculados no servidor; baixa de estoque atômica; concorrência otimista nos pedidos
- Webhook idempotente, com conferência de valor; simulado só em desenvolvimento
- Upload: conteúdo validado, imagem regravada sem metadados, limite de tamanho e de pixels, nome gerado pelo servidor
- Nenhum segredo no repositório; Postgres só em 127.0.0.1; `AllowedHosts` obrigatório em produção
- Auditoria imutável de ações do painel e eventos de pagamento
- Dependências auditadas no CI; CodeQL e Dependabot
- LGPD: exportação completa e exclusão com anonimização
