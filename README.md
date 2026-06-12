# QuestResume

QuestResume é um sistema **100% offline** para indexar o conteúdo de arquivos locais e
responder perguntas em linguagem natural sobre eles, usando busca full-text (Lucene.NET)
combinada com um modelo de IA local (LLM via LLamaSharp/llama.cpp, rodando na sua CPU).

Nenhuma chamada de rede é feita em tempo de execução, não há custo por token/requisição,
e seus arquivos nunca saem da máquina.

## Estrutura da solução

```
QuestResume.sln
src/
  QuestResume.Core/      Biblioteca com extração, indexação, busca e RAG (usada por todas as UIs)
  QuestResume.Cli/        Interface de linha de comando
  QuestResume.Api/        API local (ASP.NET Core) + interface web estática
  QuestResume.Desktop/    Aplicativo Desktop (WPF)
tests/
  QuestResume.Core.Tests/ Testes unitários do núcleo
```

As três interfaces (CLI, API+Web, Desktop) compartilham a mesma biblioteca `QuestResume.Core`
e o mesmo arquivo de configuração (`%LOCALAPPDATA%\QuestResume\config.json`), então indexar
pela CLI, por exemplo, deixa o índice disponível também na API e no Desktop.

## Formatos suportados (Grupo 1 — extração de texto)

Extração direta de texto, sem OCR nem transcrição:

- Documentos: PDF (incluindo PDF/A), DOCX, ODT, RTF
- Planilhas e apresentações: XLSX, PPTX
- Texto/dados: TXT, CSV, JSON, XML, HTML/HTM, CSS, JS, BIB, TEX, ICS, VCF
- Notebooks: IPYNB
- E-books: EPUB
- E-mails: EML, MSG

