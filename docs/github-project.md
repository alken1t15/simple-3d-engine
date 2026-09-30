# GitHub Project setup

Этот файл описывает, как оформить GitHub Project:

<https://github.com/users/alken1t15/projects/1>

Он нужен как рабочая заготовка для создания issues и настройки доски. Карточки ниже основаны на `project_plan.md`, `docs/requirements.md` и `docs/development-process.md`.

## 1. Основные поля действующего Project

| Поле | Тип | Значения |
|---|---|---|
| Status | Single select | Todo, In Progress, Testing, Done |
| Priority | Single select | Critical, High, Medium, Low |
| Area | Single select | Requirements, Architecture, Core, Rendering, Demo, Tests, Docs, Process |
| Milestone | Системное поле, milestone связанной issue | M1 Planning, M2 Technical Spike, M3 MVP, M4 Main Implementation, M5 Final Delivery |

## 2. Labels

| Label | Назначение |
|---|---|
| `area:requirements` | Требования, приоритеты, открытые вопросы |
| `area:architecture` | Архитектура, API, контракты |
| `area:core` | Математика, сцена, mesh, camera, transform |
| `area:rendering` | OpenTK/OpenGL, depth, buffers, shaders, textures |
| `area:demo` | Демонстрационное приложение и сценарии показа |
| `area:tests` | Unit, integration, smoke, regression, performance |
| `area:docs` | README, план, документация, защита |
| `area:process` | Git, PR, review, CI, роли, лицензии |
| `priority:critical` | Без этого проект не соответствует выбранному P0 |
| `priority:high` | Важно после основы или для защиты |
| `priority:medium` | Полезно после MVP |
| `type:planning` | Планирование и фиксация решений |
| `type:spike` | Техническая проверка гипотезы |
| `type:implementation` | Реализация функциональности |
| `type:testing` | Проверки и тестирование |
| `type:documentation` | Документация |

## 3. Issues

### Issue 1. Зафиксировать роли команды и правила работы

**Labels:** `area:process`, `type:planning`, `priority:critical`  
**Milestone:** M1 Planning  
**Project status:** Todo  
**Priority:** Critical  
**Area:** Process

#### Задача

Заполнить фактический состав команды, зоны ответственности и правила review/merge.

#### Checklist

- [ ] Записать участников команды в `docs/team.md`.
- [ ] Назначить владельцев направлений: API/архитектура, Core, Rendering, Demo, Tests/Docs.
- [ ] Назначить интегратора или правило ротации интегратора.
- [ ] Зафиксировать, кто проверяет PR и кто принимает merge.
- [ ] Уточнить доступность участников по неделям.

#### Acceptance criteria

- `docs/team.md` содержит имена, зоны ответственности и доступность.
- Для каждого критического направления есть владелец и резервный проверяющий.
- Правило “автор не принимает свой PR” сохранено.

---

### Issue 2. Подготовить GitHub Project и labels

**Labels:** `area:process`, `type:planning`, `priority:critical`  
**Milestone:** M1 Planning  
**Project status:** Todo  
**Priority:** Critical  
**Area:** Process

#### Задача

Оформить GitHub Project как рабочую доску команды: статусы, приоритеты, areas, labels и начальные карточки.

#### Checklist

- [ ] Создать поля Project: Status, Priority, Area, Milestone.
- [ ] Создать labels из раздела 2 этого файла.
- [ ] Создать issues из раздела 3 этого файла.
- [ ] Добавить issues в Project.
- [ ] Разложить готовые документные задачи в `Done`, будущие задачи в `Todo`.

#### Acceptance criteria

- Project содержит понятные колонки и начальные задачи.
- Каждая issue имеет labels, milestone/status и критерий готовности.
- На доске видно, что реализация движка еще не началась.

---

### Issue 3. Уточнить открытые вопросы по требованиям

**Labels:** `area:requirements`, `type:planning`, `priority:critical`  
**Milestone:** M1 Planning  
**Project status:** Todo  
**Priority:** Critical  
**Area:** Requirements

#### Задача

Собрать вопросы, которые нужно уточнить у команды, не блокируя текущий план.

#### Checklist

