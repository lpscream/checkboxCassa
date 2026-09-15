# CheckboxCassa — декомпилированный исходный код

Репозиторий содержит результат декомпиляции файла `CheckboxCassa.dll` —
драйвера кассового аппарата (РРО) для системы автоматизации торговли
**Onix** (POS-система «Мой Магазин» / Onix-JCV), реализующего интеграцию
с облачным сервисом фискализации **Checkbox** (checkbox.ua) через его
локальный/облачный HTTP API `api/v1`.

## 1. Метаданные сборки

| Параметр | Значение |
|---|---|
| Имя сборки | `CheckboxCassa` |
| Компания | Onix-JCV |
| Продукт | Onix Driver for CheckboxCassa |
| Copyright | © 2022 |
| Версия | 1.0.0.0 |
| Target Framework | .NET Framework 4.0 (`net40`) |
| Платформа | x86 |
| GUID сборки | `e1e0c30f-7e70-4ac9-9ddd-93b178978f2a` |
| Зависимости | `Onix.Devices` (внешняя сборка-фреймворк драйверов Onix, **не входит** в данную DLL), `System.Web.Extensions` (для `JavaScriptSerializer`) |

Инструмент декомпиляции: `ilspycmd` (ICSharpCode.Decompiler) — генерирует
код, приближенный к исходному C#, но не идентичный побайтово оригиналу
(имена локальных переменных, часть комментариев вида `//IL_xxxx` —
служебные пометки декомпилятора об особенностях восстановления IL,
не влияющие на семантику).

## 2. Структура проекта

```
src/
├── CheckboxCassa.csproj              # восстановленный проектный файл
├── Onix.Devices.Drivers/
│   └── CheckboxCassa.cs              # единственный класс сборки — драйвер кассы
└── Properties/
    └── AssemblyInfo.cs               # метаданные сборки
```

Сборка `CheckboxCassa.dll` содержит **ровно один публичный класс** —
`Onix.Devices.Drivers.CheckboxCassa`. Все базовые классы и интерфейсы
(`CashRegisterBase`, `DeviceBase`, `CashRegisterCapabilities`,
`ICashRegisterBaseAddNew`, `ICashRegisterBaseNewPayment`, `Payment`,
`PaymentInfo`, `ReceiptMode`, `ReceiptSaleItem`, `CashRegisterStatus`,
`DeviceCommException` и др.) определены во внешней сборке `Onix.Devices`
и физически в `CheckboxCassa.dll` отсутствуют — это framework для
подключаемых драйверов торгового оборудования, а данная DLL — плагин
(driver plug-in) к нему.

## 3. Назначение класса `CheckboxCassa`

Класс наследует `CashRegisterBase` (базовый класс драйвера кассового
аппарата в Onix) и реализует два дополнительных интерфейса:
`ICashRegisterBaseAddNew` и `ICashRegisterBaseNewPayment` (расширенные
методы регистрации продаж и оплат «нового» образца).

Драйвер не работает с физическим ФРС напрямую, а обращается по HTTP
к локально запущенному ПРРО-агенту Checkbox (`http://<host>/api/v1/...`,
по умолчанию `127.0.0.1:9200`), транслируя операции POS-системы
(открытие смены, чек, продажа товара, оплата, закрытие чека, X/Z-отчёт
и т.д.) в вызовы REST API Checkbox и обратно.

## 4. Внутренние типы

### `EN_RoundMode` (public enum)
Режим округления суммы оплаты наличными:
- `None` — без округления;
- `ROUND_10` — округление до 10 копеек;
- `ROUND_50` — округление до 50 копеек (классическое «монетное» округление
  по диапазонам остатка: 1–24 → вниз, 25–74 → до 0.50, 75–99 → вверх).

### `CheckboxCassaCapabilities` (private sealed class)
Реализация `CashRegisterCapabilities` — декларирует ограничения/возможности
кассы для ядра Onix:
- `MaxAmount`, `MaxPrice`, `MaxTotal` = 999999.99;
- `MaxQuantity` = 99999.999;
- `MaxReceiptSales` = 250 позиций в чеке;
- `MaxTaxGroup` = 5;
- `NameWidth` = 100 (ширина наименования товара), `MaxNameLength` = 200;
- `Options` = битовая маска `(CashRegisterOptions)5439`;
- `SupportedPayments` = `{ Cash(0), Cash(1), Cashless(2) }` (наличные и
  безналичная/карта);
- `IsDisplaySupport` = false (покупательский дисплей не поддерживается);
- `TextWidth` = ширина строки для «свободного текста» в режиме подделки
  чека (по умолчанию 29 символов).

## 5. Поля класса `CheckboxCassa`