Arquivos com extensões não suportadas (ex.: imagens, áudio, vídeo, executáveis) não derrubam
a indexação: eles são listados como "ignorados" nas estatísticas. Veja [Próximos passos](#próximos-passos-grupos-2-4)
para como esses formatos podem ser adicionados depois.

## Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows (o app Desktop usa WPF; CLI e API funcionam em qualquer SO suportado pelo .NET 8)
- Opcional, mas necessário para perguntas com IA: um modelo de linguagem no formato `.gguf`

## Compilando

```powershell
dotnet build QuestResume.sln
```

## Baixando um modelo de IA local (.gguf)

A busca por palavras-chave (`search`) funciona sem nenhum modelo. Para perguntas em
linguagem natural (`ask`/`chat`/aba "Perguntar"), é preciso um modelo `.gguf` compatível
com llama.cpp. Modelos pequenos recomendados (gratuitos, baixados uma única vez):

- [Phi-3-mini-4k-instruct (GGUF)](https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-gguf) — ~2.4 GB (Q4)
- [Llama-3.2-3B-Instruct (GGUF)](https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF) — ~2 GB (Q4)

Baixe um arquivo `.gguf` (variante `Q4_K_M` é um bom equilíbrio entre tamanho e qualidade)
e salve em qualquer pasta local. Depois, configure o caminho via CLI, API ou Desktop
(veja abaixo).

## Configuração

Todas as interfaces leem/gravam o mesmo arquivo: `%LOCALAPPDATA%\QuestResume\config.json`.
Principais campos:

| Campo | Descrição | Padrão |
|---|---|---|
| `DocumentsFolder` | Última pasta indexada | (vazio) |
| `IndexPath` | Pasta onde o índice Lucene é salvo | `%LOCALAPPDATA%\QuestResume\index` |
| `ModelPath` | Caminho do arquivo `.gguf` | (vazio) |
| `TopK` | Nº de trechos recuperados por pergunta | 5 |
| `ChunkSize` | Tamanho (em caracteres) de cada trecho indexado | 1000 |
| `ChunkOverlap` | Sobreposição entre trechos consecutivos | 150 |
| `ContextSize` | Tamanho do contexto do LLM (tokens) | 4096 |

## Usando a CLI

```powershell
# Indexar uma pasta com seus documentos
dotnet run --project src/QuestResume.Cli -- index "C:\Users\voce\Documentos"

# Buscar por palavras-chave (não precisa de modelo de IA)
dotnet run --project src/QuestResume.Cli -- search "contrato de aluguel"

# Perguntar em linguagem natural (precisa de modelo .gguf configurado)
dotnet run --project src/QuestResume.Cli -- ask "Qual o valor do aluguel mencionado nos documentos?"

# Modo chat interativo
dotnet run --project src/QuestResume.Cli -- chat

# Configurações
dotnet run --project src/QuestResume.Cli -- config show
dotnet run --project src/QuestResume.Cli -- config set-model "C:\Modelos\Phi-3-mini-4k-instruct-q4.gguf"
dotnet run --project src/QuestResume.Cli -- config set-folder "C:\Users\voce\Documentos"
dotnet run --project src/QuestResume.Cli -- config set-index "C:\Users\voce\AppData\Local\QuestResume\index"
dotnet run --project src/QuestResume.Cli -- config set-top-k 5
```

## Usando a API + interface web

```powershell
dotnet run --project src/QuestResume.Api
```

Abra o endereço exibido no terminal (ex.: `http://localhost:5000`) no navegador para usar
a interface web: indexar uma pasta, buscar por palavras-chave, fazer perguntas e configurar
o caminho do modelo `.gguf`.

Endpoints disponíveis:

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/status` | Status do índice e do modelo configurado |
| `GET` | `/api/config` | Lê a configuração atual |
| `PUT` | `/api/config` | Atualiza a configuração |
| `POST` | `/api/index` | `{ "folderPath": "..." }` — indexa uma pasta |
| `POST` | `/api/search` | `{ "query": "...", "topK": 5 }` — busca sem IA |
| `POST` | `/api/ask` | `{ "question": "...", "topK": 5 }` — pergunta com RAG |

## Usando o app Desktop (WPF)

```powershell
dotnet run --project src/QuestResume.Desktop
```

A aba **Perguntas** permite escolher a pasta de documentos, indexar e conversar com a IA
sobre o conteúdo indexado (mostrando os arquivos-fonte de cada resposta). A aba
**Configurações** permite apontar o modelo `.gguf`, a pasta do índice, o Top-K e o tamanho
do contexto.

## Sem modelo configurado?

Indexação e busca por palavras-chave (`search` / aba "Buscar") funcionam sem nenhum modelo
de IA. Ao tentar fazer uma pergunta (`ask`/`chat`/aba "Perguntar") sem um `.gguf` válido
configurado, as três interfaces mostram uma mensagem explicando como configurar o modelo,
em vez de travar ou lançar um erro genérico.

## Testes

```powershell
dotnet test tests/QuestResume.Core.Tests
```

## Próximos passos (Grupos 2-4)

A arquitetura de extração é baseada em `IFileExtractor` + `ExtractorRegistry`
(`src/QuestResume.Core/Extraction`), permitindo plugar novos formatos sem alterar o
restante do sistema (indexação, busca e RAG continuam iguais). Grupos planejados para o
futuro:

- **Grupo 2 — Metadados (imagens, vídeo, áudio, executáveis)**: um novo extrator usando
  `MetadataExtractor` (ou `ExifTool`) preencheria apenas `ExtractedDocument.Metadata`
  (autor, datas, dimensões, codec, etc.), com `Text` vazio — útil para busca por metadados
  mesmo sem OCR/transcrição.
- **Grupo 3 — OCR (PDFs digitalizados e imagens)**: um `TesseractOcrExtractor` usando o
  pacote `Tesseract` rodaria OCR local sobre imagens e sobre páginas de PDF sem texto
  extraível (renderizadas via PdfPig), preenchendo `ExtractedDocument.Text` normalmente.
- **Grupo 4 — Transcrição de áudio/vídeo**: um `WhisperExtractor` usando `Whisper.net`
  (whisper.cpp, também local) transcreveria a trilha de áudio para texto, que entraria no
  mesmo pipeline de chunking/indexação/RAG.

Em todos os casos, basta implementar `IFileExtractor` e registrá-lo em
`ExtractorRegistry.DefaultExtractors()` — `DocumentIndexer`, `SearchService` e
`RagQueryEngine` não precisam de nenhuma alteração.
