using AtelieMarylu.Domain.Auditoria;
using AtelieMarylu.Domain.Catalogo;
using AtelieMarylu.Domain.Pagamentos;
using AtelieMarylu.Domain.Pedidos;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AtelieMarylu.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<ProdutoImagem> ProdutoImagens => Set<ProdutoImagem>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();
    public DbSet<NotificacaoProcessada> NotificacoesProcessadas => Set<NotificacaoProcessada>();
    public DbSet<RegistroAuditoria> Auditoria => Set<RegistroAuditoria>();

    // Chaves que criptografam cookies de login e o carrinho; no banco, sobrevivem a reinícios e deploys
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(u =>
        {
            u.Property(x => x.NomeCompleto).HasMaxLength(150);
            u.Property(x => x.Cpf).HasMaxLength(11);
        });

        builder.Entity<Produto>(p =>
        {
            p.Property(x => x.Nome).HasMaxLength(120).IsRequired();
            p.Property(x => x.Descricao).HasMaxLength(2000);
            p.Property(x => x.Preco).HasPrecision(10, 2);
            p.HasMany(x => x.Imagens).WithOne().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Cascade);
            p.ToTable(t => t.HasCheckConstraint("CK_Produtos_Estoque", "\"Estoque\" >= 0"));
        });

        builder.Entity<ProdutoImagem>(i =>
        {
            i.Property(x => x.Url).HasMaxLength(300);
            i.Property(x => x.UrlMiniatura).HasMaxLength(300);
        });

        builder.Entity<Pedido>(p =>
        {
            p.OwnsOne(x => x.Endereco, e =>
            {
                e.Property(x => x.Cep).HasMaxLength(8);
                e.Property(x => x.Logradouro).HasMaxLength(150);
                e.Property(x => x.Numero).HasMaxLength(20);
                e.Property(x => x.Complemento).HasMaxLength(80);
                e.Property(x => x.Bairro).HasMaxLength(80);
                e.Property(x => x.Cidade).HasMaxLength(80);
                e.Property(x => x.Uf).HasMaxLength(2);
            });
            p.HasMany(x => x.Itens).WithOne().HasForeignKey("PedidoId").OnDelete(DeleteBehavior.Cascade);
            p.HasMany(x => x.Pagamentos).WithOne().HasForeignKey(x => x.PedidoId);
            p.Navigation(x => x.Itens).UsePropertyAccessMode(PropertyAccessMode.Field);
            p.Navigation(x => x.Pagamentos).UsePropertyAccessMode(PropertyAccessMode.Field);
            p.Property(x => x.Codigo).HasMaxLength(12);
            p.HasIndex(x => x.Codigo).IsUnique();
            p.Property(x => x.FreteValor).HasPrecision(10, 2);
            p.Property(x => x.PercentualDescontoPix).HasPrecision(5, 2);
            p.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            p.Property(x => x.Observacao).HasMaxLength(500);
            p.Property(x => x.MotivoCancelamento).HasMaxLength(200);
            p.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
            p.HasIndex(x => x.ClienteId);
            p.HasIndex(x => new { x.Status, x.PagarAte });

            // Controle de concorrência otimista com a coluna de sistema xmin do Postgres:
            // se duas pessoas (ou o webhook e um funcionário) alterarem o mesmo pedido ao mesmo tempo,
            // a segunda gravação falha em vez de sobrescrever a primeira em silêncio.
            p.Property<uint>("Versao").IsRowVersion();

            p.Ignore(x => x.Subtotal);
            p.Ignore(x => x.Total);
            p.Ignore(x => x.DescontoPix);
        });

        builder.Entity<ItemPedido>(i =>
        {
            i.ToTable("ItensPedido");
            i.Property(x => x.NomeProduto).HasMaxLength(120);
            i.Property(x => x.PrecoUnitario).HasPrecision(10, 2);
            i.HasOne<Produto>().WithMany().HasForeignKey(x => x.ProdutoId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Pagamento>(p =>
        {
            p.Property(x => x.Valor).HasPrecision(10, 2);
            p.Property(x => x.Metodo).HasConversion<string>().HasMaxLength(20);
            p.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            p.Property(x => x.Provedor).HasMaxLength(30);
            p.Property(x => x.IdExterno).HasMaxLength(100);
            p.HasIndex(x => new { x.Provedor, x.IdExterno }).IsUnique();
        });

        builder.Entity<NotificacaoProcessada>(n =>
        {
            n.Property(x => x.Provedor).HasMaxLength(30);
            n.Property(x => x.IdEvento).HasMaxLength(150);
            n.HasIndex(x => new { x.Provedor, x.IdEvento }).IsUnique();
        });

        builder.Entity<RegistroAuditoria>(a =>
        {
            a.ToTable("Auditoria");
            a.Property(x => x.Autor).HasMaxLength(256);
            a.Property(x => x.Acao).HasMaxLength(60);
            a.Property(x => x.Entidade).HasMaxLength(40);
            a.Property(x => x.EntidadeId).HasMaxLength(60);
            a.Property(x => x.Detalhes).HasMaxLength(1000);
            a.HasIndex(x => new { x.Entidade, x.EntidadeId });
            a.HasIndex(x => x.Quando);
        });
    }
}
