# Aedis — Endurecimento de Binário e Imagem

Guia da postura de hardening contra exploração de memória (buffer overflow) e escalada de
privilégio. **Ponto-chave:** o Aedis é **.NET gerenciado** — a maior parte das proteções clássicas de
binário nativo ou **não se aplica** ao modelo, ou já vem **automaticamente** do runtime e do SO. O que
é controlável vive no **deploy (imagem)** e, opcionalmente, no **publish NativeAOT**.

## Por que NX/DEP, ASLR, canary e RELRO não se "ligam" no Aedis

Esses são conceitos de **binário nativo (C/C++, ELF/PE)**. O Aedis compila para **IL (assemblies
gerenciados)** — um `.dll` gerenciado **não é** um binário nativo, e esses flags não existem nele.

- **Buffer overflow**: C# é **memory-safe** — arrays com verificação de limites, GC, sem aritmética de
  ponteiro. O Aedis tem **zero `unsafe`/`stackalloc`** (verificado), então a classe de stack-smashing
  está **estruturalmente eliminada** na camada gerenciada. Stack canary mitiga um problema que esse
  código não tem.
- **ASLR, DEP/NX, CFG, CET**: são propriedades do **processo**, não do `.dll`. São providas pelo host
  do runtime (`dotnet`/CoreCLR) e pelo SO — automaticamente:
  - **Linux**: os binários do runtime saem com PIE (ASLR), NX, RELRO e stack-protector.
  - **Windows**: ASLR (`/DYNAMICBASE`), DEP (`/NXCOMPAT`), Control Flow Guard (`/guard:cf`) e CET
    shadow stack onde o hardware/SO suportam.

Ou seja: **o processo do app já roda endurecido (ASLR/DEP/NX/CFG/CET) sem nenhuma configuração no
Aedis**, e o buffer overflow já está fora de jogo por ser código gerenciado.

## O que VOCÊ controla — e é onde concentrar o esforço

### 1. Imagem de container (deploy) — o principal vetor de redução de superfície e anti-priv-esc

Ver os artefatos de referência: [`docs/hardening/Dockerfile`](hardening/Dockerfile) e
[`docs/hardening/pod-security.yaml`](hardening/pod-security.yaml).

- **Base mínima** distroless/*chiseled* (`mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`): sem
  shell, sem gerenciador de pacotes, superfície reduzida, **non-root por padrão** (UID `$APP_UID`).
- **`USER` non-root** explícito.
- **Root FS read-only** (`readOnlyRootFilesystem: true`) + `emptyDir` para `/tmp`.
- **Drop ALL capabilities** + **`allowPrivilegeEscalation: false`** (`no-new-privileges`).
- **seccomp** `RuntimeDefault`.
- **`DOTNET_EnableDiagnostics=0`**: desliga o socket de diagnóstico do runtime (remove um canal de
  ataque/inspeção em produção).
- Manter o **runtime atualizado** (CVEs do CoreCLR/ASP.NET são corrigidos via imagem base).

### 2. NativeAOT (opt-in) — só quando se publica binário nativo

Ao publicar com `<PublishAot>true</PublishAot>`, o app vira **binário nativo** — e aí os flags clássicos
passam a existir e valer. O meta-pacote **`Aedis.Build`** traz um props `buildTransitive` que aplica o
hardening **automaticamente quando, e somente quando, `PublishAot=true`** (no-op em managed/JIT):

- **RELRO completo** (`-Wl,-z,relro,-z,now`) no Linux — GOT read-only + binding imediato.
- **Control Flow Guard** e **CET** no Windows (`<ControlFlowGuard>` / `<CetCompat>`).
- PIE (ASLR) e NX já são default do toolchain NativeAOT.

Avalie os trade-offs do AOT (reflexão/serialização limitadas) antes de adotar; é opcional.

## Escalada de privilégio — as duas camadas

| Camada | Defesa | Onde |
|---|---|---|
| **Aplicação** | auth fail-closed, authz deny-by-default, anti-abuso/denylist | hosting secure-by-default do Aedis (já entregue) |
| **Sistema operacional** | non-root, drop caps, `no-new-privileges`, read-only FS, seccomp | imagem/deploy (esta seção) |

## Checklist de deploy

- [ ] Base **distroless/chiseled** atualizada
- [ ] **`USER` non-root** (UID ≥ 10000)
- [ ] **Root FS read-only** + `/tmp` em `emptyDir`/tmpfs
- [ ] **Drop ALL capabilities** + `allowPrivilegeEscalation: false`
- [ ] **seccomp** `RuntimeDefault`
- [ ] `DOTNET_EnableDiagnostics=0` em produção
- [ ] (opcional) **NativeAOT** com o props de hardening do `Aedis.Build`
- [ ] Runtime/imagem base sem CVEs pendentes

> Resumo honesto: no nível das bibliotecas IL **não há flag de binário a ligar** (seria teatro) — a
> proteção de memória já é estrutural e a de processo é herdada do runtime/SO. O hardening acionável é a
> **imagem** (acima) e o **AOT** (opt-in). É isso que as imagens/templates oferecidos pelo Aedis devem
> embutir por padrão.
