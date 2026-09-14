// Encapsula o SortableJS. O gesto de arrastar vive inteiramente no cliente;
// o C# só é notificado ao soltar (onEnd) — nada trafega no circuito durante o arrasto.

export function iniciar(elemento, referenciaDotNet, opcoes) {
    if (typeof Sortable === "undefined") {
        console.error("[BBICore] SortableJS não carregado. Verifique o BBICoreAssets.");
        return null;
    }

    const instancia = Sortable.create(elemento, {
        handle: ".bbi-lista-ordenavel-alca",
        animation: 150,
        disabled: opcoes?.desabilitado === true,
        ghostClass: "bbi-lista-ordenavel-fantasma",
        chosenClass: "bbi-lista-ordenavel-escolhido",
        dragClass: "bbi-lista-ordenavel-arrastando",
        onEnd: function (evento) {
            // Lê a nova ordem direto do DOM, pelas chaves estáveis.
            const novaOrdemChaves = Array
                .from(elemento.querySelectorAll("[data-bbi-chave]"))
                .map(el => el.getAttribute("data-bbi-chave"));

            const dto = {
                chaveMovida: evento.item?.getAttribute("data-bbi-chave") ?? null,
                indiceOrigem: evento.oldIndex,
                indiceDestino: evento.newIndex,
                listaOrigem: elemento.getAttribute("data-bbi-lista") ?? null,
                listaDestino: (evento.to ?? elemento).getAttribute("data-bbi-lista") ?? null,
                novaOrdemChaves: novaOrdemChaves
            };

            referenciaDotNet.invokeMethodAsync("OnReordenado", dto);
        }
    });

    return {
        destruir: function () {
            instancia.destroy();
        }
    };
}