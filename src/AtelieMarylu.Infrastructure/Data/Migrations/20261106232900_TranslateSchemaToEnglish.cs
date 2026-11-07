using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtelieMarylu.Infrastructure.Data.Migrations;

// Migration escrita à mão: passa tabelas, colunas e valores gravados para o inglês.
// O EF geraria DROP TABLE + CREATE TABLE, o que apagaria pedidos e produtos. Aqui tudo é só
// RENAME e UPDATE, então nenhum dado se perde e o Down desfaz a mudança.
public partial class TranslateSchemaToEnglish : Migration
{
    private static readonly (string Old, string New)[] Tables =
    [
        ("Produtos", "Products"),
        ("ProdutoImagens", "ProductImages"),
        ("Pedidos", "Orders"),
        ("ItensPedido", "OrderItems"),
        ("Pagamentos", "Payments"),
        ("NotificacoesProcessadas", "ProcessedNotifications"),
        ("Auditoria", "AuditEntries"),
    ];

    // Tabela com o nome antigo: as colunas são renomeadas antes das tabelas
    private static readonly (string Table, string Old, string New)[] Columns =
    [
        ("Produtos", "Nome", "Name"),
        ("Produtos", "Descricao", "Description"),
        ("Produtos", "Preco", "Price"),
        ("Produtos", "Estoque", "Stock"),
        ("Produtos", "Ativo", "IsActive"),
        ("Produtos", "PrazoProducaoDias", "ProductionDays"),
        ("Produtos", "PesoGramas", "WeightGrams"),
        ("Produtos", "AlturaCm", "HeightCm"),
        ("Produtos", "LarguraCm", "WidthCm"),
        ("Produtos", "ComprimentoCm", "LengthCm"),
        ("Produtos", "CriadoEm", "CreatedAt"),
        ("ProdutoImagens", "ProdutoId", "ProductId"),
        ("ProdutoImagens", "UrlMiniatura", "ThumbnailUrl"),
        ("ProdutoImagens", "Ordem", "SortOrder"),
        ("Pedidos", "Codigo", "Code"),
        ("Pedidos", "ClienteId", "CustomerId"),
        ("Pedidos", "Endereco_Cep", "Address_PostalCode"),
        ("Pedidos", "Endereco_Logradouro", "Address_Street"),
        ("Pedidos", "Endereco_Numero", "Address_Number"),
        ("Pedidos", "Endereco_Complemento", "Address_Complement"),
        ("Pedidos", "Endereco_Bairro", "Address_District"),
        ("Pedidos", "Endereco_Cidade", "Address_City"),
        ("Pedidos", "Endereco_Uf", "Address_State"),
        ("Pedidos", "FreteCodigo", "ShippingCode"),
        ("Pedidos", "FreteNome", "ShippingName"),
        ("Pedidos", "FreteValor", "ShippingCost"),
        ("Pedidos", "FretePrazoDias", "ShippingDays"),
        ("Pedidos", "CodigoRastreio", "TrackingCode"),
        ("Pedidos", "PercentualDescontoPix", "PixDiscountPercent"),
        ("Pedidos", "Observacao", "Notes"),
        ("Pedidos", "CriadoPor", "CreatedBy"),
        ("Pedidos", "CriadoEm", "CreatedAt"),
        ("Pedidos", "PagarAte", "PayBy"),
        ("Pedidos", "PagoEm", "PaidAt"),
        ("Pedidos", "CanceladoEm", "CancelledAt"),
        ("Pedidos", "MotivoCancelamento", "CancellationReason"),
        ("ItensPedido", "PedidoId", "OrderId"),
        ("ItensPedido", "ProdutoId", "ProductId"),
        ("ItensPedido", "NomeProduto", "ProductName"),
        ("ItensPedido", "PrecoUnitario", "UnitPrice"),
        ("ItensPedido", "Quantidade", "Quantity"),
        ("Pagamentos", "PedidoId", "OrderId"),
        ("Pagamentos", "Metodo", "Method"),
        ("Pagamentos", "Valor", "Amount"),
        ("Pagamentos", "Provedor", "Provider"),
        ("Pagamentos", "IdExterno", "ExternalId"),
        ("Pagamentos", "UrlPagamento", "PaymentUrl"),
        ("Pagamentos", "PixCopiaECola", "PixCopyPaste"),
        ("Pagamentos", "CriadoEm", "CreatedAt"),
        ("Pagamentos", "AtualizadoEm", "UpdatedAt"),
        ("NotificacoesProcessadas", "Provedor", "Provider"),
        ("NotificacoesProcessadas", "IdEvento", "EventId"),
        ("NotificacoesProcessadas", "RecebidaEm", "ReceivedAt"),
        ("Auditoria", "Quando", "OccurredAt"),
        ("Auditoria", "UsuarioId", "UserId"),
        ("Auditoria", "Autor", "Author"),
        ("Auditoria", "Acao", "Action"),
        ("Auditoria", "Entidade", "Entity"),
        ("Auditoria", "EntidadeId", "EntityId"),
        ("Auditoria", "Detalhes", "Details"),
        ("AspNetUsers", "NomeCompleto", "FullName"),
        ("AspNetUsers", "Cpf", "TaxId"),
        ("AspNetUsers", "CriadoEm", "CreatedAt"),
        ("AspNetUsers", "TrocarSenhaObrigatoria", "MustChangePassword"),
    ];

