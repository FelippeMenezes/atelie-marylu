// Funções pequenas usadas pelos componentes Blazor via JS interop (import dinâmico do módulo)
export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}

// Abre o <dialog> como modal: o navegador prende o foco nele, escurece o fundo e fecha com Esc.
// Ao fechar, o navegador devolve o foco a quem abriu; se esse elemento sumiu da tela, o foco vai
// para focusOnCloseId. O evento "close" chega depois dessa devolução, então o nosso foco prevalece.
export function showDialog(dialog, focusOnCloseId) {
    if (dialog.open) return;
    if (focusOnCloseId) {
        dialog.addEventListener('close', () => document.getElementById(focusOnCloseId)?.focus(), { once: true });
    }
    dialog.showModal();
}
