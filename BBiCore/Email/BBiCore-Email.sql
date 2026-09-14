-- =============================================================================
--  BBiCore-Email.sql  —  Tabelas do módulo de e-mail (SQL Server)
-- -----------------------------------------------------------------------------
--  ESTE É O MÍNIMO. Cada sistema pode ACRESCENTAR colunas (IdArea, IdUsuario,
--  DataAlteracao, o que precisar), mas nunca ter MENOS do que está aqui — é o
--  que os contratos da biblioteca esperam encontrar.
--
--  TRÊS TABELAS:
--    Template       — o e-mail reutilizável.
--    Anexo          — o ACERVO: arquivos cadastrados uma vez, reutilizáveis.
--    TemplateAnexo  — o VÍNCULO: como um template usa um arquivo do acervo.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- Template
-- -----------------------------------------------------------------------------
CREATE TABLE Template
(
    Id                      INT IDENTITY(1,1)   NOT NULL,
    Nome                    VARCHAR(100)        NOT NULL,   -- chave de negócio
    Assunto                 VARCHAR(500)        NOT NULL,
    Corpo                   VARCHAR(MAX)        NOT NULL,   -- HTML. ÚNICA fonte da verdade do conteúdo.
    Destinatarios           VARCHAR(1000)       NULL,       -- separados por ';', podem conter {{marcadores}}
    Cc                      VARCHAR(1000)       NULL,
    Cco                     VARCHAR(1000)       NULL,

    -- Normal: a biblioteca é dona de 100% do HTML e o assistente consegue reabrir.
    -- Avançado: o usuário escreve HTML livre. A ida para avançado é DEFINITIVA.
    OrigemCriacao           TINYINT             NOT NULL DEFAULT 0,   -- 0=Normal, 1=Avancado

    -- Rascunho não pode ser enviado. Publicar é o que libera o template para os sistemas.
    Situacao                TINYINT             NOT NULL DEFAULT 0,   -- 0=Rascunho, 1=Publicado

    Classificacao           TINYINT             NOT NULL DEFAULT 0,   -- 0=Interno, 1=Confidencial, 2=Publico
    AcaoNaFalhaDeAnexo      TINYINT             NOT NULL DEFAULT 0,   -- 0=EnviarMesmoAssim, 1=FalharEnvio
    AcaoNaFalhaDeMarcador   TINYINT             NOT NULL DEFAULT 0,   -- 0=FalharEnvio, 1=EnviarMesmoAssim
    TipoDadosNome           VARCHAR(200)        NULL,                 -- tipo que preenche os marcadores

    CONSTRAINT PK_Template PRIMARY KEY (Id),
    CONSTRAINT UQ_Template_Nome UNIQUE (Nome)
);
GO

-- A tela de seleção pede só os publicados.
CREATE INDEX IX_Template_Situacao ON Template (Situacao);
GO

-- -----------------------------------------------------------------------------
-- Anexo  (acervo)
-- -----------------------------------------------------------------------------
CREATE TABLE Anexo
(
    Id              INT IDENTITY(1,1)   NOT NULL,
    NomeArquivo     VARCHAR(260)        NOT NULL,
    ContentType     VARCHAR(100)        NULL,       -- define se o arquivo PODE ser recurso de corpo
    Descricao       VARCHAR(300)        NULL,       -- ajuda o usuário a reconhecer o arquivo na lista

    -- Como o conteúdo é obtido. Vale para qualquer arquivo, em qualquer papel.
    ModoObtencao    TINYINT             NOT NULL DEFAULT 0,  -- 0=BytesNoBanco, 1=CaminhoFixo, 2=CaminhoDinamico

    Conteudo        VARBINARY(MAX)      NULL,       -- só quando ModoObtencao = BytesNoBanco
    Caminho         VARCHAR(500)        NULL,       -- só nos modos de caminho (o dinâmico aceita {{marcadores}})

    CONSTRAINT PK_Anexo PRIMARY KEY (Id),

    -- Conteúdo e caminho são mutuamente exclusivos: ou os bytes vieram junto, ou serão buscados.
    CONSTRAINT CK_Anexo_Origem CHECK
    (
        (ModoObtencao = 0 AND Conteudo IS NOT NULL AND Caminho IS NULL)
        OR
        (ModoObtencao IN (1, 2) AND Caminho IS NOT NULL AND Conteudo IS NULL)
    )
);
GO

