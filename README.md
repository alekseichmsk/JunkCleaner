# JunkCleaner

Локальный очиститель «безопасного» мусора для Windows (.NET 8 + WPF): временные файлы, `Windows\Temp`, корзина и кнопка сброса DNS‑кэша.

## Сборка и запуск

```powershell
cd JunkCleaner
dotnet build -c Release
dotnet run -c Release
```

## Публикация

Portable (требует установленной среды .NET 8 на ПК пользователя):

```powershell
dotnet publish JunkCleaner\JunkCleaner.csproj -c Release -r win-x64 --self-contained false
```

Самодостаточная сборка (больше размер, **не требует** установленного runtime на этом компьютере):

```powershell
dotnet publish JunkCleaner\JunkCleaner.csproj -c Release -r win-x64 --self-contained true
```

Готовый `JunkCleaner.exe` будет в `JunkCleaner\bin\Release\net8.0-windows\win-x64\publish\`.

### Окно «You must install or update .NET…» / «Microsoft.NETCore.App 8.0.0»

Так бывает, если вы запускаете **не самодостаточный** `JunkCleaner.exe`, а на ПК **не установлен .NET 8** (у разработчика может быть только SDK 10 — он собирает проект, но не подставляет runtime на другой машине).

**Вариант A — поставить runtime (проще для себя):** скачайте и установите [**Desktop Runtime .NET 8 (x64)**](https://dotnet.microsoft.com/download/dotnet/8.0) (блок «Run desktop apps» → Windows x64). Для WPF этого достаточно вместе с базовым компонентом установщика.

**Вариант B — раздать один `.exe` без установки .NET:** выполните публикацию с `--self-contained true` (команда выше) и переносите всю папку `publish\` (или только `JunkCleaner.exe`, если включите single-file — см. ниже).

Опционально один файл (дольше старт, проще копирование):

```powershell
dotnet publish JunkCleaner\JunkCleaner.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

## Тесты

```powershell
dotnet test JunkCleaner.sln -c Release
```

## Примечания

- Для части категорий (например `Windows\Temp`) может понадобиться запуск от имени администратора.
- Логи: `%LocalAppData%\JunkCleaner\logs\`.
