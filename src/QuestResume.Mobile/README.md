# QuestResume.Mobile

Cliente móvel (.NET MAUI) para a QuestResume.Api. Este projeto **não contém nenhuma lógica de
RAG/indexação local** — é um cliente HTTP remoto que fala com uma instância já em execução da
`QuestResume.Api` (login JWT, busca, pergunta/chat com fontes). Os DTOs usados nas chamadas HTTP
estão duplicados em `Models/ApiModels.cs` (não há referência de projeto a `QuestResume.Api` nem a
`QuestResume.Core`), então qualquer mudança de contrato na API precisa ser replicada manualmente
aqui.

## Telas

- **Login** (`Pages/LoginPage.xaml`) — URL do servidor + usuário/senha. Chama
  `POST /api/auth/login` e salva o token JWT via `Microsoft.Maui.Storage.SecureStorage`
  (Keystore no Android / Keychain no iOS) e a URL via `Preferences`.
- **Busca** (`Pages/SearchPage.xaml`) — campo de busca + lista de resultados (`POST /api/search`).
- **Perguntar** (`Pages/AskPage.xaml`) — chat/pergunta com histórico simples e exibição das fontes
  usadas na resposta (`POST /api/ask`).
- **Configurações** (`Pages/SettingsPage.xaml`) — trocar a URL do servidor e logout (limpa o token
  do SecureStorage).

## Limitações conhecidas deste projeto/ambiente

- **Não é publicado em nenhuma loja de aplicativos.** Não há keystore de assinatura Android,
  provisioning profile/certificado iOS, nem pipeline de release configurados — apenas o
  suficiente para compilar (`dotnet build`) e rodar em emulador/dispositivo durante o
  desenvolvimento.
- **O alvo `net8.0-ios` não pode ser compilado nem depurado neste ambiente (Windows).** A
  toolchain do iOS exige um agente de build macOS com Xcode instalado (via "pair to Mac" ou build
  direto num Mac); isso é uma limitação da própria Apple/MAUI, não deste código.
- **`net8.0-android` NÃO compila neste ambiente com o SDK/workloads atualmente instalados** (SDK
  `10.0.301`, banda de workload `10.0.100`: `android 36.1.43/10.0.100`, `ios 26.5.10284/10.0.100`).
  O restore até completa (depois de contornar o erro `NETSDK1202` — "workload out of support" —
  com `-p:CheckEolWorkloads=false` e fixar `-p:MauiVersion=8.0.100`, já que sem essa versão o
  restore falha antes com `NU1015: The following PackageReference item(s) do not have a version
  specified: Microsoft.Maui.Controls`), mas a compilação falha porque o pacote de referência do
  Android instalado não expõe mais os tipos/atributos usados pelos moldes padrão do MAUI para
  `net8.0-android` (`Android.App.Activity`, `MauiAppCompatActivity`, `ActivityAttribute`,
  `ApplicationAttribute`, `LaunchMode`, `ConfigChanges` etc. não são resolvidos). Comando exato
  executado e erro exato obtido:

  ```
  dotnet build src/QuestResume.Mobile -f net8.0-android -p:CheckEolWorkloads=false -p:MauiVersion=8.0.100
  ```

  ```
  Platforms\Android\MainActivity.cs(1,7): error CS0246: The type or namespace name 'Android' could not be found (are you missing a using directive or an assembly reference?)
  Platforms\Android\MainActivity.cs(8,29): error CS0246: The type or namespace name 'MauiAppCompatActivity' could not be found (are you missing a using directive or an assembly reference?)
  Platforms\Android\MainApplication.cs(7,32): error CS0246: The type or namespace name 'MauiApplication' could not be found (are you missing a using directive or an assembly reference?)
  Platforms\Android\MainApplication.cs(6,2): error CS0616: 'Application' is not an attribute class
  Platforms\Android\MainActivity.cs(7,2): error CS0246: The type or namespace name 'ActivityAttribute' could not be found (are you missing a using directive or an assembly reference?)
  Platforms\Android\MainActivity.cs(7,80): error CS0103: The name 'LaunchMode' does not exist in the current context
  Platforms\Android\MainActivity.cs(7,125): error CS0103: The name 'ConfigChanges' does not exist in the current context
  Build FAILED. (18 Error(s))
  ```

  **Como confirmação de que isso é uma limitação de ambiente (workloads instalados são apenas da
  banda .NET 10) e não um bug no código deste projeto**: com o `.csproj` temporariamente ajustado
  para `net10.0-android` (sem nenhuma outra mudança), o mesmo projeto restaura e **compila até o
  código C# das páginas** normalmente (só então apareceram erros não relacionados de API obsoleta
  específicos do MAUI 10, `Page.DisplayAlert` → `DisplayAlertAsync`, confirmando que o pipeline de
  build Android em si funciona — apenas os TFMs `net8.0-*` estão sem suporte binário neste
  ambiente). O `.csproj` final deste projeto permanece em `net8.0-android;net8.0-ios` conforme
  solicitado; para efetivamente compilar aqui seria necessário instalar uma versão de SDK/workload
  da banda .NET 8 (`dotnet workload install android --version 8.x` com um SDK 8.0.x lado a lado),
  o que não foi feito por não fazer parte do escopo pedido (apenas relatar o erro exato).
- O repositório tem uma fonte NuGet privada (`WizCross@Local`) registrada globalmente
  (`dotnet nuget list source`) que estava inacessível neste ambiente durante o desenvolvimento
  deste projeto, causando falha de `dotnet restore`/`dotnet build` por padrão (erro de índice de
  serviço inacessível). Contornado localmente restaurando/buildando com um `NuGet.config`
  temporário contendo apenas a fonte `nuget.org`; isso não é um problema do QuestResume.Mobile em
  si.
- Tratamento de erros de rede é básico (mensagens genéricas); não há retry automático nem cache
  offline dos resultados.
