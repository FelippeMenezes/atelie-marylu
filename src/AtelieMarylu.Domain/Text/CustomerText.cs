using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AtelieMarylu.Domain.Text;

// Regras para o texto livre que a cliente digita e que a loja depois escreve em e-mails (AML-0042).
//
// O problema: a loja não exige confirmar o e-mail (AML-0027). Alguém pode cadastrar o e-mail de outra pessoa com o
// nome "Seu cartão foi bloqueado, ligue 0800... ou acesse golpe.com" e fazer um pedido. A vítima recebe, do domínio
// da loja e com SPF/DKIM válidos, um e-mail com esse texto. O Razor impede HTML, mas os programas de e-mail
// transformam sozinhos texto de URL em link clicável. Um único e-mail de golpe com a marca do ateliê já prejudica a
// reputação do domínio (e pode pô-lo em listas de bloqueio, derrubando também os e-mails legítimos).
//
// A defesa, padrão de mercado: o texto livre que vai para e-mail não carrega link, e-mail nem telefone, e a
// saudação usa só o primeiro nome (uma palavra não forma uma frase de golpe).
// - Nome: lista do que PODE (o formato é previsível): letras, espaço, apóstrofo, ponto e hífen. Sem dígitos.
// - Endereço: precisa de dígitos e pontuação, então lista o que NÃO pode: "://", "www.", "@", palavra colada a outra
//   por ponto (cara de domínio, salvo abreviações de endereço) e 8 ou mais dígitos seguidos (telefone).
// A mesma regra vale na entrada (telas e Domain) e na saída (e-mails), para os dados antigos já gravados.
// Os testes (CustomerTextTests) cobrem as variações que um atacante tentaria, não só o caso óbvio.
public static partial class CustomerText
{
    public const int NameMaxLength = 80;

    // Começa com letra; no meio, letras, marcas de acento, espaço, apóstrofo (' e o ’ que o celular põe sozinho),
    // ponto e hífen; termina com letra ou ponto ("Souza Jr."). Aceita "Maria José", "D'Ávila", "J. P. Souza".
    // Junto com DotNotFollowedByInitial (ponto colado) e StackedMarks (acentos empilhados).
    [GeneratedRegex(@"^[\p{L}\p{M}](?:[\p{L}\p{M}'’ .\-]*[\p{L}\p{M}.])?$")]
    private static partial Regex NamePattern();

    // No nome, o ponto vem depois de uma inicial ou abreviação e é seguido de espaço, do fim ou de OUTRA INICIAL
    // (uma letra só, seguida de ponto, espaço ou fim): "J.P. Souza" passa. Ponto seguido de 2 ou mais letras
    // ("golpe.com", "t.me") é cara de domínio, que o programa de e-mail transformaria em link, seja qual for a
    // terminação.
    [GeneratedRegex(@"\.(?!\s|$|\p{L}(?:[.\s]|$))")]
    private static partial Regex DotNotFollowedByInitial();

    // 3 ou mais acentos seguidos sobre uma letra ("Zalgo"): o texto aparece embaralhado e esconde o que diz.
    // Nenhum nome ou endereço real em português ou espanhol passa de 2.
    [GeneratedRegex(@"\p{M}{3,}")]
    private static partial Regex StackedMarks();

    // Palavra colada a outra por ponto ("golpe.ru", "bit.ly", "Av.Brasil"). A palavra antes do ponto decide: se for
    // uma abreviação de endereço conhecida (KnownAbbreviations), é endereço; senão, é cara de domínio. Lista de
    // abreviações em vez de lista de terminações de domínio: as abreviações de endereço em português são poucas e
    // estáveis; as terminações de domínio são centenas e surgem novas todo ano.
    [GeneratedRegex(@"([\p{L}\d\-]+)\.(?=\p{L}{2})")]
    private static partial Regex WordGluedByDot();

    // Abreviações comuns em endereço brasileiro, sem acento e em minúsculas (a comparação tira os acentos)
    private static readonly HashSet<string> KnownAbbreviations =
    [
        // tipo de logradouro
        "r", "av", "avda", "al", "rod", "estr", "est", "trav", "tv", "pca", "pc", "lgo", "lg", "vl", "jd", "jdm",
        "pq", "res", "cond", "conj", "cj", "ed", "edif", "lot", "set", "faz", "sit", "ch", "nuc",
        // unidade e complemento
        "apto", "apt", "ap", "bl", "qd", "q", "lt", "lj", "cs", "sl", "km", "esq", "prox", "fds", "fte", "cx", "n",
        // títulos e nomes de rua
        "s", "sta", "sto", "sr", "sra", "dr", "dra", "prof", "profa", "pres", "mal", "gal", "gen", "cel", "cap", "alm",
        "brig", "des", "eng", "sen", "dep", "ver", "pe", "fr", "min", "cmte", "ten", "sgt", "maj", "visc", "bar", "cons",
    ];

