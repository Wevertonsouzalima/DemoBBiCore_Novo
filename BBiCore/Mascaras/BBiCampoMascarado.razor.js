// Máscaras client-side. Formatam o campo enquanto o usuário digita; o Blazor lê o
// valor no change. O CNPJ aceita LETRAS na base (formato alfanumérico, IN RFB
// 2.229/2024): 12 caracteres alfanuméricos + 2 dígitos verificadores numéricos.

function digitos(s) {
    return (s || "").replace(/\D/g, "");
}

function alfanumerico(s) {
    return (s || "").toUpperCase().replace(/[^0-9A-Z]/g, "");
}

function fmtCpf(s) {
    let d = digitos(s).slice(0, 11), r = "";
    for (let i = 0; i < d.length; i++) {
        if (i === 3 || i === 6) r += ".";
        else if (i === 9) r += "-";
        r += d[i];
    }
    return r;
}

function fmtCnpj(s) {
    // Base (12 primeiros) aceita letras; os 2 verificadores são numéricos.
    let c = alfanumerico(s).slice(0, 14);
    const base = c.slice(0, 12);
    const dv = c.slice(12).replace(/\D/g, "");
    c = base + dv;

    let r = "";
    for (let i = 0; i < c.length; i++) {
        if (i === 2 || i === 5) r += ".";
        else if (i === 8) r += "/";
        else if (i === 12) r += "-";
        r += c[i];
    }
    return r;
}

// CPF ou CNPJ: se aparecer letra, só pode ser CNPJ; senão decide pelo tamanho.
function fmtDoc(s) {
    const c = alfanumerico(s);
    const temLetra = /[A-Z]/.test(c);
    return (temLetra || c.length > 11) ? fmtCnpj(s) : fmtCpf(s);
}

function fmtTel(s) {
    let d = digitos(s).slice(0, 11), cel = d.length > 10, r = "";
    for (let i = 0; i < d.length; i++) {
        if (i === 0) r += "(";
        else if (i === 2) r += ") ";
        else if ((cel && i === 7) || (!cel && i === 6)) r += "-";
        r += d[i];
    }
    return r;
}

function fmtCep(s) {
    const d = digitos(s).slice(0, 8);
    return d.length <= 5 ? d : d.slice(0, 5) + "-" + d.slice(5);
}

function fmtMoeda(s) {
    const d = digitos(s);
    if (!d) return "";
    const v = parseInt(d, 10) / 100;
    return v.toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function fmtData(s) {
    const d = digitos(s).slice(0, 8);
    let r = "";
    for (let i = 0; i < d.length; i++) {
        if (i === 2 || i === 4) r += "/";
        r += d[i];
    }
    return r;
}

const formatadores = {
    doc: fmtDoc,
    cpf: fmtCpf,
    cnpj: fmtCnpj,
    tel: fmtTel,
    cep: fmtCep,
    moeda: fmtMoeda,
    data: fmtData
};

// Liga a máscara a um campo. Idempotente.
export function ligar(elemento, tipo) {
    if (!elemento || elemento.dataset.bbiLigado) return;
    elemento.dataset.bbiLigado = "1";

    const formatar = formatadores[tipo] || (x => x);

    elemento.addEventListener("input", () => {
        elemento.value = formatar(elemento.value);
        const fim = elemento.value.length;
        elemento.setSelectionRange(fim, fim);
    });
}
