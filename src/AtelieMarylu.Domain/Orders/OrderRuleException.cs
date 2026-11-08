namespace AtelieMarylu.Domain.Orders;

// Erro de regra do pedido. Template é o texto em inglês com {0}, {1}: as telas o usam como
// chave de tradução. Message traz o texto pronto, para logs e auditoria.
public class OrderRuleException(string template, params object[] arguments) : Exception(string.Format(template, arguments))
{
    public string Template { get; } = template;
    public object[] Arguments { get; } = arguments;
}
