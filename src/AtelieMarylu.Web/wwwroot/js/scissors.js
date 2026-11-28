// Efeito das tesourinhas na caixa de frete (ShippingCalculator.razor), via JS interop (import dinâmico).
// Fluxo: o componente chama snapshot(caixa) antes de trocar a cotação (guarda uma cópia da lista antiga)
// e reveal(caixa) depois de renderizar a nova. As tesouras caem só dentro da caixa, e a lista nova aparece
// de cima para baixo conforme elas passam, enquanto a cópia da antiga é "cortada".
// Tudo é decorativo: a cotação já está na tela antes do efeito, então uma falha aqui não esconde nada.

const DURATION = 1300;       // ms da queda inteira
const COUNT = 50;            // quantidade de tesouras
const COLORS = ['--magenta', '--marca', '--rosa', '--marca-escura', '--lilas'];
const AVERAGE_SIZE = 30;     // usado para calcular quando a frente das tesouras chega na lista

// Cada lâmina é um grupo, para abrir e fechar como se estivesse cortando
const SVG = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round">
    <g class="blade-a"><circle cx="6.5" cy="5.5" r="3.3"/><path d="M8.6 8.2 L17 22" stroke-width="2.6"/></g>
    <g class="blade-b"><circle cx="17.5" cy="5.5" r="3.3"/><path d="M15.4 8.2 L7 22" stroke-width="2.6"/></g>
    <circle cx="12" cy="14" r="1" fill="currentColor" stroke="none"/>
</svg>`;

const random = (min, max) => min + Math.random() * (max - min);
const parts = (box) => ({
    layer: box.querySelector('[data-scissors]'),
    list: box.querySelector('[data-quote-results]'),
});

// Guarda uma cópia da cotação atual por cima dela, na camada das tesouras (que o Blazor renderiza vazia
// e nunca mexe, então o JS pode pôr elementos ali sem atrapalhar a comparação do Blazor)
export function snapshot(box) {
    const { layer, list } = parts(box);
    if (!layer) return;
    layer.replaceChildren();
    if (!list) return;

    list.getAnimations().forEach((a) => a.cancel());
    const ghost = list.cloneNode(true);
    ghost.removeAttribute('data-quote-results');
    ghost.classList.add('scissors-ghost');
    ghost.style.top = `${list.offsetTop}px`;
    ghost.style.left = `${list.offsetLeft}px`;
    ghost.style.width = `${list.offsetWidth}px`;
    layer.appendChild(ghost);
}

export function reveal(box) {
    const { layer, list } = parts(box);
    if (!layer) return;
    const ghost = layer.querySelector('.scissors-ghost');
    if (!list) { layer.replaceChildren(); return; }

    // Quem pediu menos movimento no sistema vê só uma troca curta, sem tesouras
    if (matchMedia('(prefers-reduced-motion: reduce)').matches) {
        list.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 200 });
        layer.replaceChildren();
        return;
    }

    dropScissors(box, layer);

    // A frente das tesouras desce pela caixa: a troca começa quando ela chega no topo das listas
    // e termina quando passa do fim delas (.09 = largada média; .94 = duração média da queda)
    const height = box.clientHeight;
    const top = Math.min(list.offsetTop, ghost?.offsetTop ?? Infinity);
    const bottom = Math.max(list.offsetTop + list.offsetHeight, ghost ? ghost.offsetTop + ghost.offsetHeight : 0);
    const when = (y) => DURATION * (0.09 + 0.94 * (y + AVERAGE_SIZE) / (height + 2 * AVERAGE_SIZE));
    const start = when(top);
    const timing = { delay: start, duration: Math.max(when(bottom) - start, 120), easing: 'linear', fill: 'both' };

    const entering = list.animate([{ clipPath: 'inset(0 0 100% 0)' }, { clipPath: 'inset(0 0 0% 0)' }], timing);
    ghost?.animate([{ clipPath: 'inset(0% 0 0 0)' }, { clipPath: 'inset(100% 0 0 0)' }], timing);

    entering.finished.then(() => entering.cancel(), () => { /* cancelada por um novo cálculo */ });
    setTimeout(() => layer.replaceChildren(), DURATION * 1.35);
}

function dropScissors(box, layer) {
    const width = box.clientWidth;
    layer.style.setProperty('--h', `${box.clientHeight}px`);
    const fragment = document.createDocumentFragment();
    for (let i = 0; i < COUNT; i++) {
        const size = random(16, 30);
        const el = document.createElement('div');
        el.className = 'scissor';
        // Espalha pela largura em faixas, com um pouco de sorteio, para não amontoar
        const x = ((i + random(0.1, 0.9)) / COUNT) * (width - size);
        el.style.cssText = [
            `--x:${x}px`,
            `--s:${size}px`,
            `--color:var(${COLORS[i % COLORS.length]})`,
            `--fall:${DURATION * random(0.88, 1)}ms`,
            `--delay:${DURATION * random(0, 0.18)}ms`,
            `--sway:${random(380, 650)}ms`,
            `--amp:${random(6, 16)}px`,
            `--tilt:${random(-30, 30)}deg`,
            `--cut:${random(260, 380)}ms`,
            `--phase:${-random(0, 380)}ms`,
        ].join(';');
        el.innerHTML = `<div class="scissor-sway">${SVG}</div>`;
        fragment.appendChild(el);
    }
    layer.appendChild(fragment);
}
