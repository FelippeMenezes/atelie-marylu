# Publicar a loja num VPS (Hostinger)

A loja está em **https://www.ateliemarylu.com.br**, num VPS **Hostinger KVM 1** (1 vCPU, 4 GB de RAM, 50 GB de disco, Ubuntu 24.04, data center no Brasil). Tudo roda com Docker:

```
Internet ──443/80──> Caddy (HTTPS automático) ──> loja (.NET) ──> Postgres
```

Só o Caddy fica exposto à internet. A loja e o banco ficam numa rede interna do Docker.

O guia serve para qualquer VPS com Ubuntu 24.04. Só o passo 1 é específico da Hostinger.

Tempo estimado na primeira vez: 1 a 2 horas.

---

## Atenção ao terminal

Muitos passos têm comandos que rodam **no servidor**. Antes de colar, confira o prompt:

- `atelie@srv2118354:~$` é o servidor.
- `➜ atelie-marylu git:(main)` (ou parecido) é o seu computador.

Cole o `ssh` **sozinho**, espere o prompt do servidor aparecer e só depois cole o resto. Se colar tudo junto, os comandos rodam no seu computador.

---

## 1. Contratar o VPS

1. No seu computador, gere uma chave SSH. Ela substitui a senha e é muito mais segura. Defina uma *passphrase* quando for pedida:

   ```bash
   ssh-keygen -t ed25519 -f ~/.ssh/atelie_servidor -C "atelie-marylu"
   cat ~/.ssh/atelie_servidor.pub
   ```

   > O servidor atual usa a chave `~/.ssh/oracle_atelie`. O nome ficou da primeira tentativa, na Oracle Cloud. Nos comandos abaixo, troque o nome se usar outra chave.

2. Na Hostinger, contrate o **VPS KVM 1** (o plano de 12 meses sai mais barato).
3. Na configuração do VPS:
   - **Sistema:** Ubuntu 24.04 puro, sem painel.
   - **Localização:** Brasil.
   - **Senha do root:** uma senha forte. Ela só é usada até o passo 2.
   - **Chave SSH:** cole o conteúdo do `.pub` gerado acima.
4. Anote o **IP** do VPS (hoje `187.41.111.127`) e o nome provisório (`srv2118354.hstgr.cloud`). Esse nome já aponta para o IP e serve para testar o HTTPS antes de ter domínio.

A Hostinger faz um **backup semanal** do VPS sem custo extra (em hPanel → VPS → Backups). O backup diário pago não é necessário: o passo 7 cuida disso.

## 2. Preparar o servidor

### Usuário próprio (sem usar o root)

Entre como root:

```bash
ssh -i ~/.ssh/oracle_atelie root@187.41.111.127
```

No servidor, crie o usuário `atelie` com permissão de administrador e copie a chave SSH para ele:

```bash
adduser atelie
usermod -aG sudo atelie
rsync --archive --chown=atelie:atelie ~/.ssh /home/atelie
```

**Abra outro terminal** e confira se o novo usuário entra. Não feche o terminal do root ainda:

```bash
ssh -i ~/.ssh/oracle_atelie atelie@187.41.111.127
sudo whoami     # deve responder "root"
```

### SSH só com chave

Ainda no servidor, como `atelie`, desligue o login do root e o login por senha:

```bash
printf 'PermitRootLogin no\nPasswordAuthentication no\nKbdInteractiveAuthentication no\n' | sudo tee /etc/ssh/sshd_config.d/00-atelie.conf
sudo sshd -t && sudo systemctl restart ssh
sudo sshd -T | grep -E 'permitrootlogin|passwordauthentication|kbdinteractive'   # tudo "no"
```

O arquivo começa com `00` de propósito. O SSH usa o primeiro valor que encontrar, e o `50-cloud-init.conf` da imagem religaria o login por senha.

### Firewall, proteção contra força bruta e atualizações

```bash
sudo apt update && sudo apt -y upgrade
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw allow 443/udp          # HTTP/3 (opcional)
sudo ufw enable
sudo apt -y install fail2ban unattended-upgrades
sudo dpkg-reconfigure -plow unattended-upgrades   # responda "Sim"
```

O `fail2ban` bloqueia por um tempo os IPs que erram o SSH várias vezes. Confira com `sudo fail2ban-client status sshd`.

### Memória de troca (swap)

Com 4 GB de RAM, a swap evita que o build do Docker derrube o servidor por falta de memória:

