# BatteryTray

Aplicativo de bandeja para Windows 11 que monitora níveis de bateria expostos por dispositivos compatíveis.

## Código-fonte inicial

O arquivo `archive/BatteryTray-source-fixed2.zip` contém a solução Visual Studio recuperada, incluindo o projeto .NET 8, código-fonte, manifesto e script `build.bat`.

## Limitações

Nem todo periférico USB, receptor 2.4 GHz ou Bluetooth expõe bateria por uma API padrão. O aplicativo não deve inventar percentuais. Integrações específicas para AULA F75 e ONIKUMA B2 ainda precisam ser investigadas e testadas.

## Compilação

Extraia o ZIP e execute `build.bat` em um Windows com .NET 8 SDK instalado.