    // Tabela com o nome novo. Índices e restrições não mudam sozinhos ao renomear a tabela.
    private static readonly (string Old, string New)[] Indexes =
    [
        ("IX_Auditoria_Entidade_EntidadeId", "IX_AuditEntries_Entity_EntityId"),
        ("IX_Auditoria_Quando", "IX_AuditEntries_OccurredAt"),
        ("IX_ItensPedido_PedidoId", "IX_OrderItems_OrderId"),
        ("IX_ItensPedido_ProdutoId", "IX_OrderItems_ProductId"),
        ("IX_NotificacoesProcessadas_Provedor_IdEvento", "IX_ProcessedNotifications_Provider_EventId"),
        ("IX_Pagamentos_PedidoId", "IX_Payments_OrderId"),
        ("IX_Pagamentos_Provedor_IdExterno", "IX_Payments_Provider_ExternalId"),
        ("IX_Pedidos_ClienteId", "IX_Orders_CustomerId"),
        ("IX_Pedidos_Codigo", "IX_Orders_Code"),
        ("IX_Pedidos_Status_PagarAte", "IX_Orders_Status_PayBy"),
        ("IX_ProdutoImagens_ProdutoId", "IX_ProductImages_ProductId"),
    ];

    private static readonly (string Table, string Old, string New)[] Constraints =
    [
        ("Products", "PK_Produtos", "PK_Products"),
        ("ProductImages", "PK_ProdutoImagens", "PK_ProductImages"),
        ("Orders", "PK_Pedidos", "PK_Orders"),
        ("OrderItems", "PK_ItensPedido", "PK_OrderItems"),
        ("Payments", "PK_Pagamentos", "PK_Payments"),
        ("ProcessedNotifications", "PK_NotificacoesProcessadas", "PK_ProcessedNotifications"),
        ("AuditEntries", "PK_Auditoria", "PK_AuditEntries"),
        ("Products", "CK_Produtos_Estoque", "CK_Products_Stock"),
        ("OrderItems", "FK_ItensPedido_Pedidos_PedidoId", "FK_OrderItems_Orders_OrderId"),
        ("OrderItems", "FK_ItensPedido_Produtos_ProdutoId", "FK_OrderItems_Products_ProductId"),
        ("Payments", "FK_Pagamentos_Pedidos_PedidoId", "FK_Payments_Orders_OrderId"),
        ("Orders", "FK_Pedidos_AspNetUsers_ClienteId", "FK_Orders_AspNetUsers_CustomerId"),
        ("ProductImages", "FK_ProdutoImagens_Produtos_ProdutoId", "FK_ProductImages_Products_ProductId"),
    ];

