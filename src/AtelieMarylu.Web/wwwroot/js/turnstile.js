// Desenha o widget anti-robô do Cloudflare Turnstile nos formulários de login e cadastro.
// O script do Cloudflare só é baixado nas páginas que têm o widget: as outras ficam mais leves
// e não enviam dados do visitante ao Cloudflare (minimização de dados, LGPD).
// Como o Blazor troca o conteúdo da página sem recarregar (navegação aprimorada),
// a verificação roda também a cada "enhancedload".
(function () {
    var apiUrl = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit&onload=renderTurnstile";

    function renderWidgets() {
        document.querySelectorAll(".cf-turnstile:empty").forEach(function (el) {
            window.turnstile.render(el, {
                sitekey: el.dataset.sitekey,
                language: document.documentElement.lang.toLowerCase(),
                // Ocupa a largura do formulário, como o botão "Entrar" logo abaixo
                size: "flexible"
            });
        });
    }

    function setup() {
        if (!document.querySelector(".cf-turnstile")) return;
        if (window.turnstile) { renderWidgets(); return; }
        if (document.getElementById("cf-turnstile-api")) return; // já está sendo baixado

        var script = document.createElement("script");
        script.id = "cf-turnstile-api";
        script.src = apiUrl;
        script.async = true;
        document.head.appendChild(script);
    }

    // Chamado pelo próprio script do Cloudflare quando termina de carregar (parâmetro onload)
    window.renderTurnstile = renderWidgets;

    document.addEventListener("DOMContentLoaded", function () {
        setup();
        if (window.Blazor && window.Blazor.addEventListener) {
            window.Blazor.addEventListener("enhancedload", setup);
        }
    });
})();
