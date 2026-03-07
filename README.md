# NektoMe-MITM (Fork)

Fork проекта `Vimer5410/NektoMe-MITM` с расширенным интерактивным режимом и поддержкой `audiochat` через браузерные настройки устройств (без Windows-аудио моста).

## Что добавлено относительно оригинала

- Интерактивное главное меню в `Program.cs`.
- Отдельный режим `AudioChat MITM` для `https://nekto.me/audiochat/`.
- Менеджер `NektoAudioChatManager`:
    - запуск двух браузерных окон,
    - получение и подстановка auth-токенов,
    - раздельная настройка `microphone` и `speaker` для окна A и окна B,
    - применение устройств в браузере через `getUserMedia`/`setSinkId`.
- Профиль аудио-маршрутизации `NektoAudioRouteProfile`:
    - параметры могут переиспользоваться между шагами запуска.
- Улучшения текстового режима:
    - обработка капчи,
    - retry/fallback-логика,
    - расширенный статус клиентов.

## Актуальные режимы

После запуска доступны:

- `1` - Текстовый чат MITM
- `2` - AudioChat MITM (`https://nekto.me/audiochat/`)
- `0` - Выход

## Быстрый старт

### 1. Требования

- Windows
- .NET SDK 9
- Google Chrome

Проверка:

```bash
dotnet --version
```

### 2. Сборка

```bash
dotnet build NektoMe-MITM.sln
```

### 3. Запуск

```bash
dotnet run --project NektoMe-MITM-text/NektoMe-MITM-text.csproj
```

## Работа в режиме AudioChat

1. Выбери `2` в главном меню.
2. Откроются 2 окна Chrome (`A` и `B`) с `audiochat`.
3. После авторизации/подстановки токенов будет предложено выбрать устройства:
     - микрофон для окна `A`,
     - микрофон для окна `B`,
     - вывод звука для окна `A`,
     - вывод звука для окна `B`.
4. В обоих окнах нажми "Начать разговор".

Примечание: маршрутизация в режиме `AudioChat` делается на уровне браузера (пер-окно), а не через Windows audio bridge.

## Важные файлы

- `NektoMe-MITM-text/Program.cs`
- `NektoMe-MITM-text/NektoAudioChatManager.cs`
- `NektoMe-MITM-text/NektoAudioRouteProfile.cs`
- `NektoMe-MITM-text/NektoClient.cs`
- `NektoMe-MITM-text/NektoChatManager.cs`

## Upstream

Оригинальный репозиторий:

`https://github.com/Vimer5410/NektoMe-MITM`