| Поле | Назначение |
|---|---|
| `IPaddress` | адрес:порт локального агента Checkbox, читается из параметра устройства `host`, по умолчанию `127.0.0.1:9200` |
| `roundCashMode` | режим округления, параметр устройства `round` |
| `mode` | текущий режим чека (продажа/возврат — `ReceiptMode`) |
| `total` | накопленная сумма чека |
| `receipt_payment_type`, `receipt_payment_amount` | тип и сумма оплаты (старый интерфейс оплаты) |
| `paymentInfo` | детальная информация об оплате, включая данные банковского терминала (новый интерфейс `ICashRegisterBaseNewPayment`) |
| `receipt_id` | id открытого чека в Checkbox |
| `header`, `footer` | накопленный текст шапки/подвала чека |
| `fakemode_text` | буфер «фейкового» текста чека в режиме `FakeMode` |
| `_text_width` | ширина текстовой строки (29 симв.) для псевдопечати в `FakeMode` |
| `cap_getted` | флаг однократной инициализации параметров |
| `taxes`, `payments`, `payments Labels` | статические справочники кодов групп налогов, типов оплат (`CASH`/`CASHLESS`) и их подписей на украинском («Готівка», «КАРТКА») |

`FakeMode` — свойство, определяемое битом `0x10000` в `Status` устройства:
режим, при котором реальные фискальные операции не выполняются, а вместо
этого формируется текстовое представление чека (`fakemode_text`), которое
отправляется в Checkbox как «документ» (`receipt/document`) — судя по всему,
используется для нефискальных копий/тестового режима.

## 6. Методы — жизненный цикл устройства

- **`InternalOpen()`** — читает параметры устройства (`get_capabilit`),
  проверяет/открывает кассовую смену (`CheckSmena`), выставляет статус
  «готов».
- **`CheckSmena()`** — `GET /api/v1/shift`; если смена не открыта —
  `POST /api/v1/shift/open`.
- **`InternalClose()`** — закрывает устройство (если оно не в статусах
  6/7 — вероятно "error"/"disconnected").
- **`InternalResync()`** — установка/снятие бита состояния "resync" без
  дополнительной логики (заглушка синхронизации).
- **`GetCapabilities()`** — возвращает объект `CheckboxCassaCapabilities`
  (лениво создаётся один раз).

## 7. Методы — работа с чеком

- **`InternalOpenReceipt(ReceiptMode mode)`** — сбрасывает накопители чека;
  если не `FakeMode` — `POST /api/v1/receipt` с телом
  `{"departament": "1"}`, получает `id` открытого чека из JSON-ответа.
- **`InternalRegisterSaleNew(ReceiptSaleItem article)`** (интерфейс
  `ICashRegisterBaseAddNew`) — регистрация позиции товара:
  - считает количество (×1000) и цену (×100) в минимальных единицах,
    накапливает `total`;
  - если не `FakeMode` — `POST /api/v1/receipt/good` с JSON, включающим
    код, наименование (экранированное), цену, количество, налоговые
    группы (`taxes`), штрихкод, код УКТЗЕД, акцизные марки, признак
    возврата (`is_return`);
  - в `FakeMode` — формирует и печатает строку свободного текста через
    `freeText`, используя код буквы латиницы/кириллицы для обозначения
    группы налога (`'А' + номер группы`).
  - `InternalRegisterSale(...)` (старый интерфейс) — не реализован,
    всегда возвращает `amount = 0`.
- **`InternalPayment` / `InternalPaymentNew`** — фиксируют тип/сумму
  оплаты (старый и новый API), вычисляют сдачу (`change`), сбрасывают
  соответствующие биты статуса. Реальный HTTP-запрос не выполняется здесь
  — данные оплаты отправляются позже, при закрытии чека.
- **`GetJSONFromPaymentInfo()`** — формирует JSON блока `payments` для
  закрытия чека. Если оплата картой (`Payment == Cashless`,
  `TransactionInfo != null`) — добавляет реквизиты банковской операции
  (эмитент, банк, терминал, маска карты, платёжная система, код
  авторизации, RRN, тип операции ПРОДАЖ/ПОВЕРНЕННЯ/АНУЛЯЦIЯ,
  подтверждение подписи). Сумма проходит через `do_round`.
- **`do_round(decimal, Payment, ref string)`** — применяет
  `roundCashMode` только к наличным оплатам (`Payment < Cashless`, оба
  варианта Cash из `SupportedPayments`), возвращает округлённую сумму и
  строку `"rounding_mode"` для JSON.
- **`InternalCloseReceipt()`** — закрытие чека:
  - не `FakeMode`: если `paymentInfo` не задан — строит `payments` из
    старых полей `receipt_payment_type/amount`, иначе использует
    `GetJSONFromPaymentInfo()`; `POST /api/v1/receipt/done`;
  - `FakeMode`: отправляет накопленный `fakemode_text` как
    `POST /api/v1/receipt/document`;
  - сбрасывает все накопители чека.
