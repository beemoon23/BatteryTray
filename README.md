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

Identificado como `VID_3634&PID_4155` e anunciado pelo Windows como `GAMING HEADSET B2` (JieLi). Uma base comunitária de compatibilidade também lista esse VID:PID entre os headsets sem consulta de bateria documentada: os canais proprietários do dongle pertencem ao chip de áudio/firmware e não devem receber comandos presumidos. O app mostra o headset como detectado, sem inventar percentual e sem enviar comandos HID não documentados. Se o B2 for pareado em modo Bluetooth e o próprio Windows receber o nível pelo perfil de bateria, poderemos estudar a leitura desse caminho também.

## Compilação

No Windows com .NET 8 SDK instalado:

```powershell
dotnet restore BatteryTray.sln
dotnet build BatteryTray.sln --configuration Release
dotnet publish src/BatteryTray/BatteryTray.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output artifacts
```

O workflow do GitHub Actions está configurado para compilar e publicar o executável como artefato em pushes, pull requests e execução manual. Até o momento, ainda não há uma execução de CI confirmada para esta branch.
