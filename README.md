# BatteryTray

Aplicativo de bandeja para Windows 11 que monitora níveis de bateria de dispositivos compatíveis.

## Recursos atuais

- Windows Battery API.
- Bluetooth LE Battery Service padrão (UUID 0x180F / 0x2A19).
- Identificação de receptores HID específicos sem enviar comandos proprietários não validados.
- Não inventa níveis quando o dispositivo não fornece uma leitura válida.

## Dispositivos investigados

### Ajazz F75 — receptor 2.4 GHz

Identificado no Windows como `VID_3554&PID_FA09` (`2.4G Wireless Receiver`). O BatteryTray implementa uma consulta HID de leitura usando o protocolo comunitário de 20 bytes (Report ID `0x13`, comando `0x4A`), mas só envia a consulta quando o descritor confirma a coleção `FF02:0002` e o relatório de saída esperado. A resposta precisa passar pela validação de comando e checksum. O estado conectado por cabo não exibe percentual, pois esse receptor pode devolver 100% fixo nessa condição. A implementação ainda precisa ser compilada e validada no hardware real; alguns firmwares podem não responder.

### Onikuma B2

Identificado como `VID_3634&PID_4155` e anunciado pelo Windows como `GAMING HEADSET B2` (JieLi). Não há consulta de bateria documentada; os canais proprietários pertencem ao chip de áudio/firmware e não devem receber comandos presumidos. O menu da bandeja oferece **Diagnóstico passivo do Onikuma (15 s)**: abre somente a coleção HID `FF02:0002`, registra os relatórios de entrada espontâneos durante 15 segundos e salva um arquivo de texto na Área de Trabalho. Nenhum relatório Output ou Feature é enviado. Durante a captura, use o headset sem fio (fale no microfone, ajuste o volume ou reproduza áudio). Envie o arquivo `BatteryTray-Onikuma-*.txt` para análise. A captura é evidência para investigação; ela não transforma bytes desconhecidos em percentual e não garante que o firmware transmita a bateria espontaneamente.

## Compilação

No Windows com .NET 8 SDK instalado:

```powershell
dotnet restore BatteryTray.sln
dotnet build BatteryTray.sln --configuration Release
dotnet publish src/BatteryTray/BatteryTray.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output artifacts
```

O workflow do GitHub Actions está configurado para compilar e publicar o executável como artefato em pushes, pull requests e execução manual. 