- **`InternalAbortReceipt()`** — `DELETE /api/v1/receipt` (отмена чека),
  сброс накопителей.
- **`InternalFreeText(text)` / `freeText(text, isFooter)`** — добавление
  произвольного текста в чек (по умолчанию в подвал). Не `FakeMode`:
  `PATCH /api/v1/receipt` с телом `{"footer"/"header": "..."}` (не более
  1024 символов, с обрезкой). `FakeMode`: копится в `fakemode_text`.
  `CanPrintFreeTextInReceipt()` переопределён и возвращает `false`
  (свободный текст внутри позиций не поддерживается).
- **`InternalOpenDrawer()`** — пустая реализация (открытие денежного
  ящика не поддерживается/не требуется — управляется самим фискальным
  устройством/ПРРО).
- **`InternalDailyReport()`** — X-отчёт: `POST /api/v1/shift/xreport`.
- **`InternalFiscalClosure()`** — закрытие смены (Z-отчёт):
  `POST /api/v1/shift/close`.
- **`InternalServiceAmount(decimal amount)`** — служебное
  внесение/выплата наличных: `POST /api/v1/receipt/service` с телом
  `{"payment": {"type": "CASH", "value": amount*100}}`.

## 8. HTTP-инфраструктура

- **`GetUri(suffix)`** — строит `http://{IPaddress}/api/v1/{suffix}`.
- **`getHTTPresponse` / `getHTTPgetresponse` / `getHTTPpostresponse` /
  `getHTTPpatchresponse`** — обёртки над `HttpWebRequest` (GET/POST/PATCH/
  DELETE), с заголовками `User-Agent: Onix.Devices`,
  `X-Client-Name: MoyMagazin`, `X-Client-Version: v1.01`,
  `Accept: application/json`, `KeepAlive: false`,
  `Expect100Continue: false`. Тело запроса — UTF-8 JSON без BOM.
  Ошибочные HTTP-статусы не бросают исключение сразу — ответ всё равно
  читается (через перехват `WebException`), а `DeviceCommException`
  выбрасывается на уровне вызывающих методов, если код ответа не `200 OK`.
- **`ToggleAllowUnsafeHeaderParsing(bool)`** — через reflection
  включает `useUnsafeHeaderParsing` в `System.Net` (обход строгого
  парсинга заголовков HTTP, нужен для совместимости со старым/нестрогим
  сервером Checkbox-агента на .NET Framework 4.0).
- **`escapeJson(string)`** — примитивное ручное экранирование строки для
  вставки в JSON (`\`, `\n`, `"`, `\t`) — сериализация JSON в коде по
  большей части делается вручную через `string.Format`, а не через
  `JavaScriptSerializer` (последний используется только для разбора
  ответа при открытии чека).
- **`writetolog(uri, text)`** — заглушка, тело пустое (логирование
  отключено/вырезано в релизной сборке).

## 9. Используемые эндпоинты Checkbox API (`/api/v1/...`)

| Метод | Путь | Назначение |
|---|---|---|
| GET | `shift` | проверка состояния смены |
| POST | `shift/open` | открыть смену |
| POST | `shift/xreport` | X-отчёт (промежуточный) |
| POST | `shift/close` | Z-отчёт / закрытие смены |
| POST | `receipt` | открыть чек |
| DELETE | `receipt` | отменить открытый чек |
| PATCH | `receipt` | добавить header/footer к чеку |
| POST | `receipt/good` | добавить позицию товара в чек |
| POST | `receipt/service` | служебное внесение/выплата |
| POST | `receipt/done` | закрыть (провести) чек с оплатами |
| POST | `receipt/document` | нефискальный документ (используется в `FakeMode`) |

## 10. Как получен код

```
apt-get install dotnet-sdk-8.0
dotnet tool install --global ilspycmd --version 8.2.0.7535
ilspycmd CheckboxCassa.dll -o out/            # одиночный файл
ilspycmd -p CheckboxCassa.dll -o out/project  # проект с .csproj
```

Так как оригинальный `ilspycmd` собран под `net6.0`, а в системе был
установлен `.NET 8`, для запуска потребовалась правка `rollForward` в
`ilspycmd.runtimeconfig.json` на `LatestMajor`.

## Ограничения декомпиляции

- Имена локальных переменных и часть форматирования восстановлены
  декомпилятором эвристически и могут отличаться от оригинала (PDB/символы
  отладки отсутствуют).
- Классы и enum'ы из внешней сборки `Onix.Devices` в исходники не входят —
  их сигнатуры видны только по использованию (imports/casts) в
  `CheckboxCassa.cs`.
- Комментарии вида `//IL_xxxx: ...` оставлены декомпилятором как пометки
  о местах, где точное восстановление типов из IL неоднозначно (обычно
  из-за приведений `enum`↔`int`); на поведение кода они не влияют.
