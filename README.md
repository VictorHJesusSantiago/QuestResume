# QuestResume

QuestResume é um sistema **100% offline** para indexar o conteúdo de arquivos locais e
responder perguntas em linguagem natural sobre eles, usando busca full-text (Lucene.NET),
opcionalmente combinada com busca vetorial (embeddings), e um modelo de IA local para
gerar as respostas (LLamaSharp/llama.cpp embutido, ou Ollama como alternativa).

Nenhuma chamada de rede é feita em tempo de execução (mesmo com Ollama, que roda
localmente), não há custo por token/requisição, e seus arquivos nunca saem da máquina.

Recursos opcionais (todos desabilitados por padrão, com degradação graciosa — o sistema
funciona normalmente sem eles):

- **OCR** (Tesseract 5): extrai texto de imagens e de PDFs digitalizados (sem texto
  selecionável).
- **Transcrição de áudio** (Whisper.net): extrai texto de arquivos `.wav` (16kHz mono).
- **Embeddings + busca híbrida**: combina busca por palavras-chave (BM25) com busca por
  similaridade semântica (vetores), melhorando a recuperação de contexto para o RAG.
- **Ollama**: usa um servidor [Ollama](https://ollama.com) local como alternativa ao
  modelo `.gguf` embutido.

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

Arquivos com extensões não suportadas (ex.: vídeo, executáveis) não derrubam a indexação:
eles são listados como "ignorados" nas estatísticas.

Com os recursos opcionais habilitados (veja abaixo), também são suportados:

- **Imagens** (`.png`, `.jpg`, `.jpeg`, `.tiff`, `.bmp`, `.gif`) — via OCR (Tesseract).
- **PDFs digitalizados** (sem texto selecionável) — páginas sem texto extraível são
  rasterizadas e passadas pelo OCR automaticamente.
- **Áudio** (`.wav`, 16kHz mono) — via transcrição (Whisper.net).

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

## Alternativa: usando Ollama

Em vez do modelo `.gguf` embutido (LLamaSharp), você pode usar um servidor
[Ollama](https://ollama.com) já instalado na sua máquina:

1. Instale o Ollama a partir de [ollama.com](https://ollama.com).
2. Baixe um modelo: `ollama pull llama3.2`.
3. Garanta que o servidor está rodando (`ollama serve`, ou já roda automaticamente após
   a instalação) — por padrão em `http://localhost:11434`.
4. Configure o QuestResume para usar o Ollama:

```powershell
dotnet run --project src/QuestResume.Cli -- config set-llm-provider Ollama
dotnet run --project src/QuestResume.Cli -- config set-ollama-url "http://localhost:11434"
dotnet run --project src/QuestResume.Cli -- config set-ollama-model "llama3.2"
```

Se o servidor Ollama não estiver acessível ao fazer uma pergunta, as três interfaces
mostram uma mensagem explicando como instalar/iniciar o Ollama, em vez de travar.

## OCR (Tesseract) — imagens e PDFs digitalizados

Para extrair texto de imagens (`.png`, `.jpg`, `.tiff`, `.bmp`, `.gif`) e de páginas de PDF
sem texto selecionável:

1. Baixe os arquivos de idioma (`tessdata`) do Tesseract 5, por exemplo de
   [tesseract-ocr/tessdata](https://github.com/tesseract-ocr/tessdata) (ou
   [tessdata_fast](https://github.com/tesseract-ocr/tessdata_fast) para arquivos menores).
   Salve os arquivos `.traineddata` (ex.: `por.traineddata`, `eng.traineddata`) em uma
   pasta local, ex. `C:\tessdata`.
2. Configure o caminho e habilite o OCR:

```powershell
dotnet run --project src/QuestResume.Cli -- config set-tessdata-path "C:\tessdata"
dotnet run --project src/QuestResume.Cli -- config set-ocr-languages "por+eng"
dotnet run --project src/QuestResume.Cli -- config set-ocr-enabled true
```

3. Reindexe a pasta de documentos (`index`) — imagens e PDFs digitalizados passam a ser
   processados via OCR. Sem `TessDataPath` configurado (ou com `OcrEnabled=false`), esses
   arquivos continuam sendo ignorados normalmente, sem erros.

## Transcrição de áudio (Whisper.net) — arquivos .wav

Para transcrever arquivos `.wav` (a transcrição entra no mesmo pipeline de indexação/RAG
dos demais documentos):

1. Baixe um modelo Whisper no formato ggml, por exemplo em
   [ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp/tree/main)
   (`ggml-base.bin` é um bom equilíbrio entre tamanho e qualidade; `ggml-small.bin` ou
   maior para melhor precisão).
2. Configure o caminho e habilite a transcrição:

```powershell
dotnet run --project src/QuestResume.Cli -- config set-whisper-model "C:\Modelos\whisper\ggml-base.bin"
dotnet run --project src/QuestResume.Cli -- config set-stt-enabled true
```

3. Reindexe a pasta de documentos (`index`) — arquivos `.wav` passam a ser transcritos.

**Importante**: o Whisper.net não faz resample — os arquivos `.wav` precisam estar em PCM
16kHz mono. Se um arquivo estiver em outro formato/taxa, o extrator tenta convertê-lo
automaticamente chamando o [ffmpeg](https://ffmpeg.org) (se estiver disponível no `PATH`).
Se o ffmpeg não estiver instalado ou a conversão falhar, o arquivo é ignorado com um aviso
explicando como convertê-lo manualmente:

```powershell
ffmpeg -i entrada.wav -ar 16000 -ac 1 saida.wav
```

## Embeddings e busca híbrida

Por padrão, a busca usa apenas BM25 (palavras-chave, via Lucene). Habilitando embeddings,
a busca passa a combinar BM25 com similaridade vetorial (semântica), o que melhora a
recuperação de contexto para perguntas que não usam exatamente as mesmas palavras do texto.

1. Baixe um modelo de embeddings multilíngue no formato ONNX — recomendado:
   [sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2](https://huggingface.co/sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2)
   (exporte para ONNX, ex. com `optimum-cli export onnx --model sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2 <pasta_saida>`,
   ou baixe uma versão já convertida). Você precisa de dois arquivos: o modelo `.onnx` e o
   `vocab.txt` do tokenizer (formato WordPiece/BERT).
2. Configure os caminhos e habilite:

```powershell
dotnet run --project src/QuestResume.Cli -- config set-embedding-model "C:\Modelos\embeddings\model.onnx"
dotnet run --project src/QuestResume.Cli -- config set-embedding-tokenizer "C:\Modelos\embeddings\vocab.txt"
dotnet run --project src/QuestResume.Cli -- config set-embeddings-enabled true
dotnet run --project src/QuestResume.Cli -- config set-hybrid-weight 0.5
```

`HybridBm25Weight` controla o peso do BM25 na combinação final (0 = só busca vetorial,
1 = só BM25; o padrão 0.5 dá peso igual aos dois).

3. Reindexe a pasta de documentos (`index`) — cada trecho indexado também é convertido em
   embedding e salvo em `vectors.db` (na pasta do índice). Sem `EmbeddingModelPath`/
   `EmbeddingTokenizerPath` configurados (ou com `EmbeddingsEnabled=false`), a busca
   continua 100% BM25, sem erros.

## Configuração

Todas as interfaces leem/gravam o mesmo arquivo: `%LOCALAPPDATA%\QuestResume\config.json`.
Principais campos:

| Campo | Descrição | Padrão |
|---|---|---|
| `DocumentsFolder` | Última pasta indexada | (vazio) |
| `IndexPath` | Pasta onde o índice Lucene (e o vector store) é salvo | `%LOCALAPPDATA%\QuestResume\index` |
| `ModelPath` | Caminho do arquivo `.gguf` (usado quando `LlmProvider=LlamaSharp`) | (vazio) |
| `TopK` | Nº de trechos recuperados por pergunta | 5 |
| `ChunkSize` | Tamanho (em caracteres) de cada trecho indexado | 1000 |
| `ChunkOverlap` | Sobreposição entre trechos consecutivos | 150 |
| `ContextSize` | Tamanho do contexto do LLM (tokens) | 4096 |
| `LlmProvider` | Provedor de geração: `LlamaSharp` ou `Ollama` | `LlamaSharp` |
| `OllamaBaseUrl` | URL do servidor Ollama local | `http://localhost:11434` |
| `OllamaModel` | Nome do modelo Ollama (ex.: `llama3.2`) | `llama3.2` |
| `OcrEnabled` | Habilita OCR (Tesseract) para imagens e PDFs digitalizados | `false` |
| `TessDataPath` | Pasta com os arquivos de idioma (`tessdata`) do Tesseract | (vazio) |
| `OcrLanguages` | Idiomas do OCR, formato `por+eng` | `por+eng` |
| `EmbeddingsEnabled` | Habilita embeddings e busca híbrida (BM25 + vetorial) | `false` |
| `EmbeddingModelPath` | Caminho do modelo de embeddings em formato ONNX | (vazio) |
| `EmbeddingTokenizerPath` | Caminho do `vocab.txt` do tokenizer do modelo de embeddings | (vazio) |
| `HybridBm25Weight` | Peso do BM25 na busca híbrida (0-1); o restante é o peso vetorial | 0.5 |
| `SttEnabled` | Habilita transcrição de áudio (`.wav`) via Whisper.net | `false` |
| `WhisperModelPath` | Caminho do modelo Whisper no formato ggml (`.bin`) | (vazio) |

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

# Provedor de IA (LlamaSharp embutido ou Ollama local)
dotnet run --project src/QuestResume.Cli -- config set-llm-provider <LlamaSharp|Ollama>
dotnet run --project src/QuestResume.Cli -- config set-ollama-url "http://localhost:11434"
dotnet run --project src/QuestResume.Cli -- config set-ollama-model "llama3.2"

# OCR (Tesseract) — veja "OCR (Tesseract)" acima
dotnet run --project src/QuestResume.Cli -- config set-ocr-enabled <true|false>
dotnet run --project src/QuestResume.Cli -- config set-tessdata-path "C:\tessdata"
dotnet run --project src/QuestResume.Cli -- config set-ocr-languages "por+eng"

# Embeddings e busca híbrida — veja "Embeddings e busca híbrida" acima
dotnet run --project src/QuestResume.Cli -- config set-embeddings-enabled <true|false>
dotnet run --project src/QuestResume.Cli -- config set-embedding-model "C:\Modelos\embeddings\model.onnx"
dotnet run --project src/QuestResume.Cli -- config set-embedding-tokenizer "C:\Modelos\embeddings\vocab.txt"
dotnet run --project src/QuestResume.Cli -- config set-hybrid-weight 0.5

# Transcrição de áudio (Whisper) — veja "Transcrição de áudio" acima
dotnet run --project src/QuestResume.Cli -- config set-stt-enabled <true|false>
dotnet run --project src/QuestResume.Cli -- config set-whisper-model "C:\Modelos\whisper\ggml-base.bin"
```

## Usando a API + interface web

```powershell
dotnet run --project src/QuestResume.Api
```

Abra o endereço exibido no terminal (ex.: `http://localhost:5000`) no navegador para usar
a interface web: indexar uma pasta, buscar por palavras-chave, fazer perguntas e configurar
o modelo de IA, OCR, transcrição de áudio e embeddings/busca híbrida na aba
Configurações.

Endpoints disponíveis:

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/status` | Status do índice, do provedor de IA e dos recursos opcionais (OCR, embeddings, STT) |
| `GET` | `/api/config` | Lê a configuração atual |
| `PUT` | `/api/config` | Atualiza a configuração |
| `POST` | `/api/index` | `{ "folderPath": "..." }` — indexa uma pasta |
| `POST` | `/api/search` | `{ "query": "...", "topK": 5 }` — busca (BM25 ou híbrida, conforme configuração) |
| `POST` | `/api/ask` | `{ "question": "...", "topK": 5 }` — pergunta com RAG |

## Usando o app Desktop (WPF)

```powershell
dotnet run --project src/QuestResume.Desktop
```

A aba **Perguntas** permite escolher a pasta de documentos, indexar e conversar com a IA
sobre o conteúdo indexado (mostrando os arquivos-fonte de cada resposta). A aba
**Configurações** permite apontar o modelo `.gguf` (ou configurar o Ollama), a pasta do
índice, o Top-K, o tamanho do contexto, além de habilitar e configurar OCR, transcrição
de áudio (Whisper) e embeddings/busca híbrida.

## Sem modelo configurado?

Indexação e busca por palavras-chave (`search` / aba "Buscar") funcionam sem nenhum modelo
de IA. Ao tentar fazer uma pergunta (`ask`/`chat`/aba "Perguntar") sem um `.gguf` válido
configurado, as três interfaces mostram uma mensagem explicando como configurar o modelo,
em vez de travar ou lançar um erro genérico.

## Testes

```powershell
dotnet test tests/QuestResume.Core.Tests
```

Os testes unitários cobrem o caminho de "recurso não configurado" (degradação graciosa)
para OCR, transcrição e embeddings sem precisar de modelos reais. Há também testes de
integração opcionais, no mesmo projeto, que só rodam se as variáveis de ambiente abaixo
apontarem para arquivos/pastas existentes (caso contrário, passam sem fazer nada):

| Variável | Para testar |
|---|---|
| `QUESTRESUME_TEST_TESSDATA_PATH` | OCR (Tesseract) com uma imagem gerada na hora |
| `QUESTRESUME_TEST_OCR_LANGUAGES` | Idiomas do Tesseract (padrão `eng`) |
| `QUESTRESUME_TEST_WHISPER_MODEL` | Transcrição (Whisper.net) com um `.wav` de teste |
| `QUESTRESUME_TEST_EMBEDDING_MODEL` + `QUESTRESUME_TEST_EMBEDDING_TOKENIZER` | Embeddings (similaridade semântica) |

```powershell
$env:QUESTRESUME_TEST_TESSDATA_PATH = "C:\tessdata"
$env:QUESTRESUME_TEST_WHISPER_MODEL = "C:\Modelos\whisper\ggml-base.bin"
$env:QUESTRESUME_TEST_EMBEDDING_MODEL = "C:\Modelos\embeddings\model.onnx"
$env:QUESTRESUME_TEST_EMBEDDING_TOKENIZER = "C:\Modelos\embeddings\vocab.txt"
dotnet test tests/QuestResume.Core.Tests
```

## CI

O workflow `.github/workflows/ci.yml` (GitHub Actions) compila a solução completa e roda
os testes em `windows-latest` (necessário porque o Desktop usa WPF/`net8.0-windows`) a
cada push/PR para `main`.

## Empacotamento e distribuição

**Desktop** — publica um executável Windows autocontido (não precisa do .NET instalado na
máquina de destino):

```powershell
dotnet publish src/QuestResume.Desktop/QuestResume.Desktop.csproj -c Release -p:PublishProfile=win-x64
```

O executável fica em `src/QuestResume.Desktop/bin/Release/net8.0-windows/publish/win-x64/`.

**API** — há um `Dockerfile` na raiz do repositório para empacotar a API em um container:

```powershell
docker build -t questresume-api .
docker run -p 8080:8080 -v questresume-data:/root/.local/share/QuestResume questresume-api
```

O volume persiste a configuração e o índice (`%LOCALAPPDATA%\QuestResume` no Windows
equivale a `~/.local/share/QuestResume` no Linux). OCR e transcrição de áudio continuam
desabilitados por padrão dentro do container, já que dependem de arquivos (`tessdata`,
modelo Whisper) que precisam ser montados/configurados separadamente.

## Próximos passos

A arquitetura de extração é baseada em `IFileExtractor` + `ExtractorRegistry`
(`src/QuestResume.Core/Extraction`), permitindo plugar novos formatos sem alterar o
restante do sistema (indexação, busca e RAG continuam iguais). Possível extensão futura:

- **Metadados (vídeo, executáveis)**: um novo extrator usando `MetadataExtractor` (ou
  `ExifTool`) preencheria apenas `ExtractedDocument.Metadata` (autor, datas, dimensões,
  codec, etc.), com `Text` vazio — útil para busca por metadados.

Para adicionar um novo formato, basta implementar `IFileExtractor` e registrá-lo em
`ExtractorRegistry.DefaultExtractors()` — `DocumentIndexer`, `SearchService` e
`RagQueryEngine` não precisam de nenhuma alteração.
