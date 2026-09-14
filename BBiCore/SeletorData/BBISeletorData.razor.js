// BBISeletorData.razor.js
// Apoio do componente BBISeletorData. Não guarda regra de calendário: só
// posiciona o popover, informa se é celular, captura o swipe e aplica a
// máscara de digitação. A navegação de datas fica toda no C#.

let ouvintes = null;

// Retorna true quando a tela está abaixo da largura de quebra (celular).
export function ehCelular(larguraQuebra) {
    return window.matchMedia(`(max-width: ${larguraQuebra}px)`).matches;
}

// Abre o popover ancorado no campo e registra o fechamento ao clicar fora.
export function abrirPopover(campo, popover, dotNet) {
    posicionar(campo, popover);

    const aoClicarFora = (e) => {
        if (!popover.contains(e.target) && !campo.contains(e.target)) {
            dotNet.invokeMethodAsync('FecharPorClique');
        }
    };
    const aoReposicionar = () => posicionar(campo, popover);

    // Adia o listener de clique para não capturar o próprio clique de abertura.
    setTimeout(() => document.addEventListener('mousedown', aoClicarFora), 0);
    window.addEventListener('resize', aoReposicionar);
    window.addEventListener('scroll', aoReposicionar, true);

    ouvintes = { aoClicarFora, aoReposicionar };
}

// Remove os listeners do popover.
export function fecharPopover() {
    if (!ouvintes) {
        return;
    }
    document.removeEventListener('mousedown', ouvintes.aoClicarFora);
    window.removeEventListener('resize', ouvintes.aoReposicionar);
    window.removeEventListener('scroll', ouvintes.aoReposicionar, true);
    ouvintes = null;
}

// Posiciona o popover logo abaixo do campo; sobe se não couber, e recua na
// horizontal para não vazar da tela.
function posicionar(campo, popover) {
    const r = campo.getBoundingClientRect();
    const alturaPop = popover.offsetHeight;
    const larguraPop = popover.offsetWidth;

    let top = r.bottom + 4;
    const espacoAbaixo = window.innerHeight - r.bottom;
    if (espacoAbaixo < alturaPop + 8 && r.top > alturaPop + 8) {
        top = r.top - alturaPop - 4;
    }

    let left = r.left;
    if (left + larguraPop > window.innerWidth - 8) {
        left = window.innerWidth - larguraPop - 8;
    }
    if (left < 8) {
        left = 8;
    }

    popover.style.top = `${top}px`;
    popover.style.left = `${left}px`;
}

// Registra o swipe horizontal sobre a grade (celular). Avisa a direção ao C#.
export function registrarSwipe(elemento, dotNet) {
    let x0 = null;

    const inicio = (e) => { x0 = e.changedTouches[0].clientX; };
    const fim = (e) => {
        if (x0 === null) {
            return;
        }
        const dx = e.changedTouches[0].clientX - x0;
        if (Math.abs(dx) > 40) {
            dotNet.invokeMethodAsync('NavegarPorSwipe', dx < 0 ? 1 : -1);
        }
        x0 = null;
    };

    elemento.addEventListener('touchstart', inicio, { passive: true });
    elemento.addEventListener('touchend', fim, { passive: true });
}

// Máscara de digitação: dd/MM/yyyy ou dd/MM/yyyy HH:mm.
export function mascara(elemento, padrao) {
    const limite = padrao === 'datahora' ? 12 : 8;

    const aplicar = () => {
        let v = elemento.value.replace(/\D/g, '').substring(0, limite);
        let saida = '';
        for (let i = 0; i < v.length; i++) {
            if (i === 2 || i === 4) {
                saida += '/';
            } else if (i === 8) {
                saida += ' ';
            } else if (i === 10) {
                saida += ':';
            }
            saida += v[i];
        }
        elemento.value = saida;
    };

    elemento.addEventListener('input', aplicar);
}
