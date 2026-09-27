param(
    [string]$Gh = "C:\Program Files\GitHub CLI\gh.exe",
    [string]$Repo = "alken1t15/simple-3d-engine",
    [string]$ProjectOwner = "alken1t15",
    [int]$ProjectNumber = 1
)

$ErrorActionPreference = "Stop"

function Invoke-Gh {
    $output = & $Gh @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "gh failed: $($args -join ' ')`n$output"
    }
    return $output
}

function Ensure-Label($name, $color, $description) {
    Invoke-Gh label create $name --repo $Repo --color $color --description $description --force | Out-Null
}

function Ensure-Milestone($title, $description) {
    $existing = Invoke-Gh api "repos/$Repo/milestones?state=all&per_page=100" | ConvertFrom-Json
    if (-not ($existing | Where-Object { $_.title -eq $title })) {
        Invoke-Gh api "repos/$Repo/milestones" -f "title=$title" -f "description=$description" | Out-Null
    }
}

function Ensure-ProjectField($name, $options) {
    $fields = Invoke-Gh project field-list $ProjectNumber --owner $ProjectOwner --format json | ConvertFrom-Json
    if (-not ($fields.fields | Where-Object { $_.name -eq $name })) {
        Invoke-Gh project field-create $ProjectNumber --owner $ProjectOwner --name $name --data-type SINGLE_SELECT --single-select-options ($options -join ",") | Out-Null
    }
}

function Find-IssueUrl($title) {
    $encodedRepo = $Repo
    $issues = Invoke-Gh api "repos/$encodedRepo/issues?state=all&per_page=100" | ConvertFrom-Json
    $found = $issues | Where-Object { $_.title -eq $title } | Select-Object -First 1
    if ($found) { return $found.html_url }
    return $null
}

function Create-Or-Get-Issue($issue) {
    $existingUrl = Find-IssueUrl $issue.Title
    if ($existingUrl) { return $existingUrl }

    $bodyPath = Join-Path $env:TEMP ("simple-3d-engine-issue-" + [guid]::NewGuid().ToString() + ".md")
    Set-Content -LiteralPath $bodyPath -Encoding UTF8 -Value $issue.Body
    try {
        $args = @("issue", "create", "--repo", $Repo, "--title", $issue.Title, "--body-file", $bodyPath, "--milestone", $issue.Milestone)
        foreach ($label in $issue.Labels) {
            $args += @("--label", $label)
        }
        $url = Invoke-Gh @args
        return ($url | Select-Object -Last 1)
    }
    finally {
        Remove-Item -LiteralPath $bodyPath -Force -ErrorAction SilentlyContinue
    }
}

function Get-ProjectItemUrls {
    $itemsJson = Invoke-Gh project item-list $ProjectNumber --owner $ProjectOwner --format json --limit 200 | ConvertFrom-Json
    $urls = New-Object System.Collections.Generic.HashSet[string]
    foreach ($item in $itemsJson.items) {
        if ($item.content.url) { [void]$urls.Add([string]$item.content.url) }
    }
    return $urls
}

function Add-To-Project-And-SetFields($url, $issue, $existingProjectUrls) {
    if (-not $existingProjectUrls.Contains($url)) {
        Invoke-Gh project item-add $ProjectNumber --owner $ProjectOwner --url $url | Out-Null
        [void]$existingProjectUrls.Add($url)
    }
    Invoke-Gh project item-edit $ProjectNumber --owner $ProjectOwner --url $url --field "Status" --value $issue.Status | Out-Null
    Invoke-Gh project item-edit $ProjectNumber --owner $ProjectOwner --url $url --field "Priority" --value $issue.Priority | Out-Null
    Invoke-Gh project item-edit $ProjectNumber --owner $ProjectOwner --url $url --field "Area" --value $issue.Area | Out-Null
}

