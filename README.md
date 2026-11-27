# BatteryTray

Aplicativo de bandeja para Windows 11 que monitora níveis de bateria de dispositivos compatíveis.

## Recursos atuais

- Windows Battery API.
- Bluetooth LE Battery Service padrão (UUID 0x180F / 0x2A19).
- Leitura HID experimental e somente de consulta para receptores 2.4 GHz `3554:FA09`, usando o relatório documentado `0x13 / 0x4A`.
- Identificação do Onikuma B2 (`3634:4155`) sem exibir percentual até existir um protocolo confiável.
- Não inventa níveis quando o dispositivo não fornece uma leitura válida.

## Compatibilidade investigada

### Ajazz F75 — receptor 2.4 GHz

O receptor foi identificado no Windows como `VID_3554&PID_FA09`, com coleção HID de uso `FF02:0002`. O BatteryTray envia uma consulta HID de leitura de bateria e só aceita respostas com ID de relatório, comando e checksum válidos.

Limitações conhecidas: alguns dispositivos que usam esse receptor não respondem à consulta. Quando conectado por cabo, o protocolo pode reportar 100% fixo; nesse caso o app deliberadamente não mostra percentual.

### Onikuma B2

Identificado como `VID_3634&PID_4155`. O Windows expõe interfaces de áudio e HID, mas ainda não há protocolo de bateria validado para este modelo. O app mostra o dispositivo como detectado, sem inventar percentual e sem enviar comandos HID não documentados.

## Compilação

No Windows com .NET 8 SDK instalado, execute `build.bat`.

A dependência HidSharp é restaurada automaticamente pelo NuGet.