- [ ] Проверить раздел `2.2. Открытые вопросы` в `project_plan.md`.
- [ ] Выделить вопросы про формат сдачи, FPS, импорт моделей, сроки и состав команды.
- [ ] Отметить, какие решения уже приняты временно.
- [ ] После консультации обновить `docs/requirements.md`.

#### Acceptance criteria

- Вопросы отделены от утвержденных требований.
- Временные решения явно отделены от утвержденных требований.
- После уточнений есть запись, что именно изменилось.

---

### Issue 4. Проверить сценарии публичного API

**Labels:** `area:architecture`, `type:planning`, `priority:critical`  
**Milestone:** M1 Planning  
**Project status:** Todo  
**Priority:** Critical  
**Area:** Architecture

#### Задача

Проверить, что два сценария из `docs/requirements.md` проходят через проектный API без прямых OpenGL-вызовов со стороны демо.

#### Checklist

- [ ] Пройти сценарий A: минимальное демо.
- [ ] Пройти сценарий B: нагрузочная сцена.
- [ ] Сверить сценарии с `docs/api.md`.
- [ ] Найти отсутствующие типы или операции.
- [ ] Не добавлять API “на будущее”, если он не нужен для P0.

#### Acceptance criteria

- Каждый шаг сценария имеет соответствующий публичный тип или метод.
- Демо не должно напрямую управлять OpenGL.
- Неясные API-решения записаны как вопросы к technical spike.

---

### Issue 5. Выполнить OpenTK technical spike

**Labels:** `area:rendering`, `type:spike`, `priority:critical`  
**Milestone:** M2 Technical Spike  
**Project status:** Todo  
**Priority:** Critical  
**Area:** Rendering

#### Задача

Проверить самый рискованный технический путь: окно OpenTK, матрицы, depth test, текстура и корректное закрытие.

#### Checklist

- [ ] Создать минимальный экспериментальный проект.
- [ ] Открыть окно OpenTK.
- [ ] Отрисовать цветной куб.
- [ ] Проверить depth test на перекрытии граней.
- [ ] Нанести простую контрольную текстуру.
- [ ] Проверить несколько запусков и закрытий без падения.
- [ ] Записать версии .NET/OpenTK/GPU/драйвера.
- [ ] Записать принятую конвенцию матриц.

#### Acceptance criteria

- На целевом ноутбуке виден куб с корректной глубиной и текстурой.
- Приложение закрывается без исключений.
- Результат spike записан в документацию.

---

### Issue 6. Создать C# solution и базовые проекты

**Labels:** `area:architecture`, `type:implementation`, `priority:critical`  
**Milestone:** M3 MVP  
**Project status:** Todo  
**Priority:** Critical  
**Area:** Architecture

#### Задача

Создать минимальную структуру solution для будущего движка без лишнего усложнения.

#### Checklist

- [ ] Создать `Engine3D.sln`.
- [ ] Создать `src/Engine3D.Core`.
- [ ] Создать `src/Engine3D.OpenGL`.
- [ ] Создать `src/Engine3D.Demo`.
- [ ] Создать `tests/Engine3D.Tests`.
- [ ] Добавить базовую сборку в CI.

#### Acceptance criteria

- `dotnet build` проходит.
- Проекты имеют понятные зависимости: Demo зависит от Engine/OpenGL, Tests от Core.
- В solution нет лишних слоев и пустых проектов.

---

### Issue 7. Реализовать базовые модели Core

**Labels:** `area:core`, `type:implementation`, `priority:critical`  
**Milestone:** M3 MVP  
**Project status:** Todo  
**Priority:** Critical  
**Area:** Core

#### Задача

Подготовить управляемые данные сцены: mesh, vertex, transform, scene object, camera.

#### Checklist

- [ ] Определить `Vertex`.
- [ ] Определить `MeshData` с vertices/indices/UV.
- [ ] Определить `Transform`.
- [ ] Определить `SceneObject`.
- [ ] Определить `Scene`.
- [ ] Определить `Camera`.
- [ ] Добавить валидацию индексов mesh.

#### Acceptance criteria

- Core не зависит от OpenTK window и OpenGL calls.
- Неверные индексы mesh отклоняются понятной ошибкой.
- Один mesh может использоваться несколькими объектами.

---

### Issue 8. Добавить процедурные cube и plane

