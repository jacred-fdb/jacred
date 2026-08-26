---
title: Сборка
description: Сборка JacRed из исходников — make, RID, publish
tags:
  - start
  - build
---
# Сборка

Предпочтительный интерфейс — **`make`** (см. `make help`). Скрипты сборки лежат в [`scripts/`](https://github.com/jacred-fdb/jacred/tree/main/scripts).

## Требования для сборки

- **.NET 10.0 SDK** (см. [`JacRed.csproj`](https://github.com/jacred-fdb/jacred/blob/main/JacRed.csproj))
- **Node.js 22+** (сборка Vue SPA в `wwwroot/`)
- **Git** (для генерации версии из тегов)
- **Bash** (для скриптов сборки)
- **Make** (GNU Make / BSD Make)

## Сборка для текущей платформы

```bash
make publish
```

## Сборка для конкретной платформы (RID)

```bash
make publish RID=linux-arm64
make publish RID="linux-x64 osx-arm64"
make publish-linux-arm64
```

## Сборка для всех платформ

```bash
make publish-all
```

## Другие цели

```bash
make web       # только SPA → wwwroot/
make test      # .NET тесты
make docker    # docker build -t jacred .
make clean
```

Поддерживаемые платформы:

- **Linux**: amd64, arm64
- **Windows**: x64
- **macOS**: arm64, amd64

Результат сборки находится в каталоге **`dist/<platform>/`** (self-contained).

## Особенности сборки

- **Linux / Windows:** single-file публикация (один исполняемый файл), сжатие включено
- **macOS (osx-arm64, osx-amd64):** каталог с бинарником и зависимостями (`PublishSingleFile=false`) — обход известного бага .NET с `EnableCompressionInSingleFile` на Apple Silicon
- Self-contained (включает .NET runtime)
- Оптимизация для скорости выполнения
- Версия генерируется автоматически из Git тегов через [`scripts/generate-version.sh`](https://github.com/jacred-fdb/jacred/blob/main/scripts/generate-version.sh)
