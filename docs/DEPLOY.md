# Publicar a loja na Oracle Cloud (gratuito)

Tudo roda numa única máquina virtual do plano **Always Free** da Oracle, com Docker:

```
Internet ──443/80──> Caddy (HTTPS automático) ──> loja (.NET) ──> Postgres
```

Só o Caddy fica exposto à internet. A loja e o banco ficam numa rede interna do Docker.

Tempo estimado na primeira vez: 1 a 2 horas.

---

## 1. Criar a conta

1. Acesse oracle.com/br/cloud/free e clique em **Comece gratuitamente**.
2. **Região principal (Home Region):** escolha **Brazil East (São Paulo)** ou **Brazil Southeast (Vinhedo)**. Essa escolha **não pode ser mudada depois**, e os recursos gratuitos só existem nela.
3. O cartão de crédito é pedido só para verificação. Pode aparecer uma cobrança temporária pequena, que é estornada.

Recomendado depois de criar:
- Em **Billing → Upgrade and Manage Payment**, passe a conta para **Pay As You Go**. Os recursos "Always Free" continuam gratuitos, e a Oracle deixa de poder desligar a máquina por ficar ociosa (o que acontece com contas só gratuitas e lojas com pouco movimento).
- Logo em seguida, em **Billing → Budgets**, crie um orçamento de **US$ 1** com alerta por e-mail. Se algo pago for criado por engano, você fica sabendo na hora.

> Regra de ouro: ao criar qualquer recurso, confira o selo **"Always Free-eligible"**. Sem o selo, não crie.

## 2. Criar a rede

1. Menu **Networking → Virtual Cloud Networks → Start VCN Wizard → Create VCN with Internet Connectivity**.
2. Nome: `atelie-vcn`. Mantenha o resto e conclua.
3. Abra a VCN → **Security Lists → Default Security List** → **Add Ingress Rules** e adicione:

   - Origem `0.0.0.0/0`, TCP, porta **80** (HTTP, para o certificado e o redirecionamento)
   - Origem `0.0.0.0/0`, TCP, porta **443** (HTTPS)
   - Origem `0.0.0.0/0`, UDP, porta **443** (HTTP/3, opcional)

4. Na regra que já existe para a porta **22** (SSH), troque a origem `0.0.0.0/0` pelo **seu IP** seguido de `/32` (descubra em `curl ifconfig.me`). Assim só você tenta acessar a máquina.

## 3. Criar a máquina

1. No seu computador, gere uma chave SSH (é a "senha" de acesso, muito mais segura):

   ```bash
   ssh-keygen -t ed25519 -f ~/.ssh/oracle_atelie -C "atelie-marylu"
   cat ~/.ssh/oracle_atelie.pub
   ```

2. Menu **Compute → Instances → Create instance**:
   - **Name:** `atelie-loja`
   - **Image:** Canonical **Ubuntu 24.04** (a versão para aarch64/ARM)
   - **Shape:** Ampere **VM.Standard.A1.Flex**, com **2 OCPUs e 12 GB** de memória (o gratuito vai até 4 e 24 GB no total)
   - **Networking:** a `atelie-vcn`, subnet pública, **Assign a public IPv4 address** marcado
   - **SSH keys:** cole o conteúdo do `oracle_atelie.pub`
   - **Boot volume:** o padrão (cerca de 50 GB) basta
3. Se aparecer **"Out of capacity"**, as máquinas ARM gratuitas estão esgotadas naquele momento. Tente outro *availability domain* ou outro horário.
4. Anote o **IP público** da instância.

## 4. Preparar o servidor

Entre na máquina:

```bash
ssh -i ~/.ssh/oracle_atelie ubuntu@IP_PUBLICO
```

### Firewall do Ubuntu

A imagem Ubuntu da Oracle já vem com regras de firewall (`iptables`) que bloqueiam tudo, menos SSH. Libere as portas da loja e salve as regras para continuarem valendo depois de reiniciar:

```bash
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 80 -j ACCEPT
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 443 -j ACCEPT
sudo iptables -I INPUT 6 -m state --state NEW -p udp --dport 443 -j ACCEPT
sudo netfilter-persistent save
```

Não use o `ufw` nessa imagem: ele conflita com as regras da Oracle.

### Atualizações de segurança automáticas

```bash
sudo apt update && sudo apt -y upgrade
sudo dpkg-reconfigure -plow unattended-upgrades   # responda "Sim"
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
sudo usermod -aG docker ubuntu
exit
```

Entre de novo pelo SSH (para o grupo `docker` valer) e confira com `docker run --rm hello-world`.

## 5. Apontar o domínio

O HTTPS automático precisa de um nome apontando para o IP da máquina **antes** de subir a loja.

- **Domínio próprio** (ex.: `ateliemarylu.com.br`, cerca de R$ 40 por ano no registro.br): no painel de DNS, crie um registro **A** com o nome vazio (ou `www`) apontando para o IP público.
- **Gratuito, para começar:** crie um subdomínio em duckdns.org (ex.: `ateliemarylu.duckdns.org`) e aponte para o IP público.

