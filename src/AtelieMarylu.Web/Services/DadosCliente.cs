namespace AtelieMarylu.Web.Services;

// Só o que as telas do painel mostram. Hash de senha e CPF não saem do banco à toa.
public record DadosCliente(string Id, string NomeCompleto, string? Email, string? Telefone, DateTime CriadoEm)
{
    public string? NomeParaExibir => string.IsNullOrEmpty(NomeCompleto) ? Email : NomeCompleto;
}