$labels = @(
    @("area:requirements", "5319E7", "Requirements, priorities, and open questions"),
    @("area:architecture", "1D76DB", "Architecture, API, contracts"),
    @("area:core", "0E8A16", "Core math, scene, mesh, camera, transform"),
    @("area:rendering", "D93F0B", "OpenTK/OpenGL rendering"),
    @("area:demo", "FBCA04", "Demo application and presentation scenarios"),
    @("area:tests", "BFDADC", "Unit, integration, smoke, regression, performance"),
    @("area:docs", "0075CA", "Documentation and delivery notes"),
    @("area:process", "6F42C1", "Git, PR, review, CI, roles, licenses"),
    @("priority:critical", "B60205", "Required for selected P0"),
    @("priority:high", "D93F0B", "Important after the base is ready"),
    @("priority:medium", "FBCA04", "Useful after MVP"),
    @("priority:low", "C2E0C6", "Optional improvement"),
    @("type:planning", "C5DEF5", "Planning and decision tracking"),
    @("type:spike", "F9D0C4", "Technical hypothesis check"),
    @("type:implementation", "0E8A16", "Implementation task"),
    @("type:testing", "BFD4F2", "Testing and verification"),
    @("type:documentation", "0075CA", "Documentation task")
)

foreach ($label in $labels) {
    Ensure-Label $label[0] $label[1] $label[2]
}

$milestones = @(
    @("M1 Planning", "Requirements, architecture, API and team process are ready for discussion."),
    @("M2 Technical Spike", "OpenTK, matrix convention, depth, texture and shutdown path are verified."),
    @("M3 MVP", "Minimal public API demo works with Core, renderer, texture and tests."),
    @("M4 Main Implementation", "Performance scene and selected post-MVP functionality are checked."),
    @("M5 Final Delivery", "Final checks, documentation, licenses, contribution history and demo are ready.")
)

foreach ($milestone in $milestones) {
    Ensure-Milestone $milestone[0] $milestone[1]
}

Ensure-ProjectField "Priority" @("Critical", "High", "Medium", "Low")
Ensure-ProjectField "Area" @("Requirements", "Architecture", "Core", "Rendering", "Demo", "Tests", "Docs", "Process")