**Labels:** `area:core`, `type:implementation`, `priority:critical`  
**Milestone:** M3 MVP  
**Project status:** Todo  
**Priority:** Critical  
**Area:** Core

#### Задача

Добавить минимальные полигональные примитивы для MVP: куб и плоскость.

#### Checklist

- [ ] Создать фабрику куба.
- [ ] Создать фабрику плоскости.
- [ ] Добавить vertices, indices и UV.
- [ ] Покрыть генерацию простыми unit-тестами.

#### Acceptance criteria

- Куб и плоскость создаются без внешних ассетов.
- Индексы не выходят за границы вершин.
- UV готовы для контрольной текстуры.

---

### Issue 9. Реализовать базовый OpenGL renderer

**Labels:** `area:rendering`, `type:implementation`, `priority:critical`  
**Milestone:** M3 MVP  
**Project status:** Todo  
**Priority:** Critical  
**Area:** Rendering

#### Задача

Реализовать минимальный путь `Scene -> Renderer -> frame`: buffers, shaders, camera transform, depth test.

#### Checklist

- [ ] Создать OpenGL context через OpenTK.
- [ ] Подготовить vertex/index buffers.
- [ ] Добавить минимальные vertex/fragment shaders.
- [ ] Передать model/view/projection matrices.
- [ ] Включить depth test.
- [ ] Освобождать GPU-ресурсы при завершении.

#### Acceptance criteria

- Сцена с кубом и плоскостью отображается.
- Ближний объект перекрывает дальний.
- Повторный запуск не падает из-за ресурсов.

---

### Issue 10. Добавить базовую текстуру и материал

**Labels:** `area:rendering`, `type:implementation`, `priority:critical`  
**Milestone:** M3 MVP  
**Project status:** Todo  
**Priority:** Critical  
**Area:** Rendering

#### Задача

Добавить один базовый материал и растровую текстуру по UV для проверки требования о текстурах.

#### Checklist

- [ ] Создать собственную контрольную текстуру.
- [ ] Добавить загрузку текстуры.
- [ ] Привязать texture coordinates к shader.
- [ ] Проверить ориентацию UV.
- [ ] Записать источник текстуры в `THIRD_PARTY.md` или отметить, что она создана командой.

#### Acceptance criteria

- Текстура видна на кубе.
- Контрольный рисунок не перевернут неожиданно.
- Отсутствие файла текстуры дает понятную ошибку.

---

### Issue 11. Собрать MVP demo

**Labels:** `area:demo`, `type:implementation`, `priority:critical`  
**Milestone:** M3 MVP  
**Project status:** Todo  
**Priority:** Critical  
**Area:** Demo

#### Задача

Собрать демонстрационную программу, которая использует публичный API движка и показывает MVP-сцену.

#### Checklist

- [ ] Создать engine/window через публичный API.
- [ ] Создать scene и camera.
- [ ] Добавить куб и плоскость.
- [ ] Назначить transform нескольким объектам.
- [ ] Добавить вращение или изменение камеры.
- [ ] Закрывать приложение штатным способом.

#### Acceptance criteria

- Демо запускается из README.
- Демо не вызывает OpenGL напрямую.
- В окне видны несколько объектов, камера, depth и текстура.

---

### Issue 12. Добавить unit и integration tests для MVP

**Labels:** `area:tests`, `type:testing`, `priority:critical`  
**Milestone:** M3 MVP  
**Project status:** Todo  
**Priority:** Critical  
**Area:** Tests

#### Задача

Покрыть базовую математику и данные сцены тестами, которые можно запускать в CI без GPU.

#### Checklist

- [ ] Проверить transform/matrix order.
- [ ] Проверить camera projection/view.
- [ ] Проверить mesh indices validation.
- [ ] Проверить cube/plane factories.
- [ ] Проверить повторное использование mesh несколькими объектами.
- [ ] Добавить запуск тестов в CI.

#### Acceptance criteria

- `dotnet test` проходит локально и в CI.
- Тесты не требуют графического окна.
- Рендеринг проверяется отдельным smoke-сценарием.

---

### Issue 13. Подготовить manual smoke checklist

**Labels:** `area:tests`, `type:testing`, `priority:high`  
**Milestone:** M3 MVP  
**Project status:** Todo  
**Priority:** High  
**Area:** Tests

