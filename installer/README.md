# Установщик Island

Нужен Inno Setup 6: `winget install -e --id JRSoftware.InnoSetup`.

Сборка из корня репозитория:

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

Скрипт делает `dotnet publish` в `installer/payload`, компилирует `installer/Island.iss`
и кладёт в `installer/output` файлы `Island-Setup.exe` и `Island-Setup.exe.sha256`.

Параметры:
- `-Version 1.2.0` — версия (по умолчанию из `src/Island/Island.csproj`);
- `-SkipPublish` — не публиковать, взять готовую папку;
- `-PayloadDir <папка>` — откуда брать файлы программы. Без `-SkipPublish` папка очищается перед публикацией,
  поэтому она должна лежать внутри `installer/`; иначе скрипт останавливается с ошибкой.

## Что делает установщик

- Ставит Island на пользователя, без прав администратора, в `%LOCALAPPDATA%\Programs\Island`.
- Добавляет ярлык в меню «Пуск», по желанию — на рабочий стол.
- Задача «Запускать вместе с Windows» (включена по умолчанию) пишет
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Island` = `"<папка>\Island.exe"`,
  так же, как пункт меню в трее.
- Удаление убирает этот ключ, только если он указывает на эту копию.
  Данные в `%APPDATA%\Island` не удаляются.
- Нужен .NET 9 Desktop Runtime (x64); если его нет, установщик предложит открыть страницу загрузки.

Первая установка через `Island-Setup.exe` переносит программу в `%LOCALAPPDATA%\Programs\Island`
(ярлык и автозапуск начинают указывать туда). Старую папку, из которой Island запускался раньше,
после этого можно удалить: настройки и данные лежат отдельно, в `%APPDATA%\Island`.

## Тихий режим (автообновление)

```
Island-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

Установщик закрывает работающий Island из папки установки, ставит файлы и запускает новый Island.
`/NOLAUNCH` отключает запуск, `/NOICONS` — ярлыки. В тихом режиме автозапуск не трогается:
если пользователь выключил его в трее, обновление его не включит.

AppId `{AC7DE9DF-D68E-4599-85A6-968CC9FA163C}` менять нельзя.
