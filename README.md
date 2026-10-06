# DanBackup

Backup pessoal para preparar uma máquina para formatação ou troca de sistema (ex.: Windows 10 → 11).
Você escolhe o que salvar; a restauração é sempre manual e item a item.

## O que salva

| Módulo | Conteúdo | Admin? |
|---|---|---|
| Pastas do usuário | Desktop, Documentos, Imagens, Músicas, Vídeos, Downloads + pastas extras | |
| Navegadores | Chrome, Edge, Brave, Vivaldi, Opera, Firefox: favoritos (+ HTML), histórico, preferências, lista de extensões. Firefox inclui senhas | |
| Configurações de jogos | Catálogo (`Catalogs/games.json`) + varredura: My Games, Saved Games, Unreal, Unity (+registro), Steam userdata. Só arquivos de configuração por padrão | |
| Configurações de programas | Git, SSH, VS Code, Windows Terminal, Notepad++, OBS, PuTTY... (`Catalogs/apps.json`) | |
| Outlook e Office | .pst, assinaturas, autocompletar, Normal.dotm, dicionário | |
| Redes Wi-Fi | Perfis com senha (`netsh wlan export`) | Sim |
| Programas instalados | Lista CSV/JSON + `winget export`. Reinstalação só dos pacotes marcados | |
| Drivers | `pnputil /export-driver` | Sim |
| Impressoras | Cada impressora (driver, porta/IP, compartilhamento, padrão); recria a fila e a porta TCP/IP | Restaurar |
| Fontes | Fontes que não vieram com o Windows | |
| Certificados | Repositório Pessoal (.pfx protegido com a senha do backup) | |
| Variáveis de ambiente | Usuário e sistema; restauração só acrescenta, nunca sobrescreve | Restaurar sistema |
| Chaves do registro | As que você adicionar nas Configurações | HKLM |

## Formato do backup

Pastas comuns, navegáveis à mão. Só os itens sensíveis (🔒) são criptografados:

```
DanBackup_PC_2026-10-06_1000/
  manifest.json      ← o que foi salvo e o status de cada item
  checksums.sha256   ← hash SHA-256 de cada arquivo (usado pela verificação)
  backup.log
  modules/<módulo>/<item>/...
```

Arquivos de apps/jogos ficam "espelhados" com tokens (`files/LOCALAPPDATA/VALORANT/...`), então a restauração
funciona mesmo se o nome do usuário ou a letra da unidade mudar.

## Verificação (teste de recuperação)

A aba **Verificar** testa um backup sem alterar nada no computador — use antes de formatar. Ela confere:

1. a senha (contra o verificador do manifesto);
2. cada arquivo contra o `checksums.sha256` gerado no backup (faltando/corrompido);
3. a descriptografia de cada `.dbenc` (o AES-GCM detecta qualquer alteração);
4. o conteúdo de cada item: perfis Wi-Fi são XML válido com senha, `.pfx` abre com a senha (sem instalar), `.reg` é válido,
   a lista do winget é legível, drivers têm `.inf`, etc.

Resultado por item: ✔ recuperável, ⚠ recuperável com ressalvas, ✖ não recuperável. O log fica em `verificacao_*.log` na pasta do backup.
Ao terminar um backup, o app oferece verificar na hora. O `checksums.sha256` também pode ser conferido com `sha256sum -c`.

## Criptografia

Itens sensíveis são gravados como `*.dbenc` (AES-256-GCM; chave derivada da senha por PBKDF2-SHA256, 600 mil iterações, salt por backup):
Wi-Fi, variáveis de ambiente, chaves SSH, credenciais do Git, AWS/Azure/Kube/Docker, `.npmrc`, FileZilla, WinSCP, mRemoteNG,
senhas do Firefox e dados de preenchimento automático dos navegadores. Certificados viram `.pfx` protegido com a mesma senha.

A senha não fica no código nem no backup: o `manifest.json` guarda só o salt e um verificador. Com "Lembrar neste computador",
ela é guardada nas configurações protegida pelo DPAPI do Windows. **Sem a senha, os itens 🔒 não podem ser restaurados.**

## Desenvolvimento

```powershell
dotnet build
dotnet test
dotnet run --project src/DanBackup.App
./publish.ps1        # gera publish/DanBackup.exe (arquivo único)
```

**Releases:** criar e enviar uma tag (`git tag v0.3.0 && git push origin v0.3.0`) dispara o workflow
`.github/workflows/release.yml`, que roda os testes, gera o `DanBackup.exe` e publica uma Release no GitHub.

- `src/DanBackup.Core` — engine, módulos (`Modules/`), catálogos (`Catalogs/*.json`, embutidos)
- `src/DanBackup.App` — interface WPF (MVVM, CommunityToolkit.Mvvm)
- `tests/DanBackup.Tests` — testes (inclui um backup real de módulos leves para uma pasta temporária)

Para adicionar um módulo: implemente `BackupModuleBase` e registre em `ModuleRegistry`.

## Próximos passos

- [x] Releases automáticas pelo GitHub Actions
- [ ] Destino Google Drive nativo (API + OAuth). Hoje já funciona apontando para a pasta do *Google Drive para computador*.
- [ ] Ampliar o catálogo de jogos
- [ ] Compactação opcional (.zip) do backup
