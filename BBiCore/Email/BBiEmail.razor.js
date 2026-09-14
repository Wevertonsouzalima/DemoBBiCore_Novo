// Baixa um arquivo a partir de bytes em base64 (usado para o .eml do rascunho).
export function baixarArquivo(nomeArquivo, base64) {
    const link = document.createElement("a");
    link.href = `data:message/rfc822;base64,${base64}`;
    link.download = nomeArquivo;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}
