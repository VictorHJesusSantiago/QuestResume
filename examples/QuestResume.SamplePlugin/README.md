# QuestResume.SamplePlugin

Exemplo mínimo de plugin de extração de terceiros para o QuestResume: adiciona suporte a
arquivos `.log` (texto puro).

## Como compilar e instalar

1. Compile o projeto:

   ```
   dotnet build examples/QuestResume.SamplePlugin/QuestResume.SamplePlugin.csproj -c Release
   ```

2. Copie o `QuestResume.SamplePlugin.dll` gerado (em
   `examples/QuestResume.SamplePlugin/bin/Release/net8.0/`) para a pasta de plugins:

   ```
   %LOCALAPPDATA%\QuestResume\plugins\
   ```

3. Rode `questresume plugins list` (CLI), acesse `GET /api/plugins` (API), ou abra
   Configurações > Plugins (Desktop) para confirmar que o plugin foi carregado.

## Como funciona

- Todo tipo público, concreto e com construtor sem parâmetros que implemente
  `QuestResume.Core.Extraction.IFileExtractor` (ou a interface de marcação
  `IExtractorPlugin`) dentro de uma DLL em `plugins/` é carregado automaticamente no início da
  aplicação, em um `AssemblyLoadContext` isolado por plugin.
- Um plugin com erro de carregamento (DLL corrompida, tipo incompatível, dependência ausente)
  é ignorado com uma mensagem de log — não derruba a aplicação nem impede outros plugins de
  carregar.
- Para escrever seu próprio plugin, copie `LogFileExtractor.cs`, ajuste
  `SupportedExtensions` e a lógica de `ExtractAsync`, referencie apenas `QuestResume.Core`
  (não referencie QuestResume.Api/Cli/Desktop) e compile como acima.
