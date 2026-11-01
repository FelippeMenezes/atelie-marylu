// Funções pequenas usadas pelos componentes Blazor via JS interop (import dinâmico do módulo)
export async function copiarTexto(texto) {
    try {
        await navigator.clipboard.writeText(texto);
        return true;
    } catch {
        return false;
    }
}