    // Valores guardados como texto: status, papéis, permissões e ações da auditoria
    private static readonly (string Table, string Column, string Old, string New)[] Values =
    [
        ("Orders", "Status", "AguardandoPagamento", "AwaitingPayment"),
        ("Orders", "Status", "Pago", "Paid"),
        ("Orders", "Status", "EmPreparo", "Preparing"),
        ("Orders", "Status", "Enviado", "Shipped"),
        ("Orders", "Status", "Entregue", "Delivered"),
        ("Orders", "Status", "Cancelado", "Cancelled"),
        ("Payments", "Status", "Pendente", "Pending"),
        ("Payments", "Status", "Aprovado", "Approved"),
        ("Payments", "Status", "Recusado", "Rejected"),
        ("Payments", "Status", "Cancelado", "Cancelled"),
        ("Payments", "Status", "Estornado", "Refunded"),
        ("Payments", "Method", "CartaoCredito", "CreditCard"),
        ("Payments", "Provider", "simulado", "simulated"),
        ("ProcessedNotifications", "Provider", "simulado", "simulated"),
        ("AuditEntries", "Action", "pedido.criado", "order.created"),
        ("AuditEntries", "Action", "pedido.cancelado", "order.cancelled"),
        ("AuditEntries", "Action", "pedido.expirado", "order.expired"),
        ("AuditEntries", "Action", "pedido.itens", "order.items"),
        ("AuditEntries", "Action", "pedido.entrega", "order.delivery"),
        ("AuditEntries", "Action", "pedido.preparo", "order.preparing"),
        ("AuditEntries", "Action", "pedido.enviado", "order.shipped"),
        ("AuditEntries", "Action", "pedido.entregue", "order.delivered"),
        ("AuditEntries", "Action", "pagamento.notificacao", "payment.notified"),
        ("AuditEntries", "Action", "pagamento.divergente", "payment.mismatch"),
        ("AuditEntries", "Action", "pagamento.manual", "payment.manual"),
        ("AuditEntries", "Action", "produto.criado", "product.created"),
        ("AuditEntries", "Action", "produto.editado", "product.edited"),
        ("AuditEntries", "Action", "acesso.alterado", "access.changed"),
        ("AuditEntries", "Action", "grupo.criado", "group.created"),
        ("AuditEntries", "Action", "grupo.permissoes", "group.permissions"),
        ("AuditEntries", "Action", "grupo.excluido", "group.deleted"),
        ("AuditEntries", "Entity", "Pedido", "Order"),
        ("AuditEntries", "Entity", "Produto", "Product"),
        ("AuditEntries", "Entity", "Usuario", "User"),
        ("AuditEntries", "Entity", "Grupo", "Group"),
        ("AuditEntries", "Author", "sistema", "system"),
        ("AspNetUserClaims", "ClaimValue", "produtos.gerenciar", "products.manage"),
        ("AspNetUserClaims", "ClaimValue", "pedidos.ver", "orders.view"),
        ("AspNetUserClaims", "ClaimValue", "pedidos.criar", "orders.create"),
        ("AspNetUserClaims", "ClaimValue", "pedidos.editar", "orders.edit"),
        ("AspNetUserClaims", "ClaimValue", "pedidos.cancelar", "orders.cancel"),
        ("AspNetUserClaims", "ClaimValue", "clientes.ver", "customers.view"),
        ("AspNetRoleClaims", "ClaimValue", "produtos.gerenciar", "products.manage"),
        ("AspNetRoleClaims", "ClaimValue", "pedidos.ver", "orders.view"),
        ("AspNetRoleClaims", "ClaimValue", "pedidos.criar", "orders.create"),
        ("AspNetRoleClaims", "ClaimValue", "pedidos.editar", "orders.edit"),
        ("AspNetRoleClaims", "ClaimValue", "pedidos.cancelar", "orders.cancel"),
        ("AspNetRoleClaims", "ClaimValue", "clientes.ver", "customers.view"),
        ("AspNetUserClaims", "ClaimType", "permissao", "permission"),
        ("AspNetRoleClaims", "ClaimType", "permissao", "permission"),
        ("AspNetRoles", "Name", "Cliente", "Customer"),
        ("AspNetRoles", "NormalizedName", "CLIENTE", "CUSTOMER"),
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var column in Columns)
            RenameColumn(migrationBuilder, column.Table, column.Old, column.New);

        foreach (var table in Tables)
            RenameTable(migrationBuilder, table.Old, table.New);

        foreach (var index in Indexes)
            RenameIndex(migrationBuilder, index.Old, index.New);

        foreach (var constraint in Constraints)
            RenameConstraint(migrationBuilder, constraint.Table, constraint.Old, constraint.New);

        foreach (var value in Values)
            UpdateValue(migrationBuilder, value.Table, value.Column, value.Old, value.New);

        // A verificação em duas etapas saiu da loja: sem as telas, uma conta com ela ligada não
        // conseguiria mais entrar. O Down não recupera isto: a pessoa precisaria ativar de novo.
        migrationBuilder.Sql("""UPDATE "AspNetUsers" SET "TwoFactorEnabled" = FALSE;""");
        migrationBuilder.Sql("""DELETE FROM "AspNetUserTokens" WHERE "Name" IN ('AuthenticatorKey', 'RecoveryCodes');""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var value in Values)
            UpdateValue(migrationBuilder, value.Table, value.Column, value.New, value.Old);

        foreach (var constraint in Constraints)
            RenameConstraint(migrationBuilder, constraint.Table, constraint.New, constraint.Old);

        foreach (var index in Indexes)
            RenameIndex(migrationBuilder, index.New, index.Old);

        foreach (var table in Tables)
            RenameTable(migrationBuilder, table.New, table.Old);

        foreach (var column in Columns)
            RenameColumn(migrationBuilder, column.Table, column.New, column.Old);
    }

    private static void RenameColumn(MigrationBuilder migrationBuilder, string table, string from, string to) =>
        migrationBuilder.RenameColumn(name: from, table: table, newName: to);

    // A sequência dos Ids acompanha a tabela, para o banco ficar igual ao de uma instalação nova
    private static void RenameTable(MigrationBuilder migrationBuilder, string from, string to)
    {
        migrationBuilder.RenameTable(name: from, newName: to);
        migrationBuilder.Sql($"""ALTER SEQUENCE "{from}_Id_seq" RENAME TO "{to}_Id_seq";""");
    }

    private static void RenameIndex(MigrationBuilder migrationBuilder, string from, string to) =>
        migrationBuilder.Sql($"""ALTER INDEX "{from}" RENAME TO "{to}";""");

    private static void RenameConstraint(MigrationBuilder migrationBuilder, string table, string from, string to) =>
        migrationBuilder.Sql($"""ALTER TABLE "{table}" RENAME CONSTRAINT "{from}" TO "{to}";""");

    private static void UpdateValue(MigrationBuilder migrationBuilder, string table, string column, string from, string to) =>
        migrationBuilder.Sql($"""UPDATE "{table}" SET "{column}" = '{to}' WHERE "{column}" = '{from}';""");
}
