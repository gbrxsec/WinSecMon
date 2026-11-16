# WinSecMon

Monitoramento leve para servidores Windows. O serviço verifica se serviços críticos continuam ativos, acompanha sessões RDP, mede saúde básica do sistema e pode detectar alterações em arquivos importantes. Quando algo muda ou cai, envia um alerta por webhook.

> Projeto comunitário. Teste em homologação antes de usar em produção e valide os requisitos de segurança da sua empresa.

## O que ele monitora

- **Serviços do Windows:** alerta quando um serviço para, fica pendente ou volta a funcionar.
- **Saúde do servidor:** CPU, memória e uso de disco acima dos limites configurados.
- **Sessões RDP:** novas conexões e encerramentos, com usuário, hostname e IP do cliente.
- **Integridade de arquivos:** hash SHA-256, cópia do estado anterior e backup quando um arquivo monitorado muda.
- **Notificações:** Microsoft Teams, Discord ou um endpoint HTTP genérico.

O projeto é um Worker Service para **Windows** e requer **.NET 8**.

## Segurança antes de publicar

Nunca coloque webhook, token, senha ou dados reais de clientes no Git. Configure segredos por variável de ambiente ou por um gerenciador de segredos. O arquivo `appsettings.json` deste repositório contém somente valores de exemplo.

Se uma URL de webhook real já apareceu em algum commit, mensagem, anexo ou log, considere-a comprometida: **revogue-a e gere outra antes de tornar o repositório público**.

## Configuração rápida

1. Instale o .NET 8 SDK ou Runtime no servidor Windows.
2. Clone o repositório e edite `checkservice/appsettings.json`.
3. Informe os nomes dos serviços em `ServiceChecker.Services`.
4. Configure o webhook por variável de ambiente. Exemplos no PowerShell:

```powershell
$env:WEBHOOK_PROVIDER = "discord" # teams, discord ou generic
$env:WEBHOOK_URL = "https://seu-endpoint-seguro.example/webhook"
```

Também é possível usar a configuração hierárquica do .NET:

```powershell
$env:Webhook__Provider = "teams"
$env:Webhook__Url = "https://seu-webhook-do-teams"
```

Variáveis de ambiente têm prioridade sobre o arquivo de configuração e são a opção recomendada para produção.

### Exemplos de configuração

```json
{
  "ServiceChecker": {
    "CheckIntervalSeconds": 10,
    "Services": ["Spooler", "W32Time"]
  },
  "Webhook": {
    "Provider": "discord",
    "Url": ""
  },
  "ServerInfo": {
    "ServerName": "SRV-APP-01",
    "CompanyName": "Minha Empresa"
  }
}
```

Para monitorar integridade de arquivos, habilite a seção e use caminhos existentes no servidor:

```json
"FileIntegrityCheck": {
  "Enabled": true,
  "FilesToMonitor": [
    {
      "Name": "Arquivo de configuração",
      "Path": "C:\\Aplicacao\\config.ini",
      "BackupPath": "C:\\Backups\\WinSecMon"
    }
  ]
}
```

## Teams, Discord e WhatsApp

- **Teams:** use um webhook/incoming webhook compatível com o formato MessageCard aceito pelo seu tenant.
- **Discord:** crie um webhook no canal desejado e use `WEBHOOK_PROVIDER=discord`. O WinSecMon envia o campo `content`.
- **WhatsApp:** o WhatsApp não oferece um formato universal de webhook de entrada para este projeto. Use `WEBHOOK_PROVIDER=generic` apontando para um gateway interno ou serviço de automação que receba o JSON do WinSecMon e o encaminhe pela API oficial do WhatsApp Business. O payload genérico contém `title`, `message`, `server`, `company`, `timestamp` e `facts`.

Não envie diretamente credenciais da API do WhatsApp no repositório. O gateway deve armazená-las como segredo.

## Executar em desenvolvimento

No PowerShell, na pasta do projeto:

```powershell
dotnet restore
dotnet run --project .\\checkservice
```

O serviço também pode ser publicado para Windows x64:

```powershell
dotnet publish .\\checkservice -c Release -r win-x64 --self-contained true -o .\\publish
```

## Instalar como Serviço do Windows

Abra um PowerShell como Administrador, publique o projeto e registre o executável:

```powershell
sc.exe create WinSecMon binPath= "C:\\WinSecMon\\publish\\checkservice.exe" start= auto
sc.exe description WinSecMon "Monitoramento de serviços, saúde, RDP e integridade de arquivos"
sc.exe start WinSecMon
```

Para remover:

```powershell
sc.exe stop WinSecMon
sc.exe delete WinSecMon
```

O usuário do serviço precisa ter permissão para consultar os serviços, ler os arquivos monitorados, gravar os backups e acessar a rede para chamar o webhook. Use uma conta com o menor privilégio possível.

## Limitações conhecidas

- O monitoramento de serviços e RDP é específico para Windows.
- A primeira execução considera o estado atual como referência; configure o intervalo de verificação conforme a criticidade do ambiente.
- O alerta é enviado por mudança de estado para evitar uma mensagem a cada ciclo enquanto o serviço permanece parado.
- Webhooks são unidirecionais. Para escalonamento, telefone, SMS ou WhatsApp, use um gateway/serviço de automação com política de retry, autenticação e auditoria.
- Antes de produção, adicione testes automatizados e configure retenção/rotação dos logs.

## Estrutura

```text
checkservice/
├── Worker.cs                 # Loop principal de monitoramento
├── TeamsNotifier.cs          # Payloads para Teams, Discord e generic webhook
├── RdpSessionTracker.cs      # Sessões RDP via WTS API
├── SystemHealthChecker.cs    # CPU, memória e disco
├── FileIntegrityChecker.cs   # Hash e backup de arquivos
├── appsettings.json          # Exemplo sem segredos
└── WinSecMon.csproj
```

## Licença

Adicione aqui a licença escolhida antes da publicação pública. Sem um arquivo de licença, os direitos de uso e redistribuição permanecem limitados pelo direito autoral padrão.