```bash
sudo fallocate -l 2G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
free -h      # a linha "Swap" deve mostrar 2,0Gi
```

### Docker (pelo repositório oficial)

```bash
sudo apt -y install ca-certificates curl git
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt update
sudo apt -y install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo usermod -aG docker atelie
exit
```

Entre de novo pelo SSH, para o grupo `docker` valer, e confira com `docker ps` e `docker compose version`.

## 3. Apontar o domínio

O domínio `ateliemarylu.com.br` foi registrado no **registro.br**, que custa cerca de R$ 40 por ano e é renovado em novembro. O HTTPS automático precisa que o domínio já aponte para o IP do servidor **antes** de a loja subir.

1. No registro.br, abra o domínio e mantenha **"Utilizar os servidores DNS do Registro.br"**.
2. Clique em **Configurar zona DNS** e depois em **Modo avançado**. Logo depois do registro, aparece "Domínio em transição" por até cerca de 2 horas. Espere e tente de novo.
3. Crie duas entradas e clique em **Salvar alterações**:
   - Tipo **A**, nome **vazio** (o registro.br não aceita `@`), dados `187.41.111.127`
   - Tipo **A**, nome **www**, dados `187.41.111.127`

Confira se já propagou. Pode levar de minutos a algumas horas:

```bash
dig +short ateliemarylu.com.br       # deve mostrar 187.41.111.127
dig +short www.ateliemarylu.com.br   # idem
```

Recomendado: ative a verificação em duas etapas na conta do registro.br. Quem invadir essa conta pode desviar o site.

## 4. Publicar a loja

No servidor, como `atelie`:

```bash
git clone https://github.com/FelippeMenezes/atelie-marylu.git
cd atelie-marylu
cp .env.producao.example .env.producao
chmod 600 .env.producao            # só o seu usuário lê
```

Gere as senhas direto no arquivo, sem que elas apareçam na tela:

```bash
sed -i "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=$(openssl rand -hex 24)|" .env.producao
sed -i "s|^ADMIN_SENHA=.*|ADMIN_SENHA=Aa1-$(openssl rand -hex 8)|" .env.producao
```

O `Aa1-` garante maiúscula, número e símbolo. Não use `!` dentro de aspas duplas no terminal: o bash troca `!$` pelo último comando digitado.

Preencha o resto com `nano .env.producao`:

- `DOMINIO=www.ateliemarylu.com.br`: o endereço oficial.
- `DOMINIOS_EXTRAS="ateliemarylu.com.br srv2118354.hstgr.cloud"`: endereços que só redirecionam para o oficial. Use aspas, porque há espaço entre os endereços. Cada um ganha certificado próprio, então todos precisam apontar para o IP.
- `ADMIN_EMAIL` e os dados da loja (`LOJA_...`).

Para salvar no nano, use Ctrl+O e Enter; para sair, Ctrl+X. Para ver a senha temporária do Admin sem expor as outras, use `grep ^ADMIN_SENHA .env.producao`. Não cole senhas em chats nem em prints.

Suba tudo. O primeiro build leva alguns minutos:

```bash
docker compose -f docker-compose.prod.yml --env-file .env.producao up -d --build
docker compose -f docker-compose.prod.yml --env-file .env.producao logs -f app    # Ctrl+C para sair
```

Quando aparecer `Application started`, acesse `https://www.ateliemarylu.com.br`. O Caddy emite os certificados no primeiro acesso. Se a página demorar uns segundos, recarregue.

### Primeiro acesso

1. Entre com `ADMIN_EMAIL` e `ADMIN_SENHA`. A loja pede uma **senha nova** antes de liberar o painel.
2. Depois de trocar, apague a senha antiga do arquivo e recrie os containers:

   ```bash
   sed -i "s|^ADMIN_SENHA=.*|ADMIN_SENHA=|" .env.producao
   docker compose -f docker-compose.prod.yml --env-file .env.producao up -d
   ```

3. Cadastre os produtos e as fotos em **Painel → Produtos**.

O login vale por endereço. Quem entrou pelo `hstgr.cloud` precisa entrar de novo pelo domínio.

## 5. Atualizar a loja

O site **não** se atualiza sozinho quando uma PR entra na `main`. Depois do merge, rode no servidor:

```bash
cd ~/atelie-marylu && ./deploy/backup.sh && git pull && docker compose -f docker-compose.prod.yml --env-file .env.producao up -d --build
```

