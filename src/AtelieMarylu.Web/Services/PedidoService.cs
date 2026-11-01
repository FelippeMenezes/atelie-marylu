using AtelieMarylu.Domain.Carrinho;
using AtelieMarylu.Domain.Catalogo;
using AtelieMarylu.Domain.Frete;
using AtelieMarylu.Domain.Pagamentos;
using AtelieMarylu.Domain.Pedidos;
using AtelieMarylu.Web.Configuracao;
using AtelieMarylu.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AtelieMarylu.Web.Services;

// Orquestra pedido + estoque + frete + pagamento. As regras de cada pedido ficam na
// entidade Pedido (projeto Domain); aqui fica o que depende de banco e de serviços externos.
public class PedidoService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ICalculadoraFrete frete,
    IGatewayPagamento gateway,
    AuditoriaService auditoria,
    PedidoEventos eventos,
    IOptions<LojaOptions> loja,
    TimeProvider relogio,
    ILogger<PedidoService> logger)
{
    public const string ProvedorManual = "manual";
    private const string MensagemConcorrencia = "O pedido foi alterado por outra pessoa ao mesmo tempo. Recarregue a página e tente de novo.";

    public async Task<IReadOnlyList<OpcaoFrete>> CotarFreteAsync(string cep, IReadOnlyList<ItemCarrinho> itens, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var produtos = await CarregarProdutosAsync(db, itens.Select(i => i.ProdutoId), ct);
        return await frete.CotarAsync(cep, MontarPacotes(itens, produtos), ct);
    }

    // Cria o pedido a partir do carrinho. Preços vêm do banco e o frete é cotado de novo aqui:
    // nada que o navegador mandou é usado como valor.
    // criadoPor preenchido = pedido lançado pelo painel (sem limite de pendentes e com prazo maior).
    public async Task<Pedido> CriarAsync(string clienteId, IReadOnlyList<ItemCarrinho> itens, EnderecoEntrega endereco,
        string codigoFrete, string? criadoPor = null, CancellationToken ct = default)
    {
        if (itens.Count == 0) throw new RegraDePedidoException("O carrinho está vazio.");
        var cep = Cep.Normalizar(endereco.Cep) ?? throw new RegraDePedidoException("CEP inválido.");
        endereco.Cep = cep;
        var opcoes = loja.Value;
        var agora = relogio.GetUtcNow().UtcDateTime;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (criadoPor is null)
        {
            var pendentes = await db.Pedidos.CountAsync(p => p.ClienteId == clienteId && p.Status == StatusPedido.AguardandoPagamento, ct);
            if (pendentes >= opcoes.MaxPedidosPendentesPorCliente)
                throw new RegraDePedidoException("Você já tem pedidos aguardando pagamento. Pague ou aguarde o cancelamento deles antes de fazer outro.");
        }

        var produtos = await CarregarProdutosAsync(db, itens.Select(i => i.ProdutoId), ct);
        var itensPedido = itens.Select(i =>
        {
            if (!produtos.TryGetValue(i.ProdutoId, out var p) || !p.Ativo)
                throw new RegraDePedidoException("Um dos produtos do carrinho não está mais disponível.");
            return new ItemPedido(p.Id, p.Nome, p.Preco, i.Quantidade);
        }).ToList();

        var cotacao = await frete.CotarAsync(cep, MontarPacotes(itens, produtos), ct);
        var opcao = cotacao.FirstOrDefault(o => o.Codigo == codigoFrete)
            ?? throw new RegraDePedidoException("Opção de frete inválida. Calcule o frete novamente.");

        var prazo = criadoPor is null ? TimeSpan.FromMinutes(opcoes.PrazoPagamentoMinutos) : TimeSpan.FromHours(opcoes.PrazoPagamentoPainelHoras);
        var pedido = new Pedido(clienteId, itensPedido, endereco, opcao, agora + prazo, opcoes.DescontoPixPercentual, criadoPor);

        await using (var transacao = await db.Database.BeginTransactionAsync(ct))
        {
            foreach (var item in itensPedido)
                await BaixarEstoqueAsync(db, item.ProdutoId, item.Quantidade, item.NomeProduto, ct);
            db.Pedidos.Add(pedido);
            await db.SaveChangesAsync(ct);
            await auditoria.RegistrarAsync(db, "pedido.criado", "Pedido", pedido.Id, $"{pedido.Codigo} · {pedido.Total:C}");
            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
        }

        logger.LogInformation("Pedido {Codigo} criado para o cliente {ClienteId}.", pedido.Codigo, clienteId);
        return pedido;
    }

    public async Task<Pagamento> IniciarPagamentoAsync(int pedidoId, MetodoPagamento metodo, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pedido = await CarregarPedidoAsync(db, pedidoId, ct);
        if (pedido.PrazoDePagamentoVencido(relogio.GetUtcNow().UtcDateTime))
            throw new RegraDePedidoException("O prazo de pagamento deste pedido terminou.");
        var cliente = await db.Users.AsNoTracking().FirstAsync(u => u.Id == pedido.ClienteId, ct);

        var pagamento = pedido.IniciarPagamento(metodo, gateway.Nome);
        await SalvarAsync(db, ct);

        var inicio = await gateway.IniciarAsync(new DadosCobranca(
            pedido.Id, pedido.Codigo, pagamento.Valor, metodo, cliente.Email ?? "", cliente.NomeCompleto,
            $"Pedido {pedido.Codigo} - Ateliê Marylu", pedido.PagarAte), ct);
        pagamento.RegistrarInicio(inicio.IdExterno, inicio.UrlPagamento, inicio.PixCopiaECola);
        await SalvarAsync(db, ct);
        eventos.Notificar(pedido.Id);
        return pagamento;
    }

    // Recebe o aviso do provedor (webhook). Retorna false se o pagamento não existir.
    // - Idempotente: o mesmo IdEvento só é aplicado uma vez.
    // - Se um funcionário alterar o pedido no mesmo instante, tenta de novo com os dados atualizados.
    public async Task<bool> ProcessarNotificacaoAsync(NotificacaoPagamento notificacao, CancellationToken ct = default)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                return await ProcessarNotificacaoUmaVezAsync(notificacao, ct);
            }
            catch (DbUpdateConcurrencyException) when (tentativa < 3)
            {
                logger.LogInformation("Conflito ao processar notificação {IdEvento}; tentando de novo.", notificacao.IdEvento);
            }
        }
    }

    private async Task<bool> ProcessarNotificacaoUmaVezAsync(NotificacaoPagamento notificacao, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pedidoId = await db.Pagamentos
            .Where(p => p.Provedor == gateway.Nome && p.IdExterno == notificacao.IdExterno)
            .Select(p => (int?)p.PedidoId)
            .FirstOrDefaultAsync(ct);
        if (pedidoId is null) return false;

        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        db.NotificacoesProcessadas.Add(new NotificacaoProcessada { Provedor = gateway.Nome, IdEvento = notificacao.IdEvento });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            logger.LogInformation("Notificação {IdEvento} já processada; ignorando.", notificacao.IdEvento);
            return true;
        }

        var pedido = await CarregarPedidoAsync(db, pedidoId.Value, ct);
        var pagamento = pedido.Pagamentos.First(p => p.IdExterno == notificacao.IdExterno);
        string? alerta = null;

        if (notificacao.Status == StatusPagamento.Aprovado)
        {
            try
            {
                pedido.ConfirmarPagamento(pagamento, notificacao.ValorPago);
            }
            catch (RegraDePedidoException ex)
            {
                // Ex.: pedido cancelado/expirado e o cliente pagou mesmo assim, ou valor diferente.
                // O dinheiro entrou: fica registrado na auditoria como "pagamento.divergente",
                // que aparece em destaque no resumo do painel para a loja estornar ou resolver.
                pagamento.AtualizarStatus(StatusPagamento.Aprovado);
                alerta = $"Pagamento {notificacao.IdExterno} aprovado no pedido {pedido.Codigo}, mas: {ex.Message} Verifique e, se for o caso, estorne.";
            }
        }
        else if (pagamento.Status != StatusPagamento.Aprovado || notificacao.Status == StatusPagamento.Estornado)
        {
            pagamento.AtualizarStatus(notificacao.Status);
        }

        await auditoria.RegistrarAsync(db, alerta is null ? "pagamento.notificacao" : "pagamento.divergente", "Pedido", pedido.Id,
            alerta ?? $"{notificacao.Status} via {gateway.Nome} ({notificacao.IdExterno})", Autor.Sistema);
        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);

        eventos.Notificar(pedido.Id);
        if (alerta is not null) logger.LogError("ALERTA: {Alerta}", alerta);
        return true;
    }

    // Para quando o cliente pagou por fora (PIX direto, dinheiro na retirada...)
    public Task RegistrarPagamentoManualAsync(int pedidoId, MetodoPagamento metodo, CancellationToken ct = default) =>
        AlterarAsync(pedidoId, "pagamento.manual", (pedido, _) =>
        {
            var pagamento = pedido.IniciarPagamento(metodo, ProvedorManual);
            pagamento.RegistrarInicio($"manual-{Guid.NewGuid():N}", null, null);
            pedido.ConfirmarPagamento(pagamento);
            return Task.FromResult<string?>($"{metodo} · {pagamento.Valor:C}");
        }, ct: ct);

    public Task CancelarAsync(int pedidoId, string? motivo = null, CancellationToken ct = default) =>
        AlterarAsync(pedidoId, "pedido.cancelado", async (pedido, db) =>
        {
            pedido.Cancelar(motivo);
            foreach (var item in pedido.Itens)
                await DevolverEstoqueAsync(db, item.ProdutoId, item.Quantidade, ct);
            return motivo;
        }, ct: ct);

    // Tarefa periódica: cancela pedidos cujo prazo de pagamento acabou e devolve o estoque
    public async Task<int> ExpirarVencidosAsync(CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        List<int> vencidos;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            vencidos = await db.Pedidos
                .Where(p => p.Status == StatusPedido.AguardandoPagamento && p.PagarAte < agora)
                .Select(p => p.Id).ToListAsync(ct);
        }

        var cancelados = 0;
        foreach (var id in vencidos)
        {
            try
            {
                await AlterarAsync(id, "pedido.expirado", async (pedido, db) =>
                {
                    // Confere de novo: pode ter sido pago entre a consulta e agora
                    if (!pedido.PrazoDePagamentoVencido(agora)) return null;
                    pedido.Cancelar("Prazo de pagamento expirado");
                    foreach (var item in pedido.Itens)
                        await DevolverEstoqueAsync(db, item.ProdutoId, item.Quantidade, ct);
                    return "Prazo de pagamento expirado";
                }, Autor.Sistema, ct);
                cancelados++;
            }
            catch (Exception ex) when (ex is RegraDePedidoException or DbUpdateException)
            {
                logger.LogWarning(ex, "Não foi possível expirar o pedido {PedidoId}", id);
            }
        }
        return cancelados;
    }

    public Task AlterarQuantidadeAsync(int pedidoId, int produtoId, int quantidade, CancellationToken ct = default) =>
        AlterarAsync(pedidoId, "pedido.itens", async (pedido, db) =>
        {
            var nome = pedido.Itens.FirstOrDefault(i => i.ProdutoId == produtoId)?.NomeProduto ?? "";
            var diferenca = pedido.AlterarQuantidade(produtoId, quantidade);
            if (diferenca > 0) await BaixarEstoqueAsync(db, produtoId, diferenca, nome, ct);
            else if (diferenca < 0) await DevolverEstoqueAsync(db, produtoId, -diferenca, ct);
            await RecotarFreteAsync(db, pedido, ct);
            return $"{nome}: quantidade {quantidade}";
        }, ct: ct);

    public Task AdicionarItemAsync(int pedidoId, int produtoId, int quantidade, CancellationToken ct = default) =>
        AlterarAsync(pedidoId, "pedido.itens", async (pedido, db) =>
        {
            var produto = await db.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId && p.Ativo, ct)
                ?? throw new RegraDePedidoException("Produto não encontrado.");
            pedido.AdicionarItem(new ItemPedido(produto.Id, produto.Nome, produto.Preco, quantidade));
            await BaixarEstoqueAsync(db, produto.Id, quantidade, produto.Nome, ct);
            await RecotarFreteAsync(db, pedido, ct);
            return $"+{quantidade} {produto.Nome}";
        }, ct: ct);

    public Task AlterarEntregaAsync(int pedidoId, EnderecoEntrega endereco, string? observacao, CancellationToken ct = default) =>
        AlterarAsync(pedidoId, "pedido.entrega", async (pedido, db) =>
        {
            endereco.Cep = Cep.Normalizar(endereco.Cep) ?? throw new RegraDePedidoException("CEP inválido.");
            var cepMudou = endereco.Cep != pedido.Endereco.Cep;
            pedido.AlterarEndereco(endereco);
            pedido.Observacao = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim();
            if (cepMudou && pedido.PodeAlterarItens) await RecotarFreteAsync(db, pedido, ct);
            return $"CEP {endereco.Cep}";
        }, ct: ct);

    public Task IniciarPreparoAsync(int pedidoId, CancellationToken ct = default) =>
        AlterarAsync(pedidoId, "pedido.preparo", (p, _) => { p.IniciarPreparo(); return Task.FromResult<string?>(null); }, ct: ct);

    public Task MarcarEnviadoAsync(int pedidoId, string? codigoRastreio, CancellationToken ct = default) =>
        AlterarAsync(pedidoId, "pedido.enviado", (p, _) => { p.MarcarEnviado(codigoRastreio); return Task.FromResult(p.CodigoRastreio); }, ct: ct);

    public Task MarcarEntregueAsync(int pedidoId, CancellationToken ct = default) =>
        AlterarAsync(pedidoId, "pedido.entregue", (p, _) => { p.MarcarEntregue(); return Task.FromResult<string?>(null); }, ct: ct);

    // Carrega, altera, registra auditoria e grava tudo numa transação.
    // A alteração devolve o texto de detalhe da auditoria (ou null para "nada a fazer").
    private async Task AlterarAsync(int pedidoId, string acao, Func<Pedido, ApplicationDbContext, Task<string?>> alteracao,
        Autor? autor = null, CancellationToken ct = default)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await using var transacao = await db.Database.BeginTransactionAsync(ct);
            var pedido = await CarregarPedidoAsync(db, pedidoId, ct);
            var estadoAntes = pedido.Status;
            var detalhes = await alteracao(pedido, db);
            if (detalhes is null && pedido.Status == estadoAntes && !db.ChangeTracker.HasChanges())
                return;
            await auditoria.RegistrarAsync(db, acao, "Pedido", pedido.Id, detalhes, autor);
            await SalvarAsync(db, ct);
            await transacao.CommitAsync(ct);
        }

        eventos.Notificar(pedidoId);
    }

    private static async Task SalvarAsync(ApplicationDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new RegraDePedidoException(MensagemConcorrencia);
        }
    }

    private async Task RecotarFreteAsync(ApplicationDbContext db, Pedido pedido, CancellationToken ct)
    {
        var itens = pedido.Itens.Select(i => new ItemCarrinho(i.ProdutoId, i.Quantidade)).ToList();
        var produtos = await CarregarProdutosAsync(db, itens.Select(i => i.ProdutoId), ct);
        var opcoes = await frete.CotarAsync(pedido.Endereco.Cep, MontarPacotes(itens, produtos), ct);
        var mesma = opcoes.FirstOrDefault(o => o.Codigo == pedido.FreteCodigo);
        if (mesma is not null) pedido.DefinirFrete(mesma);
    }

    private static async Task<Pedido> CarregarPedidoAsync(ApplicationDbContext db, int pedidoId, CancellationToken ct) =>
        await db.Pedidos.Include(p => p.Itens).Include(p => p.Pagamentos).FirstOrDefaultAsync(p => p.Id == pedidoId, ct)
            ?? throw new RegraDePedidoException("Pedido não encontrado.");

    private static async Task<Dictionary<int, Produto>> CarregarProdutosAsync(ApplicationDbContext db, IEnumerable<int> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return await db.Produtos.AsNoTracking().Where(p => lista.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
    }

    private static List<PacoteFrete> MontarPacotes(IEnumerable<ItemCarrinho> itens, Dictionary<int, Produto> produtos) =>
        itens.Where(i => produtos.ContainsKey(i.ProdutoId))
            .Select(i =>
            {
                var p = produtos[i.ProdutoId];
                return new PacoteFrete(p.PesoGramas, p.AlturaCm, p.LarguraCm, p.ComprimentoCm, i.Quantidade);
            }).ToList();

    // UPDATE com condição no próprio banco: se duas pessoas comprarem a última peça ao mesmo tempo,
    // só uma consegue (a outra afeta 0 linhas e recebe o erro).
    private static async Task BaixarEstoqueAsync(ApplicationDbContext db, int produtoId, int quantidade, string nome, CancellationToken ct)
    {
        var linhas = await db.Produtos
            .Where(p => p.Id == produtoId && p.Estoque >= quantidade)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estoque, p => p.Estoque - quantidade), ct);
        if (linhas == 0)
            throw new RegraDePedidoException($"Estoque insuficiente para \"{nome}\".");
    }

    private static Task DevolverEstoqueAsync(ApplicationDbContext db, int produtoId, int quantidade, CancellationToken ct) =>
        db.Produtos.Where(p => p.Id == produtoId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Estoque, p => p.Estoque + quantidade), ct);
}