-- -----------------------------------------------------------------------------
-- TemplateAnexo  (vínculo)
-- -----------------------------------------------------------------------------
CREATE TABLE TemplateAnexo
(
    Id                  INT IDENTITY(1,1)   NOT NULL,
    IdTemplate          INT                 NOT NULL,
    IdAnexo             INT                 NOT NULL,

    -- Papel do arquivo NESTE template. Cabeçalho/Rodapé/Inline são RECURSOS DO CORPO
    -- (vão embutidos por cid:, fora do clipe) e só valem para IMAGEM.
    Papel               TINYINT             NOT NULL DEFAULT 3,  -- 0=Cabecalho, 1=Rodape, 2=Inline, 3=Anexo

    -- Este template REIVINDICA o arquivo: ele deixa de ser oferecido aos demais.
    -- Fica no vínculo (não no acervo) porque a regra é verificada na CRIAÇÃO — quando
    -- ainda dá para barrar sem afetar templates que já usavam o arquivo.
    Exclusivo           BIT                 NOT NULL DEFAULT 0,

    -- Usado no HTML como cid:{ContentId}. Existe SE E SOMENTE SE o papel for de corpo.
    -- É GERADO pela biblioteca — nunca digitado pelo usuário.
    ContentId           VARCHAR(80)         NULL,

    Ordem               INT                 NOT NULL DEFAULT 0,
    ExcluirAposAnexar   BIT                 NOT NULL DEFAULT 0,  -- só faz sentido nos modos de caminho

    CONSTRAINT PK_TemplateAnexo PRIMARY KEY (Id),

    CONSTRAINT FK_TemplateAnexo_Template FOREIGN KEY (IdTemplate)
        REFERENCES Template (Id) ON DELETE CASCADE,

    CONSTRAINT FK_TemplateAnexo_Anexo FOREIGN KEY (IdAnexo)
        REFERENCES Anexo (Id),

    -- ContentId existe exatamente quando o papel é de corpo (0, 1 ou 2).
    CONSTRAINT CK_TemplateAnexo_ContentId CHECK
    (
        (Papel IN (0, 1, 2) AND ContentId IS NOT NULL)
        OR
        (Papel = 3 AND ContentId IS NULL)
    )
);
GO

-- Cabeçalho e rodapé: no máximo UM por template (a regra da biblioteca substitui o anterior;
-- este índice garante que nem um erro de gravação consiga criar o segundo).
CREATE UNIQUE INDEX UX_TemplateAnexo_PapelUnico
    ON TemplateAnexo (IdTemplate, Papel)
    WHERE Papel IN (0, 1);
GO

-- Listar os anexos de um template, e descobrir quem reivindicou um arquivo do acervo.
CREATE INDEX IX_TemplateAnexo_Template ON TemplateAnexo (IdTemplate);
GO

CREATE INDEX IX_TemplateAnexo_Anexo ON TemplateAnexo (IdAnexo, Exclusivo);
GO

-- =============================================================================
--  CONSULTA DE REFERÊNCIA — anexos DISPONÍVEIS para um template
--  (todos do acervo, menos os reivindicados com exclusividade por OUTROS)
-- =============================================================================
--
--  SELECT a.*
--  FROM   Anexo a
--  WHERE  a.Id NOT IN (
--             SELECT ta.IdAnexo
--             FROM   TemplateAnexo ta
--             WHERE  ta.Exclusivo = 1
--               AND  ta.IdTemplate <> @IdTemplate
--         );
-- =============================================================================