#### Задача

Создать ручной smoke-чек-лист для визуальной проверки, потому что GPU-результат не стоит полностью доверять CI.

#### Checklist

- [ ] Описать запуск MVP demo.
- [ ] Проверить видимость куба и плоскости.
- [ ] Проверить depth.
- [ ] Проверить ориентацию текстуры.
- [ ] Проверить движение камеры или вращение.
- [ ] Проверить повторный запуск.

#### Acceptance criteria

- Smoke-чек-лист можно выполнить за несколько минут.
- Результат проверки фиксируется перед merge крупных изменений.
- CI и ручная проверка не смешиваются.

---

### Issue 14. Подготовить performance scene

**Labels:** `area:tests`, `type:testing`, `priority:critical`  
**Milestone:** M4 Main Implementation  
**Project status:** Todo  
**Priority:** Critical  
**Area:** Tests

#### Задача

Подготовить воспроизводимую сцену примерно из 1000 простых объектов и протокол FPS.

#### Checklist

- [ ] Использовать один shared mesh для множества объектов.
- [ ] Создать около 1000 `SceneObject`.
- [ ] Зафиксировать resolution, VSync, число объектов и примерное число треугольников.
- [ ] Добавить warm-up interval.
- [ ] Измерить FPS/frame time.
- [ ] Записать параметры целевого ноутбука.

#### Acceptance criteria

- Есть воспроизводимый сценарий измерения.
- Результаты не выдают один замер за строгую гарантию для любого железа.
- Производительность измеряется после работающего MVP.

---

### Issue 15. Обновить README для сборки, запуска и защиты

**Labels:** `area:docs`, `type:documentation`, `priority:high`  
**Milestone:** M5 Final Delivery  
**Project status:** Todo  
**Priority:** High  
**Area:** Docs

#### Задача

После появления solution обновить README так, чтобы участники команды могли собрать и запустить проект из чистого клона.

#### Checklist

- [ ] Добавить требования к окружению.
- [ ] Добавить команды `dotnet build`, `dotnet test`, запуск demo.
- [ ] Описать ограничения проекта.
- [ ] Добавить ссылку на smoke checklist.
- [ ] Добавить сценарий демонстрации.

#### Acceptance criteria

- Новый участник может собрать и запустить проект по README.
- README не обещает функции, которых нет.
- Команды проверены на чистом клоне или отдельной папке.

---

### Issue 16. Подготовить финальную сдачу

**Labels:** `area:docs`, `area:tests`, `type:documentation`, `priority:critical`  
**Milestone:** M5 Final Delivery  
**Project status:** Todo  
**Priority:** Critical  
**Area:** Docs

#### Задача

Собрать финальный комплект для защиты: рабочее демо, тесты, замеры, лицензии, вклад команды и список ограничений.

#### Checklist

- [ ] Проверить Definition of Done из `project_plan.md`.
- [ ] Запустить unit/integration tests.
- [ ] Выполнить manual smoke checklist.
- [ ] Выполнить performance scene и записать результат.
- [ ] Проверить `THIRD_PARTY.md`.
- [ ] Проверить вклад участников и PR history.
- [ ] Подготовить короткий сценарий показа.

#### Acceptance criteria

- Проект запускается по README.
- P0-01...P0-09 либо выполнены, либо изменение объема явно записано.
- Команда может объяснить архитектуру, API, тесты и ограничения.

## 4. Как раскладывать карточки по Status

| Status | Что туда класть |
|---|---|
| Todo | Задача согласована, но работа не началась. |
| In Progress | У задачи есть владелец и активная ветка/PR. |
| Testing | PR открыт, нужна проверка людей, CI и предусмотренные задачей ручные проверки. |
| Done | PR смержен, критерии готовности выполнены. |

## 5. Что уже можно отметить Done

После создания issues можно закрыть или перевести в Done только те пункты, которые реально уже сделаны:

- базовый GitHub-репозиторий создан;
- план, требования, архитектура, API и процесс разработки подготовлены;
- MIT license добавлена;
- документационный CI добавлен.

Технические задачи по OpenTK, solution, рендеру, тестам и MVP должны остаться `Todo`, пока код не появится.
