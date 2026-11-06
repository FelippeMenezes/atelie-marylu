namespace AtelieMarylu.Domain.Catalogo;

// Erro de regra ao gravar um produto. A mensagem é mostrada ao usuário.
public class RegraDeProdutoException(string mensagem) : Exception(mensagem);
