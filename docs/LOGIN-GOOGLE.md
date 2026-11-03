# Entrar com Google

A loja mostra o botão **Continuar com Google** no login e no cadastro. Ele só aparece quando as chaves do Google estão configuradas. Sem chaves, a loja funciona normalmente com e-mail e senha.

É gratuito e não exige contrato nem verificação do Google, porque a loja pede só o nome e o e-mail.

## Como funciona para a cliente

1. Ela clica em **Continuar com Google** e entra na tela do Google.
2. Na primeira vez, a loja mostra o e-mail recebido e pede para confirmar o nome (e o telefone, se quiser). A conta é criada com o e-mail do Google.
3. Nas próximas vezes, entra direto com um clique.

Regras de segurança:

- A loja nunca recebe a senha do Google. Recebe só o nome, o e-mail e um código da conta.
- Se já existir uma conta na loja com o mesmo e-mail, a loja **não** conecta sozinha. A pessoa entra com a senha e conecta em **Minha conta → Conta Google**. Assim, só quem sabe a senha decide quem mais pode entrar.
- E-mails que o Google não confirmou são recusados.
- Quem ativou a verificação em duas etapas na loja continua precisando dela.
- Em **Minha conta → Conta Google**, a pessoa conecta e desconecta o Google. A loja não deixa desconectar quando o Google é a única forma de entrar: antes, é preciso criar uma senha.

## Criar as chaves no Google Cloud

1. Acesse **console.cloud.google.com**, crie um projeto (ex.: `atelie-marylu`) e confira se ele está selecionado no topo.
2. Pesquise **Google Auth Platform** e clique em **Começar**:
   - **Nome do app:** Ateliê Marylu
   - **E-mail para suporte:** o e-mail da loja
   - **Público-alvo:** Externo
   - **Dados de contato:** o e-mail da loja
3. Em **Branding**:
   - **Página inicial:** `https://www.ateliemarylu.com.br`
   - **Política de privacidade:** `https://www.ateliemarylu.com.br/politica-de-privacidade`
   - **Termos de serviço:** `https://www.ateliemarylu.com.br/termos-de-uso`
   - **Domínios autorizados:** `ateliemarylu.com.br`
   - Não envie logotipo: com logotipo, o Google exige uma verificação que leva dias.
4. Em **Clientes → Criar cliente**:
   - **Tipo:** Aplicativo da Web
   - **Origens JavaScript autorizadas:** vazio
   - **URIs de redirecionamento autorizados:**
     - `https://www.ateliemarylu.com.br/signin-google`
     - `https://localhost:7050/signin-google` (para testar no seu computador)
5. Guarde o **ID do cliente** e a **Chave secreta do cliente** num gerenciador de senhas. A chave secreta só é mostrada uma vez.
6. Em **Público-alvo**, clique em **Publicar app**. Enquanto estiver em "Teste", só os usuários de teste conseguem entrar.

A chave do Google não vence. Se ela vazar, crie outra em **Clientes** e apague a antiga.

## Configurar no seu computador (para testar)

As chaves ficam nos *user-secrets*, fora do repositório. Rode um comando por vez:

```bash
cd ~/Felippe/atelie-marylu/src/AtelieMarylu.Web
dotnet user-secrets set "LoginGoogle:ClientId" "COLE_O_ID_AQUI"
read -rs "SEGREDO?Cole a chave secreta e aperte Enter: "
dotnet user-secrets set "LoginGoogle:ClientSecret" "$SEGREDO"; unset SEGREDO
```

O `read -rs` pede a chave sem mostrá-la na tela e sem guardá-la no histórico do terminal.

Rode a loja com `--launch-profile https` e abra `https://localhost:7050`. O endereço precisa ser exatamente esse, porque é o que foi cadastrado no Google.

## Configurar no servidor

No servidor, na pasta `~/atelie-marylu`, rode um comando por vez:

```bash
read -rp "Cole o ID do cliente e aperte Enter: " GID
sed -i "s|^GOOGLE_CLIENT_ID=.*|GOOGLE_CLIENT_ID=$GID|" .env.producao; unset GID
read -rsp "Cole a chave secreta e aperte Enter: " GSEGREDO; echo
sed -i "s|^GOOGLE_CLIENT_SECRET=.*|GOOGLE_CLIENT_SECRET=$GSEGREDO|" .env.producao; unset GSEGREDO
docker compose -f docker-compose.prod.yml --env-file .env.producao up -d
```

O servidor usa bash, por isso o `read` tem uma escrita diferente da usada no seu computador (zsh).

Se as linhas `GOOGLE_CLIENT_ID=` e `GOOGLE_CLIENT_SECRET=` ainda não existirem no `.env.producao`, acrescente-as antes:

```bash
grep -q '^GOOGLE_CLIENT_ID=' .env.producao || printf 'GOOGLE_CLIENT_ID=\nGOOGLE_CLIENT_SECRET=\n' >> .env.producao
```

O botão aparece assim que a loja reinicia.

## Problemas comuns

- **"Erro 400: redirect_uri_mismatch":** o endereço de volta não está cadastrado exatamente igual no cliente do Google (`https`, com `www` e sem barra no fim). Depois de corrigir, pode levar alguns minutos para valer.
- **"Acesso bloqueado: o app não concluiu o processo de verificação":** o app ainda está em "Teste". Clique em **Publicar app** em **Público-alvo**.
- **O botão não aparece:** a loja foi iniciada antes de configurar as chaves, ou um dos dois valores está vazio. Reinicie a loja.