    // Tira tudo o que não é letra nem dígito ("0800/123-4567" vira "08001234567"), para achar o telefone escrito
    // com qualquer separador. Letras continuam quebrando a sequência: "Rua 25 de Março 1200" não vira número.
    [GeneratedRegex(@"[^\p{L}\d]")]
    private static partial Regex NotLetterOrDigit();

    // Telefone: 8 ou mais dígitos seguidos. Número de casa, quadra e lote reais não chegam a isso; o CEP tem campo
    // próprio e não passa por aqui.
    [GeneratedRegex(@"\d{8,}")]
    private static partial Regex LongDigitRun();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\.(?=\S)")]
    private static partial Regex DotGluedToText();

    // Forma única para gravar e comparar: sem espaços nas pontas nem repetidos, e em NFC ("é" num caractere só,
    // e não "e" + acento solto). O mesmo nome digitado em teclados diferentes vira o mesmo texto no banco.
    public static string Normalize(string? text) =>
        Spaces().Replace((text ?? "").Normalize(NormalizationForm.FormC), " ").Trim();

    public static bool IsValidName(string? name)
    {
        var text = Normalize(name);
        return text.Length is > 0 and <= NameMaxLength
            && NamePattern().IsMatch(text)
            && !DotNotFollowedByInitial().IsMatch(text)
            && !StackedMarks().IsMatch(text);
    }

    // Para a SAÍDA (saudação dos e-mails): o primeiro nome, se o nome inteiro passar na regra; senão null, e o e-mail
    // usa "Olá!". Só o primeiro nome, padrão de mercado ("Olá, Maria!"): mesmo um nome válido só com letras pode ser
    // uma frase ("Seu pedido foi bloqueado procure..."), e uma palavra não é. Protege também as contas antigas.
    public static string? SafeFirstNameOrNull(string? name) =>
        IsValidName(name) ? Normalize(name).Split(' ')[0] : null;

    // Para o nome que vem do Google, que não dá para recusar: tira o que a regra não aceita (dígitos, emoji,
    // parênteses...), junta os espaços e corta no limite. Devolve "" se não sobrar um nome válido; a pessoa
    // digita o nome no formulário, que valida de novo.
    public static string CleanName(string? raw)
    {
        var kept = new StringBuilder();
        foreach (var c in (raw ?? "").Normalize(NormalizationForm.FormC))
        {
            var category = char.GetUnicodeCategory(c);
            var isLetter = char.IsLetter(c) || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
            kept.Append(isLetter || c is '\'' or '’' or '.' or '-' ? c : ' ');
        }

        // "golpe.com" vira "golpe. com": ponto colado em texto não passa na regra (e não vira link)
        var text = Normalize(DotGluedToText().Replace(kept.ToString(), ". "));
        var start = 0;
        while (start < text.Length && !char.IsLetter(text[start])) start++;
        text = text[start..];
        if (text.Length > NameMaxLength) text = text[..NameMaxLength];
        text = text.TrimEnd(' ', '\'', '’', '-');
        return IsValidName(text) ? text : "";
    }

    // Uma parte do endereço (rua, número, complemento, bairro, cidade). Vazio é aceito: o [Required] decide.
    public static bool IsSafeAddressPart(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var normalized = Normalize(text);
        if (normalized.Contains("://", StringComparison.Ordinal)
            || normalized.Contains("www.", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains('@')
            || StackedMarks().IsMatch(normalized))
            return false;
        if (WordGluedByDot().Matches(normalized).Any(m => !KnownAbbreviations.Contains(WithoutAccents(m.Groups[1].Value))))
            return false;
        return !LongDigitRun().IsMatch(NotLetterOrDigit().Replace(normalized, ""));
    }

    // "Pça" → "pca", para comparar com a lista de abreviações
    private static string WithoutAccents(string word)
    {
        var plain = new StringBuilder();
        foreach (var c in word.Normalize(NormalizationForm.FormD))
            if (char.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                plain.Append(char.ToLowerInvariant(c));
        return plain.ToString();
    }
}
