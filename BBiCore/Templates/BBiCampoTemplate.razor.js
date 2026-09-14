// Insere um texto na posição do cursor de um input/textarea, mantendo o foco.
export function inserirNoCursor(id, texto) {
    const el = document.getElementById(id);
    if (!el) return;
    const ini = el.selectionStart ?? el.value.length;
    const fim = el.selectionEnd ?? el.value.length;
    el.value = el.value.substring(0, ini) + texto + el.value.substring(fim);
    const pos = ini + texto.length;
    el.setSelectionRange(pos, pos);
    el.focus();
    // dispara input para o Blazor capturar o novo valor
    el.dispatchEvent(new Event("input", { bubbles: true }));
}
