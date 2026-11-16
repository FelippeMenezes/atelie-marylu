// Service worker do PWA: um script que o navegador instala e que fica entre a loja e a internet.
//
// Decisão do AML-0032: ele NÃO guarda páginas da loja. O Blazor Server precisa do servidor a cada
// clique, e uma página guardada mostraria preço/estoque velhos ou um formulário com token vencido.
// O único papel dele é mostrar a offline.html quando o celular está sem internet.
//
// Ao mudar este arquivo (ou a offline.html), suba a versão: o navegador instala o worker novo
// e o "activate" apaga o cache antigo.
const CACHE = 'atelie-offline-v1';
const OFFLINE_URL = '/offline.html';
const PRECACHE = [OFFLINE_URL, '/img/pwa/icon-192.png'];

// Instalação: guarda a página offline e o ícone que ela usa
self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(CACHE)
            .then((cache) => cache.addAll(PRECACHE))
            .then(() => self.skipWaiting())
    );
});

// Ativação: apaga caches de versões anteriores, liga o "navigation preload" e assume as abas já abertas
self.addEventListener('activate', (event) => {
    event.waitUntil((async () => {
        const keys = await caches.keys();
        await Promise.all(keys.filter((key) => key !== CACHE).map((key) => caches.delete(key)));
        // Navigation preload: o navegador já pede a página ao servidor enquanto o worker acorda,
        // então interceptar a navegação não deixa a loja mais lenta
        if (self.registration.navigationPreload) {
            await self.registration.navigationPreload.enable();
        }
        await self.clients.claim();
    })());
});

// Toda requisição da página passa por aqui. Se o handler não chamar event.respondWith,
// a requisição segue normal para a internet, como se o service worker não existisse.
self.addEventListener('fetch', (event) => {
    const request = event.request;

    // Só GET e só da própria loja. Fica de fora: POST de formulário, outros domínios (fontes do Google,
    // Cloudflare) e, pelas regras abaixo, a conexão do Blazor (/_blazor), o webhook, imagens e CSS.
    const url = new URL(request.url);
    if (request.method !== 'GET' || url.origin !== self.location.origin) return;

    // Os arquivos que a própria offline.html usa (o ícone): rede primeiro, cache se não houver conexão
    if (request.mode !== 'navigate') {
        if (PRECACHE.includes(url.pathname)) {
            event.respondWith(fetch(request).catch(() => caches.match(url.pathname)));
        }
        return;
    }

    // Internet primeiro, sempre: a resposta do servidor nunca é guardada no cache.
    // A página offline só aparece quando a rede FALHA (sem conexão). Um erro 404/500 do servidor
    // chega normal, porque é uma resposta, não uma falha de rede.
    event.respondWith((async () => {
        try {
            const preloaded = await event.preloadResponse;
            return preloaded ?? await fetch(request);
        } catch {
            return (await caches.match(OFFLINE_URL)) ?? Response.error();
        }
    })());
});
