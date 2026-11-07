namespace AtelieMarylu.Domain.Catalog;

// Erro de regra ao gravar um produto. A mensagem é mostrada ao usuário.
public class ProductRuleException(string message) : Exception(message);
