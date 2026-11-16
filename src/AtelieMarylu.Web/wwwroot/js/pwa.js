// PWA: registra o service worker e controla o botão "Instalar o app" da vitrine.
// Fica em arquivo separado porque a CSP não aceita script inline.

// O endereço é fixo (/service-worker.js, sem o hash do @Assets): o service worker só controla
// as páginas abaixo da pasta onde ele está, e trocar o endereço a cada deploy criaria um worker novo.
if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
        navigator.serviceWorker.register('/service-worker.js', { scope: '/' }).catch(() => {
            // Sem service worker a loja funciona igual, só não mostra a página offline
        });
    });
}

// Chrome, Edge e Android avisam pelo "beforeinstallprompt" que a loja pode ser instalada.
// Guardamos o aviso para abrir a janela de instalação só quando a pessoa clicar no botão.
// O Safari (iPhone) não tem esse aviso: lá o botão mostra como instalar pelo menu Compartilhar.
let installPrompt = null;

const isInstalled = () =>
    window.matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;

// iPadOS se apresenta como Mac; o toque é o que diferencia
const isIos = /iphone|ipad|ipod/i.test(navigator.userAgent)
    || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);

function updateInstallButton() {
    const button = document.querySelector('[data-pwa-install]');
    if (button) {
        button.hidden = isInstalled() || !(installPrompt || isIos);
    }
}

window.addEventListener('beforeinstallprompt', (event) => {
    event.preventDefault();
    installPrompt = event;
    updateInstallButton();
});

window.addEventListener('appinstalled', () => {
    installPrompt = null;
    updateInstallButton();
});

// Um único ouvinte no document: a navegação aprimorada do Blazor troca o HTML da página,
// e um ouvinte preso no botão se perderia ao sair e voltar para a vitrine
document.addEventListener('click', async (event) => {
    // closest só existe em elementos; um clique vindo de outro tipo de nó não interessa aqui
    if (!(event.target instanceof Element)) return;
    const button = event.target.closest('[data-pwa-install]');
    if (!button) return;

    if (installPrompt) {
        // O aviso só pode ser usado uma vez; o navegador manda outro se a pessoa recusar
        const prompt = installPrompt;
        installPrompt = null;
        await prompt.prompt();
        await prompt.userChoice;
        updateInstallButton();
        return;
    }

    const hint = document.querySelector('[data-pwa-ios-hint]');
    if (hint) {
        hint.hidden = !hint.hidden;
        button.setAttribute('aria-expanded', String(!hint.hidden));
    }
});

// Sem internet, a navegação aprimorada do Blazor (que busca a página por fetch) falha calada:
// a URL muda e a tela fica parada. Nesse caso, trocamos por uma abertura de página comum, que
// passa pelo service worker e mostra a offline.html. A fase de captura no window roda antes do Blazor.
window.addEventListener('click', (event) => {
    if (navigator.onLine || event.defaultPrevented || event.button !== 0
        || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    if (!(event.target instanceof Element)) return;

    const link = event.target.closest('a[href]');
    if (!link || link.target || link.hasAttribute('download') || link.origin !== location.origin) return;

    event.preventDefault();
    event.stopPropagation();
    location.assign(link.href);
}, true);

updateInstallButton();
// A navegação aprimorada do Blazor traz o botão de volta com "hidden"; recalcula a cada troca de página
window.Blazor?.addEventListener('enhancedload', updateInstallButton);
