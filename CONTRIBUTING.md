# Как помочь проекту

Спасибо за интерес. Клип - локальный менеджер буфера для Windows 10 и 11.
Самые полезные вещи не требуют глубокого погружения в код.

## 1. Повторяемые отчёты об ошибках

Если история не появляется, горячая клавиша не срабатывает или окно выглядит
странно на конкретном оформлении Windows - откройте
[баг](https://github.com/scarrymany/klip/issues/new?template=bug_report.yml).

К отчёту приложите версию, издание Windows (10 или 11, сборка) и способ установки.

## 2. Идеи, которые не ломают локальность

Клип хранит текст только на машине пользователя и не ходит в сеть.
Предложения, которые требуют облака, телеметрии или аккаунта, скорее всего
не подойдут. Локальный поиск, метки, папки и удобство горячей клавиши - да.

## Работа с кодом

Нужны .NET 9 SDK и Windows. WPF на Linux и macOS не собирается.

```bash
git clone https://github.com/scarrymany/klip.git
cd klip
dotnet restore Klip.sln
dotnet build Klip.sln -c Release
dotnet publish src/Klip/Klip.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/app
```

### Что стоит знать про устройство

| Файл | За что отвечает |
|---|---|
| `Services/ClipboardWatcher.cs` | `AddClipboardFormatListener`, чтение текста, пропуск своих копий |
| `Services/ClipStore.cs` | SQLite, лимит 500, дедуп соседних одинаковых записей |
| `Services/HotkeyService.cs` | `RegisterHotKey` Ctrl+Shift+V |
| `Services/TrayService.cs` | Значок и меню «Показать / Выход» |
| `Services/AcrylicHelper.cs` | Mica/Acrylic, тёмный режим, скругление, запасной фон Win10 |
| `Services/StartupService.cs` | HKCU Run |
| `MainWindow.xaml` | Окно, список, фильтры, редактор |

Свои копии в буфер идут через `ClipboardWatcher.CopyText`: сначала ставится флаг,
потом `SetDataObject`. Без флага запись появилась бы в истории второй раз.

### Требования к изменениям

- Пользовательские строки - на русском.
- Комментарии объясняют, почему сделано так, а не что делает строчка.
- Никаких длинных тире в коде, комментариях и документации - только обычный дефис.
- Не тащите сетевые вызовы и сторонние пакеты без необходимости. Сейчас из NuGet
  нужен только `Microsoft.Data.Sqlite`.
- История - это текст. Картинки и файлы в этот релиз не входят.

### Перед пул-реквестом

```bash
dotnet build Klip.sln -c Release
```

Проверьте вручную: копирование из блокнота, клик по карточке, закрепление,
создание заметки, папку, Ctrl+Shift+V при скрытом окне и закрытие в трей.

## Вопросы

Пишите в issues или в Telegram: [@yeet17](https://t.me/yeet17).
