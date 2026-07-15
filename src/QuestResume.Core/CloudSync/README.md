# CloudSync — integrações com nuvem (Google Drive / OneDrive / Dropbox)

Este módulo implementa a infraestrutura para sincronizar arquivos de provedores de
nuvem para uma pasta local, que depois é indexada pelo pipeline normal do QuestResume
(`DocumentIndexer`). Todo o fluxo é offline-first: nada é chamado automaticamente —
a sincronização só ocorre quando o usuário executa `questresume cloud sync ...` (CLI)
ou `POST /api/cloud/{provider}/sync` (API) ou clica em "Sincronizar" (Desktop).

## Como habilitar

1. **Google Drive**: crie um projeto no [Google Cloud Console](https://console.cloud.google.com/),
   ative a "Google Drive API" e crie uma credencial OAuth 2.0 do tipo "Aplicativo para
   computador" (Desktop app) — isso permite o fluxo Authorization Code + PKCE sem client
   secret. Copie o **Client ID** gerado e preencha em `AppOptions.GoogleDriveClientId`
   (via `questresume config set-google-client-id <id>` ou tela de Configurações do
   Desktop). Escopo necessário: `https://www.googleapis.com/auth/drive.readonly`.

2. **OneDrive**: registre um aplicativo no [Azure AD Portal](https://portal.azure.com/)
   (Azure Active Directory > Registros de aplicativo > Novo registro), marque como
   "Aplicativo móvel e para desktop" com o URI de redirecionamento
   `http://localhost:8765/callback` (ou a porta configurada), e habilite o fluxo público
   sem client secret (PKCE). Copie o **Application (client) ID** e preencha em
   `AppOptions.OneDriveClientId`. Escopo necessário: `Files.Read.All`.

3. **Dropbox**: crie um app em https://www.dropbox.com/developers/apps ("Scoped access",
   tipo "App folder" ou "Full Dropbox" conforme a necessidade), com as permissões
   `files.metadata.read` + `files.content.read` habilitadas na aba "Permissions", e
   adicione `http://localhost:8767/callback/` como URI de redirecionamento OAuth2 na
   aba "Settings" (necessário apenas para o fluxo interativo da CLI/Desktop; a API usa
   `/api/cloud/dropbox/callback` do próprio servidor). Copie a **App key** (não a App
   secret — o fluxo é PKCE, sem client secret) e preencha em
   `AppOptions.DropboxClientId` (via `questresume config set-dropbox-client-id <id>`
   ou tela de Configurações do Desktop/Web).

Sem o Client ID configurado, os comandos falham com uma mensagem clara em PT-BR
(ex.: `"Configure GoogleDriveClientId em appsettings antes de usar 'cloud auth google'."`).

## Fluxo de autenticação (Authorization Code + PKCE)

1. `ICloudProvider.AuthenticateAsync` gera um `code_verifier`/`code_challenge` (PKCE),
   monta a URL de autorização do provedor e abre o navegador padrão do usuário
   (`Process.Start` com `UseShellExecute = true`, na CLI) ou retorna a URL para o
   front-end abrir (na API/Desktop).
2. Um `HttpListener` local (CLI/Desktop, via `OAuthLoopbackListener`) ou o endpoint
   `GET /api/cloud/{provider}/callback` (API/Web) recebe o redirecionamento do provedor
   com o `code` de autorização. No caminho da API, o `code_verifier`/`redirect_uri`
   usados para montar a URL de autorização **não são reenviados pelo navegador** (o
   redirecionamento do provedor só inclui `code` e `state`, nunca parâmetros
   arbitrários) — por isso `GET /api/cloud/{provider}/auth-url` guarda esses dois
   valores no servidor associados a um `state` opaco de uso único
   (`CloudOAuthStateStore`, em memória, expira em 10 min), embutido na própria URL de
   autorização devolvida ao front-end; `/callback` recupera-os a partir do `state`
   recebido de volta.
3. O `code` é trocado por um `access_token`/`refresh_token` diretamente com o provedor
   (Google: `https://oauth2.googleapis.com/token`; OneDrive: via MSAL; Dropbox:
   `https://api.dropboxapi.com/oauth2/token`).
4. Os tokens são persistidos localmente em `<IndexPath>/cloud-tokens.json`
   (veja `CloudTokenStore`), nunca logados. Nenhum client secret é armazenado — os
   apps são "públicos" (PKCE), como recomendado para apps desktop/CLI.

## Particularidade do Dropbox: pastas por caminho, não por ID

Ao contrário do Google Drive (`folderId`) e do OneDrive (`itemId`), a API do Dropbox
identifica pastas por **caminho** (ex.: `/Documentos/Contratos`). Por isso, em
`cloud sync dropbox <pasta>` / `POST /api/cloud/dropbox/sync`, o parâmetro
"pasta remota" deve ser um caminho Dropbox (`""` ou `"/"` para a raiz do Dropbox do
usuário) — `DropboxProvider.ListFilesAsync` trata isso de forma transparente.

## Sincronização

`CloudSyncService.SyncFolderAsync(providerName, remoteFolderId, localTargetPath, ct)`
lista os arquivos da pasta remota (`ICloudProvider.ListFilesAsync`) e baixa cada um
(`ICloudProvider.DownloadFileAsync`) para `<localTargetPath>/_cloud_<provider>/`,
preservando o nome original. Depois disso, o chamador (CLI/API) roda a indexação
normal (`DocumentIndexer.IndexFolderAsync`) apontando para essa subpasta, reusando
100% do pipeline de extração/indexação já existente.
