// Funções pequenas usadas pelos componentes Blazor via JS interop (import dinâmico do módulo)
export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}