As migrations do banco são aplicadas automaticamente quando a loja reinicia. Mudanças só no `.env.producao` precisam apenas de `up -d`, sem `--build`.

## 6. Monitoramento e logs

- `https://www.ateliemarylu.com.br/health` responde `Healthy` quando a loja e o banco estão ok. Cadastre esse endereço num monitor gratuito, como o UptimeRobot, para ser avisado se a loja cair.
- Os logs da loja estão em JSON (Serilog). Só os erros dos últimos 15 minutos:

  ```bash
  docker compose -f docker-compose.prod.yml --env-file .env.producao logs app --since 15m 2>&1 | grep -iE '"(Error|Fatal)"' | tail
  ```

- Tudo: `docker compose -f docker-compose.prod.yml --env-file .env.producao logs --tail 200 app` (ou `caddy`).
- Situação dos containers: `docker compose -f docker-compose.prod.yml --env-file .env.producao ps`.
- Espaço em disco: `df -h` e `docker system df`. Os logs dos containers são limitados a 200 MB por serviço. Imagens antigas de builds podem ser removidas com `docker image prune`.

## 7. Backup diário

O script `deploy/backup.sh` salva o banco e as fotos em `backups/` e mantém os últimos 14 dias.

```bash
cd ~/atelie-marylu
./deploy/backup.sh                 # teste manual
crontab -l                         # veja se o agendamento já existe
```

Se não existir, agende o backup para todo dia às 3h:

```bash
(crontab -l 2>/dev/null; echo "0 3 * * * cd /home/atelie/atelie-marylu && ./deploy/backup.sh >> /home/atelie/backup.log 2>&1") | crontab -
```

**Cópia fora do servidor (recomendado).** Se o VPS se perder, o backup local vai junto, e o backup semanal da Hostinger fica no mesmo fornecedor. Para copiar para outro lugar:

1. Crie um destino gratuito, por exemplo um bucket no Backblaze B2 (10 GB grátis) ou uma pasta no Google Drive.
2. No servidor, instale e configure o rclone (`sudo apt install rclone && rclone config`), criando um "remote" com o nome `nuvem`.
3. Na linha do cron, antes do `./deploy/backup.sh`, acrescente `RCLONE_DESTINO=nuvem:atelie-backups`.

### Restaurar um backup

```bash
C="docker compose -f docker-compose.prod.yml --env-file .env.producao"
$C stop app
$C exec -T postgres sh -c 'dropdb -U "$POSTGRES_USER" "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" "$POSTGRES_DB"'
$C exec -T postgres sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < backups/banco_AAAA-MM-DD_HHMM.dump
$C run --rm --no-deps -T --entrypoint tar app -xzf - -C /app/wwwroot < backups/fotos_AAAA-MM-DD_HHMM.tar.gz
$C start app
```

## Problemas comuns

- **Os comandos "não funcionaram" ou deram "arquivo inexistente":** provavelmente rodaram no seu computador. Veja [Atenção ao terminal](#atenção-ao-terminal).
- **O terminal parece travado:** às vezes a saída termina sem quebra de linha (ex.: `Healthy`) e o prompt fica grudado nela. Aperte Enter. Se a conexão SSH congelou de verdade, digite Enter, `~` e `.` para encerrar.
- **O certificado HTTPS não sai:** o domínio ainda não aponta para o IP (confira com `dig +short`), ou as portas 80/443 estão fechadas no `ufw`. Veja `... logs caddy`.
- **Erro 502:** a loja não subiu. Veja `... logs app`. Muitas vezes é senha do banco com `$` ou `;` no `.env.producao`.
- **Mudei o `.env.producao` e nada mudou:** rode o `up -d` de novo para recriar os containers.
- **Mudei o código e nada mudou:** faltou o `--build` no `up -d`.

## Checklist de segurança do servidor

- [ ] SSH só com chave, sem login do root (`sshd -T` mostra tudo `no`)
- [ ] `ufw` ativo, só com SSH, 80 e 443
- [ ] `fail2ban` ativo
- [ ] Atualizações automáticas ativas (`unattended-upgrades`)
- [ ] `.env.producao` com `chmod 600` e `ADMIN_SENHA` vazio depois do primeiro acesso
- [ ] Postgres sem porta exposta (já é assim no `docker-compose.prod.yml`)
- [ ] Backup diário no cron, com cópia fora do servidor
- [ ] Verificação em duas etapas nas contas da Hostinger, do registro.br e do GitHub
- [ ] Monitor de disponibilidade no `/health`
