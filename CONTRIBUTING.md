# Вклад

Пул-реквесты приветствуются.

```powershell
dotnet restore
dotnet build Klip.sln -c Release
dotnet publish src/Klip/Klip.csproj -c Release -r win-x64 --self-contained true
```

Нужны Windows 10/11 и .NET 9 SDK.

Особенно полезны:

- правки Mica/Acrylic на разных сборках Windows
- обработка редких форматов буфера
- перевод интерфейса
- проверка установщиков EXE/MSI