$issues = @(
    @{
        Title = "[Process] Зафиксировать роли команды и правила работы"
        Labels = @("area:process", "type:planning", "priority:critical")
        Milestone = "M1 Planning"
        Status = "Todo"
        Priority = "Critical"
        Area = "Process"
        Body = @"
## Задача
Заполнить фактический состав команды, зоны ответственности и правила review/merge. Это закрывает организационную часть требований преподавателя.

## Checklist
- [ ] Записать участников команды в `docs/team.md`.
- [ ] Назначить владельцев направлений: API/архитектура, Core, Rendering, Demo, Tests/Docs.
- [ ] Назначить интегратора или правило ротации интегратора.
- [ ] Зафиксировать, кто проверяет PR и кто принимает merge.
- [ ] Уточнить доступность участников по неделям.

## Acceptance criteria
- `docs/team.md` содержит имена, зоны ответственности и доступность.
- Для каждого критического направления есть владелец и резервный проверяющий.
- Правило “автор не принимает свой PR” сохранено.
"@
    },
    @{
        Title = "[Process] Подготовить GitHub Project, labels и milestones"
        Labels = @("area:process", "type:planning", "priority:critical")
        Milestone = "M1 Planning"
        Status = "Done"
        Priority = "Critical"
        Area = "Process"
        Body = @"
## Задача
Оформить GitHub Project как рабочую доску команды: статусы, приоритеты, areas, labels, milestones и начальные карточки.

## Checklist
- [x] Создать labels по зонам, типам и приоритетам.
- [x] Создать milestones M1...M5.
- [x] Создать Project fields `Priority` и `Area`.
- [x] Создать начальные issues.
- [x] Добавить issues в Project.

## Acceptance criteria
- Project содержит понятные задачи и поля.
- Каждая issue имеет labels, milestone/status и критерий готовности.
- На доске видно, что реализация движка еще не началась.
"@
    },
    @{
        Title = "[Requirements] Уточнить открытые вопросы по требованиям"
        Labels = @("area:requirements", "type:planning", "priority:critical")
        Milestone = "M1 Planning"
        Status = "In Progress"
        Priority = "Critical"
        Area = "Requirements"
        Body = @"
## Задача
Собрать вопросы, которые нужно уточнить у преподавателя или команды, не блокируя текущий план.

## Checklist
- [x] Проверить раздел `2.3` в `project_plan.md`.
- [ ] Уточнить формат сдачи, FPS, импорт моделей, сроки и состав команды.
- [ ] Отметить, какие решения уже приняты временно.
- [ ] После консультации обновить `docs/requirements.md`.

## Acceptance criteria
- Вопросы отделены от утвержденных требований.
- Временные решения не выданы за слова преподавателя.
- После уточнений есть запись, что именно изменилось.
"@
    },
    @{
        Title = "[Architecture] Проверить сценарии публичного API"
        Labels = @("area:architecture", "type:planning", "priority:critical")
        Milestone = "M1 Planning"
        Status = "Done"
        Priority = "Critical"
        Area = "Architecture"
        Body = @"
## Задача
Проверить, что два сценария из `docs/requirements.md` проходят через проектный API без прямых OpenGL-вызовов со стороны демо.

## Checklist
- [x] Пройти сценарий A: минимальное демо.
- [x] Пройти сценарий B: нагрузочная сцена.
- [x] Сверить сценарии с `docs/api.md`.
- [x] Найти отсутствующие типы или операции.
- [x] Не добавлять API “на будущее”, если он не нужен для P0.

## Acceptance criteria
- Каждый шаг сценария имеет соответствующий публичный тип или метод.
- Демо не должно напрямую управлять OpenGL.
- Неясные API-решения записаны как вопросы к technical spike.
"@
    },
    @{
        Title = "[Rendering] Выполнить OpenTK technical spike"
        Labels = @("area:rendering", "type:spike", "priority:critical")
        Milestone = "M2 Technical Spike"
        Status = "Todo"
        Priority = "Critical"
        Area = "Rendering"
        Body = @"
## Задача
Проверить самый рискованный технический путь: окно OpenTK, матрицы, depth test, текстура и корректное закрытие.

## Checklist
- [ ] Создать минимальный экспериментальный проект.
- [ ] Открыть окно OpenTK.
- [ ] Отрисовать цветной куб.
- [ ] Проверить depth test на перекрытии граней.
- [ ] Нанести простую контрольную текстуру.
- [ ] Проверить несколько запусков и закрытий без падения.
- [ ] Записать версии .NET/OpenTK/GPU/драйвера.
- [ ] Записать принятую конвенцию матриц.

## Acceptance criteria
- На целевом ноутбуке виден куб с корректной глубиной и текстурой.
- Приложение закрывается без исключений.
- Результат spike записан в документацию.
"@
    },
    @{
        Title = "[Architecture] Создать C# solution и базовые проекты"
        Labels = @("area:architecture", "type:implementation", "priority:critical")
        Milestone = "M3 MVP"
        Status = "Todo"
        Priority = "Critical"
        Area = "Architecture"
        Body = @"
## Задача
Создать минимальную структуру solution для будущего движка без лишнего усложнения.

## Checklist
- [ ] Создать `Simple3DEngine.sln`.
- [ ] Создать `src/Simple3DEngine.Core`.
- [ ] Создать `src/Simple3DEngine.OpenGL`.
- [ ] Создать `src/Simple3DEngine.Demo`.
- [ ] Создать `tests/Simple3DEngine.Tests`.
- [ ] Добавить базовую сборку в CI.

## Acceptance criteria
- `dotnet build` проходит.
- Проекты имеют понятные зависимости.
- В solution нет лишних слоев и пустых проектов.
"@
    },
    @{
        Title = "[Core] Реализовать базовые модели Core"
        Labels = @("area:core", "type:implementation", "priority:critical")
        Milestone = "M3 MVP"
        Status = "Todo"
        Priority = "Critical"
        Area = "Core"
        Body = @"
## Задача
Подготовить управляемые данные сцены: mesh, vertex, transform, scene object, camera.

## Checklist
- [ ] Определить `Vertex`.
- [ ] Определить `MeshData` с vertices/indices/UV.
- [ ] Определить `Transform`.
- [ ] Определить `SceneObject`.
- [ ] Определить `Scene`.
- [ ] Определить `Camera`.
- [ ] Добавить валидацию индексов mesh.

## Acceptance criteria
- Core не зависит от OpenTK window и OpenGL calls.
- Неверные индексы mesh отклоняются понятной ошибкой.
- Один mesh может использоваться несколькими объектами.
"@
    },
    @{
        Title = "[Core] Добавить процедурные cube и plane"
        Labels = @("area:core", "type:implementation", "priority:critical")
        Milestone = "M3 MVP"
        Status = "Todo"
        Priority = "Critical"
        Area = "Core"
        Body = @"
## Задача
Добавить минимальные полигональные примитивы для MVP: куб и плоскость.

## Checklist
- [ ] Создать фабрику куба.
- [ ] Создать фабрику плоскости.
- [ ] Добавить vertices, indices и UV.
- [ ] Покрыть генерацию простыми unit-тестами.

## Acceptance criteria
- Куб и плоскость создаются без внешних ассетов.
- Индексы не выходят за границы вершин.
- UV готовы для контрольной текстуры.
"@
    },
    @{
        Title = "[Rendering] Реализовать базовый OpenGL renderer"
        Labels = @("area:rendering", "type:implementation", "priority:critical")
        Milestone = "M3 MVP"
        Status = "Todo"
        Priority = "Critical"
        Area = "Rendering"
        Body = @"
## Задача
Реализовать минимальный путь `Scene -> Renderer -> frame`: buffers, shaders, camera transform, depth test.

## Checklist
- [ ] Создать OpenGL context через OpenTK.
- [ ] Подготовить vertex/index buffers.
- [ ] Добавить минимальные vertex/fragment shaders.
- [ ] Передать model/view/projection matrices.
- [ ] Включить depth test.
- [ ] Освобождать GPU-ресурсы при завершении.

## Acceptance criteria
- Сцена с кубом и плоскостью отображается.
- Ближний объект перекрывает дальний.
- Повторный запуск не падает из-за ресурсов.
"@
    },
    @{
        Title = "[Rendering] Добавить базовую текстуру и материал"
        Labels = @("area:rendering", "type:implementation", "priority:critical")
        Milestone = "M3 MVP"
        Status = "Todo"
        Priority = "Critical"
        Area = "Rendering"
        Body = @"
## Задача
Добавить один базовый материал и растровую текстуру по UV для проверки требования о текстурах.

## Checklist
- [ ] Создать собственную контрольную текстуру.
- [ ] Добавить загрузку текстуры.
- [ ] Привязать texture coordinates к shader.
- [ ] Проверить ориентацию UV.
- [ ] Записать источник текстуры в `THIRD_PARTY.md` или отметить, что она создана командой.

## Acceptance criteria
- Текстура видна на кубе.
- Контрольный рисунок не перевернут неожиданно.
- Отсутствие файла текстуры дает понятную ошибку.
"@
    },
    @{
        Title = "[Demo] Собрать MVP demo"
        Labels = @("area:demo", "type:implementation", "priority:critical")
        Milestone = "M3 MVP"
        Status = "Todo"
        Priority = "Critical"
        Area = "Demo"
        Body = @"
## Задача
Собрать демонстрационную программу, которая использует публичный API движка и показывает MVP-сцену.

## Checklist
- [ ] Создать engine/window через публичный API.
- [ ] Создать scene и camera.
- [ ] Добавить куб и плоскость.
- [ ] Назначить transform нескольким объектам.
- [ ] Добавить вращение или изменение камеры.
- [ ] Закрывать приложение штатным способом.

## Acceptance criteria
- Демо запускается из README.
- Демо не вызывает OpenGL напрямую.
- В окне видны несколько объектов, камера, depth и текстура.
"@
    },
    @{
        Title = "[Tests] Добавить unit и integration tests для MVP"
        Labels = @("area:tests", "type:testing", "priority:critical")
        Milestone = "M3 MVP"
        Status = "Todo"
        Priority = "Critical"
        Area = "Tests"
        Body = @"
## Задача
Покрыть базовую математику и данные сцены тестами, которые можно запускать в CI без GPU.

## Checklist
- [ ] Проверить transform/matrix order.
- [ ] Проверить camera projection/view.
- [ ] Проверить mesh indices validation.
- [ ] Проверить cube/plane factories.
- [ ] Проверить повторное использование mesh несколькими объектами.
- [ ] Добавить запуск тестов в CI.

## Acceptance criteria
- `dotnet test` проходит локально и в CI.
- Тесты не требуют графического окна.
- Рендеринг проверяется отдельным smoke-сценарием.
"@
    },
    @{
        Title = "[Tests] Подготовить manual smoke checklist"
        Labels = @("area:tests", "type:testing", "priority:high")
        Milestone = "M3 MVP"
        Status = "Todo"
        Priority = "High"
        Area = "Tests"
        Body = @"
## Задача
Создать ручной smoke-чек-лист для визуальной проверки, потому что GPU-результат не стоит полностью доверять CI.

## Checklist
- [ ] Описать запуск MVP demo.
- [ ] Проверить видимость куба и плоскости.
- [ ] Проверить depth.
- [ ] Проверить ориентацию текстуры.
- [ ] Проверить движение камеры или вращение.
- [ ] Проверить повторный запуск.

## Acceptance criteria
- Smoke-чек-лист можно выполнить за несколько минут.
- Результат проверки фиксируется перед merge крупных изменений.
- CI и ручная проверка не смешиваются.
"@
    },
    @{
        Title = "[Tests] Подготовить performance scene"
        Labels = @("area:tests", "type:testing", "priority:critical")
        Milestone = "M4 Main Implementation"
        Status = "Todo"
        Priority = "Critical"
        Area = "Tests"
        Body = @"
## Задача
Подготовить воспроизводимую сцену примерно из 1000 простых объектов и протокол FPS.

## Checklist
- [ ] Использовать один shared mesh для множества объектов.
- [ ] Создать около 1000 `SceneObject`.
- [ ] Зафиксировать resolution, VSync, число объектов и примерное число треугольников.
- [ ] Добавить warm-up interval.
- [ ] Измерить FPS/frame time.
- [ ] Записать параметры целевого ноутбука.

## Acceptance criteria
- Есть воспроизводимый сценарий измерения.
- Результаты не выдают приблизительный ориентир преподавателя за строгую гарантию.
- Производительность измеряется после работающего MVP.
"@
    },
    @{
        Title = "[Docs] Обновить README для сборки, запуска и защиты"
        Labels = @("area:docs", "type:documentation", "priority:high")
        Milestone = "M5 Final Delivery"
        Status = "Todo"
        Priority = "High"
        Area = "Docs"
        Body = @"
## Задача
После появления solution обновить README так, чтобы преподаватель и участники команды могли собрать и запустить проект из чистого клона.

## Checklist
- [ ] Добавить требования к окружению.
- [ ] Добавить команды `dotnet build`, `dotnet test`, запуск demo.
- [ ] Описать ограничения проекта.
- [ ] Добавить ссылку на smoke checklist.
- [ ] Добавить сценарий демонстрации.

## Acceptance criteria
- Новый участник может собрать и запустить проект по README.
- README не обещает функции, которых нет.
- Команды проверены на чистом клоне или отдельной папке.
"@
    },
    @{
        Title = "[Delivery] Подготовить финальную сдачу"
        Labels = @("area:docs", "area:tests", "type:documentation", "priority:critical")
        Milestone = "M5 Final Delivery"
        Status = "Todo"
        Priority = "Critical"
        Area = "Docs"
        Body = @"
## Задача
Собрать финальный комплект для защиты: рабочее демо, тесты, замеры, лицензии, вклад команды и список ограничений.

## Checklist
- [ ] Проверить Definition of Done из `project_plan.md`.
- [ ] Запустить unit/integration tests.
- [ ] Выполнить manual smoke checklist.
- [ ] Выполнить performance scene и записать результат.
- [ ] Проверить `THIRD_PARTY.md`.
- [ ] Проверить вклад участников и PR history.
- [ ] Подготовить короткий сценарий показа.

## Acceptance criteria
- Проект запускается по README.
- P0-01...P0-09 либо выполнены, либо изменение объема явно записано.
- Команда может объяснить архитектуру, API, тесты и ограничения.
"@
    }
)

$existingProjectUrls = Get-ProjectItemUrls

foreach ($issue in $issues) {
    Write-Host "Ensuring issue: $($issue.Title)"
    $url = Create-Or-Get-Issue $issue
    Add-To-Project-And-SetFields $url $issue $existingProjectUrls
}

Write-Host "GitHub Project setup completed."

