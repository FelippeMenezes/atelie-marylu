// Desenha o widget anti-robô do Cloudflare Turnstile nos formulários de login e cadastro.
// Como o Blazor troca o conteúdo da página sem recarregar (navegação aprimorada),
// redesenhamos também a cada "enhancedload".
(function () {
    function renderWidgets() {
        if (!window.turnstile) return;
        document.querySelectorAll(".cf-turnstile:empty").forEach(function (el) {
            window.turnstile.render(el, { sitekey: el.dataset.sitekey, language: "pt-br" });
        });
    }

    window.renderTurnstile = renderWidgets;

    document.addEventListener("DOMContentLoaded", function () {
        if (window.Blazor && window.Blazor.addEventListener) {
            window.Blazor.addEventListener("enhancedload", renderWidgets);
        }
    });
})();
