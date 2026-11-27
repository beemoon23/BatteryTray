# BatteryTray

Aplicativo de bandeja para Windows 11 que monitora níveis de bateria de dispositivos compatíveis.

## Recursos atuais

- Windows Battery API.
- Bluetooth LE Battery Service padrão (UUID 0x180F / 0x2A19).
- Identificação de receptores HID específicos sem enviar comandos proprietários não validados.
- Não inventa níveis quando o dispositivo não fornece uma leitura válida.

## Dispositivos investigados

### Ajazz F75 — receptor 2.4 GHz

Identificado no Windows como `VID_3554&PID_FA09` (`2.4G Wireless Receiver`). O BatteryTray já consegue identificar o receptor por HID, mas ainda não consulta a bateria. Existe um protocolo comunitário associado a esse identificador; antes de ativá-lo, precisamos validar a coleção HID e o descritor da interface no Windows para garantir que o relatório seja enviado à interface correta.

### Onikuma B2

Identificado como `VID_3634&PID_4155`. O Windows expõe interfaces de áudio e HID, mas ainda não há protocolo de bateria validado para este modelo. O app mostra o dispositivo como detectado, sem inventar percentual e sem enviar comandos HID não documentados.

## Compilação

No Windows com .NET 8 SDK instalado:

```powershell
dotnet restore BatteryTray.sln
dotnet build BatteryTray.sln --configuration Release
dotnet publish src/BatteryTray/BatteryTray.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output artifacts
```

O workflow do GitHub Actions executa a compilação e publica o executável como artefato quando acionado por push ou pull request.
