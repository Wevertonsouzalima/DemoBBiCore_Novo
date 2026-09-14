// Dispara o download de um arquivo a partir de bytes em base64.
export function baixar(nomeArquivo, base64, mime) {
    const link = document.createElement("a");
    link.href = `data:${mime};base64,${base64}`;
    link.download = nomeArquivo;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}