Confira se já propagou (pode levar de minutos a algumas horas):

```bash
dig +short ateliemarylu.com.br   # deve mostrar o IP público
```

## 6. Publicar a loja

```bash
git clone https://github.com/FelippeMenezes/atelie-marylu.git
cd atelie-marylu
cp .env.producao.example .env.producao
chmod 600 .env.producao            # só o seu usuário lê
openssl rand -hex 24               # use para POSTGRES_PASSWORD
openssl rand -hex 12               # use (com uma letra maiúscula e um símbolo, ex.: "!") para ADMIN_SENHA
nano .env.producao                 # preencha DOMINIO, senhas, e-mail do admin e dados da loja
```

Suba tudo (o primeiro build leva alguns minutos):

```bash
docker compose -f docker-compose.prod.yml --env-file .env.producao up -d --build
docker compose -f docker-compose.prod.yml --env-file .env.producao logs -f app    # Ctrl+C para sair
```

Quando aparecer `Application started`, acesse `https://SEU_DOMINIO`. O Caddy emite o certificado no primeiro acesso; se demorar uns segundos, recarregue.

### Primeiro acesso

1. Entre com `ADMIN_EMAIL` e `ADMIN_SENHA`. A loja pede uma **senha nova** antes de liberar o painel.
2. Depois de trocar, **apague o valor de `ADMIN_SENHA`** no `.env.producao`. O Admin já existe, e a senha antiga não vale mais.
3. Cadastre os produtos e as fotos em **Painel → Produtos**.

## 7. Backup diário

O script `deploy/backup.sh` salva o banco e as fotos em `backups/` e mantém os últimos 14 dias.

```bash
mkdir -p backups
./deploy/backup.sh                 # teste manual
crontab -e
```

Adicione a linha abaixo (backup todo dia às 3h):

```
0 3 * * * cd /home/ubuntu/atelie-marylu && ./deploy/backup.sh >> backups/backup.log 2>&1
```

**Cópia fora do servidor (recomendado).** Se a máquina se perder, o backup local vai junto. O Object Storage da Oracle tem 10 GB grátis:
1. Crie um bucket `atelie-backups` em **Storage → Buckets**.
2. Em **Perfil → Customer Secret Keys**, gere uma chave de acesso compatível com S3.
3. No servidor, instale e configure o rclone (`sudo apt install rclone && rclone config`) com o provedor "S3 → Oracle Cloud Infrastructure Object Storage", usando o nome `oci`.
4. Na linha do cron, antes do `./deploy/backup.sh`, acrescente `RCLONE_DESTINO=oci:atelie-backups`.

### Restaurar um backup

```bash
C="docker compose -f docker-compose.prod.yml --env-file .env.producao"
$C stop app
$C exec -T postgres sh -c 'dropdb -U "$POSTGRES_USER" "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" "$POSTGRES_DB"'
$C exec -T postgres sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < backups/banco_AAAA-MM-DD_HHMM.dump
$C run --rm --no-deps -T --entrypoint tar app -xzf - -C /app/wwwroot < backups/fotos_AAAA-MM-DD_HHMM.tar.gz
$C start app
```

## 8. Atualizar a loja

Depois de mergear uma PR na `main`:

```bash
cd ~/atelie-marylu
git pull
docker compose -f docker-compose.prod.yml --env-file .env.producao up -d --build
```

As migrations do banco são aplicadas automaticamente quando a loja reinicia. Faça um `./deploy/backup.sh` antes de atualizações grandes.

## 9. Monitoramento

- `https://SEU_DOMINIO/health` responde `Healthy` quando a loja e o banco estão ok. Cadastre esse endereço num monitor gratuito (ex.: UptimeRobot) para ser avisado se a loja cair.
- Logs: `docker compose -f docker-compose.prod.yml --env-file .env.producao logs --tail 200 app`
- Espaço em disco: `df -h` e `docker system df`. Os logs dos containers são limitados a 200 MB por serviço.

## Problemas comuns

- **O certificado HTTPS não sai:** o domínio ainda não aponta para o IP (`dig +short`), ou as portas 80/443 estão fechadas (passo 2 ou firewall do passo 4). Veja `... logs caddy`.
- **Erro 502:** a loja não subiu. Veja `... logs app`. Muitas vezes é senha do banco com `$` ou `;` no `.env.producao`.
- **"Out of capacity" ao criar a máquina:** tente outro horário ou availability domain.
- **Mudei o `.env.producao` e nada mudou:** rode o `up -d` de novo para recriar os containers.

## Checklist de segurança do servidor

- [ ] SSH só com chave e porta 22 liberada apenas para o seu IP
- [ ] Atualizações automáticas ativas (`unattended-upgrades`)
- [ ] `.env.producao` com `chmod 600` e sem `ADMIN_SENHA` depois do primeiro acesso
- [ ] Postgres sem porta exposta (já é assim no `docker-compose.prod.yml`)
- [ ] Backup diário no cron, com cópia fora do servidor
- [ ] Alerta de orçamento de US$ 1 na Oracle
- [ ] Monitor de disponibilidade no `/health`
