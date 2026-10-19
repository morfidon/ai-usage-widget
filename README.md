# AI Usage Widget — Claude + Codex + Antigravity + Devin + JetBrains Air

Want to see your remaining AI subscription limits without opening five different apps?
This Windows tray widget reads the accounts you already use and keeps their quotas in
one small window. Checking limits does not send prompts or run model tasks.

## Quick start (Windows x64)

You need **native Windows 10/11 x64**, a writable checkout, and a signed-in supported
provider app. Use 64-bit Windows PowerShell 5.1 or PowerShell 7. WSL/Linux/macOS,
Windows ARM64 and x86 are outside the supported setup path. The widget UI is Polish;
these setup instructions and the agent guide are English.

Clone this repository (or download and extract its ZIP), then open PowerShell in
that directory. Git is needed only if you choose to clone it. Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\setup.ps1 -InstallPrerequisites
```

Setup detects installed clients, obtains .NET 9 if needed, builds, runs offline tests,
installs for the current user, checks the selected accounts, and opens the widget.
The first download may take several minutes. No administrator access is needed.
Python, Node and NuGet packages are not required to build or run this app.

Prefer to select the accounts yourself? This works both when pasted into PowerShell
and when an AI agent invokes `powershell.exe -File`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\setup.ps1 -InstallPrerequisites -Providers "Codex,Claude"
```

Autostart and the optional Claude status line bridge stay unchanged on an existing
installation and are **off on a fresh installation**. To enable them explicitly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\setup.ps1 -Providers "Codex,Claude" -Autostart Enable -ClaudeBridge Enable
```

The Claude bridge changes your Claude Code status line, keeps a backup, and invokes
your previous status line command. Autostart adds a current-user Windows Run entry.
Both options also accept `Disable`; the default is `Keep`. Provider selection controls
quota polling, session monitoring and local transcript indexing. Later setup runs
reuse saved provider selection unless you pass `-Providers` again.

| Result | Meaning | Next step |
|---|---|---|
| Exit 0 | Selected quotas passed; the window was verified if launch was requested. | Start using the widget. |
| Exit 3 | Installation finished; a selected account needs login or its app must run. | Follow `Checks[].Action`, then rerun the account check. |
| Exit 2 | A prerequisite, installation, connection, or window check failed. | Read the error and [troubleshooting](docs/TROUBLESHOOTING.md). |

Setup saves `setup-result.json` in `%LOCALAPPDATA%\AIUsageWidget\data`. With `-NoLaunch`,
window verification is skipped and explicitly recorded. `-CheckOnly` checks platform,
SDK/runtime and provider selection without building, downloading, installing or logging in.
It does not verify account access, write permissions or the visible UI.

## Set it up with an AI agent

Paste the repository link with this prompt into an agent running on your Windows PC:

> Analyze this repository, read AGENTS.md, and set up AI Usage Widget for me on this
> Windows PC. Detect my installed providers and use setup.ps1. You may download the
> verified per-user .NET prerequisites if needed. Keep autostart and Claude integration
> unchanged unless I ask for them. Verify the selected accounts and the visible widget.
> If I need to log in, tell me the exact action, wait for me, then finish verification.
> Do not ask me to paste credentials. Report what works and any remaining blocker.

The agent can automate installation, but your account login still belongs to you.
A coding agent running only in a remote Linux environment cannot verify a Windows UI.

## Where it installs and how to update

| Item | Default location |
|---|---|
| Executable | `%LOCALAPPDATA%\Programs\AIUsageWidget\bin\AIUsageWidget.exe` |
| Start Menu | **AI Usage Widget** |
| Settings, quota history and diagnostic summaries | `%LOCALAPPDATA%\AIUsageWidget\data` |
| Optional private .NET SDK | `%LOCALAPPDATA%\AIUsageWidget\dotnet` |
| Installed optional private runtime | `%LOCALAPPDATA%\Programs\AIUsageWidget\dotnet` |

You can move or remove the source checkout after a successful install. To update,
exit the widget from its tray menu, obtain the new source, and rerun setup. Existing
settings, provider selection and history are kept. Old checkout-based installations
migrate known widget data without deleting the originals. Custom install/data locations
are supported with `-InstallDirectory` and `-DataDirectory`; reuse the same paths on updates.

To undo owned autostart, Start Menu and Claude integrations (files and data are retained):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\AIUsageWidget\uninstall.ps1"
```

To check accounts after login:

```powershell
& "$env:LOCALAPPDATA\Programs\AIUsageWidget\bin\AIUsageWidget.exe" --doctor
```

`--doctor` writes `setup-diagnostic.json` in the data directory. For terminal JSON,
run `dotnet <installed-path>\AIUsageWidget.dll --doctor` with a .NET 9 host. If setup
installed a private SDK, its host is `%LOCALAPPDATA%\AIUsageWidget\dotnet\dotnet.exe`.
Diagnostics accept `--providers "Codex,Claude"` for a one-off selection without saving it.

Read [provider login instructions](docs/PROVIDERS.md), [troubleshooting](docs/TROUBLESHOOTING.md),
[privacy and data access](docs/PRIVACY.md), and the [MIT license](LICENSE).

## Building and verification

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\test_bridge.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\test_install.ps1
dotnet .\dist\AIUsageWidget.dll --visual-test
```

`build.ps1` publishes a framework-dependent build and runs self-tests. `global.json`
selects a stable .NET 9 SDK, allowing later .NET 9 feature bands and patches, but not
a different major version or previews ([Microsoft's SDK policy](https://learn.microsoft.com/dotnet/core/tools/global-json)).
The project clears NuGet sources; required Windows desktop targeting packs must be
present in the SDK. To verify without replacing an in-use development executable, use
`build.ps1 -OutputDirectory .verify/check/dist`.

Windows CI runs these checks with synthetic data. Account login and actual post-logon
tray visibility require a Windows user session and are not proven by a green CI build.
See [performance evidence](PERFORMANCE_2026-10-18.md) for the separate A/B measurements.

## Szczegółowa instrukcja po polsku

Natywny widget Windows pokazujący jednocześnie pozostałe limity subskrypcji Claude, Codex, Google Antigravity, Devin oraz kredyty JetBrains AI używane w Air. Nie wysyła promptów ani nie uruchamia zadań AI.

## Obsługa

- Uruchom **AI Usage Widget** z menu Start. `Uruchom widget.cmd` uruchamia lokalną wersję z `dist`, przeznaczoną do pracy nad kodem.
- Przeciągnij nagłówek, aby przesunąć okno. Położenie i wybrany tryb są zapamiętywane.
- `◆` włącza lub wyłącza pozostawanie nad innymi oknami.
- `↻` odświeża limity. Claude respektuje przerwę między odczytami nawet przy ręcznym odświeżaniu.
- `▤` przełącza pełny i kompaktowy widok. Oba pokazują wszystkie limity przekazane przez dostawcę.
- `−` lub Escape ukrywa panel. Kliknięcie rybki przy zegarze przywraca okno.
- Menu `•••` i prawy przycisk ikony przy zegarze udostępniają autostart i zakończenie aplikacji.
- Powiadomienie pojawia się, gdy pozostaje najwyżej 10% danego limitu.

W obu widokach duży procent i kolorowy pasek pokazują **pozostały limit**: 84% oznacza wypełnienie paska w 84%. Pod paskiem widnieje termin resetu. Podpowiedzi uzupełniają kartę o czas odczytu i pełny termin resetu, bez powtarzania procentów. Plan konta jest dostępny w przeglądzie. Niebieski wyróżnia Codex, brzoskwiniowy Claude; żółty sygnalizuje najwyżej 20% pozostałego limitu, a czerwony najwyżej 10%. Dane nieaktualne mają osobny komunikat. Podpowiedzi pojawiają się po 120 ms, mają ograniczoną długość i wysokość; podpowiedź sesji wskazuje tylko przejście do pełnej listy. Stan usługi znajdziesz pod kropką obok nazwy dostawcy; kliknięcie otwiera stronę statusu. Incydenty i brak odczytu pozostają widoczne na karcie.

Każdy limit ma wyróżniony termin resetu: odliczanie (np. „Reset za 1 godz. 40 min”) oraz dzień i godzinę (np. „Środa, 7 października · 14:00”). Dla bliskich terminów pojawiają się „Dzisiaj” i „Jutro”. Tryb kompaktowy także pokazuje reset, ze skróconą datą; pełny termin dostępny jest po najechaniu. Godziny są w lokalnej strefie Windows, odliczanie aktualizuje się automatycznie, a daty pochodzą z odczytu konta.

## Historia, sesje i statystyki

- Kliknięcie nazwy lub postaci **Codex / Claude** otwiera przegląd wyłącznie tego dostawcy: sesje wymagające uwagi, bieżące limity, lokalne liczniki i najważniejsze projekty. Wybór dostawcy i okresu obowiązuje wspólnie w całym oknie; sesje pokazują stan bieżący.
- **◷** otwiera osobne okno z zakładkami Przegląd, Limity, Projekty, Modele i tokeny oraz Sesje. Przycisk działa także w widoku kompaktowym; okno jest też dostępne z menu ikony przy zegarze.
- Historia obejmuje 24 godziny / 7 dni, Claude / Codex i wszystkie zwrócone pule. Każda pula ma osobny wykres wykorzystania (0–100%) i ostatni odczyt. Najedź na wykres, aby zobaczyć szczegóły punktu. W `data/usage-history.json` przechowujemy 31 dni rzeczywistych odczytów, od pierwszego uruchomienia nowej wersji. Braki danych i zmiana cyklu resetu przerywają linię. Nie odtwarzamy procentów z tokenów.
- Licznik sesji i nazwy aktywnych projektów są widoczne również w trybie kompaktowym. Kliknięcie otwiera pełną listę sesji wybranego dostawcy. Claude korzysta z `claude agents --json`. Codex czyta wspólną lokalną historię SQLite i sprawdza systemową blokadę rozmowy, dzięki czemu obejmuje sesje aplikacji desktopowej i odrzuca stare zadania po awarii. Rozpoznane niezakończone pytania i zgody otrzymują stan oczekiwania. Wewnętrzny format Codex może zmienić się po aktualizacji; brak odczytu jest oznaczany.
- Animowane SVG — krab Claude i robot Codex — pokazują pracę, oczekiwanie, gotowość, odpoczynek, zakończenie zadania, niski lub wyczerpany limit i brak aktualnych danych. Animacje zatrzymują się po ukryciu okna i respektują ustawienie animacji Windows. Można je wyłączyć w menu widgetu.
- Ranking projektów pokazuje lokalne tokeny z dnia / tygodnia. Kliknięcie nazwy projektu lub **Sesje i modele →** otwiera rankingi od największego zużycia: sesje oraz modele, z liczbą tokenów i udziałem w projekcie. Kliknięcie nazwy sesji ze strzałką ↗ otwiera tę rozmowę: niebieska prowadzi do Codex, brzoskwiniowa do Claude Desktop. Claude Desktop korzysta z lokalnej rozmowy Claude Code; jeśli nadal działa ona w terminalu, aplikacja może poprosić o jej wcześniejsze zamknięcie. Link do Claude wykorzystuje obsługę `claude://resume?session=UUID` zweryfikowaną w lokalnej wersji Desktop 2.42304.0. Nie wysyła promptu ani nie uruchamia nowego zadania. Przy błędzie otwarcia karta pokazuje komunikat; kopiowanie ID pozostaje dostępne. Sesje mają daty, kopiowanie ID i rozwijany podział na modele; lista pokazuje po 15 sesji na stronę. Tytuły historycznych sesji Codex są odczytywane lokalnie, tylko dla widocznej strony; gdy tytułu brakuje, pozostaje identyfikator. **Skład tokenów** pokazuje cztery czytelne pola: wejście bez cache, odczyt cache, wyjście i zapis cache. Procent przy modelu wewnątrz sesji dotyczy tej sesji. Udział tokenów nie jest udziałem w abonamencie ani kwotą do zapłaty. Powielone zapisy odpowiedzi są deduplikowane. Rozmowy internetowe i dane innych komputerów nie są doliczane.
- **Modele i tokeny** pokazują ranking modeli, wejście bez cache, odczyt cache i wyjście osobno dla Codex lub Claude. Dla Claude dostępne są też zapis cache, tokeny subagentów i duży kontekst w rozwijanym objaśnieniu. Niepuste grupy MCP, umiejętności i subagentów są zwinięte. Liczby wywołań nie są przedstawiane jako procent abonamentu. Stałego kontekstu nie da się oddzielić z samych liczników; pełne przypisanie sprawdzisz w interaktywnym `/usage` w Claude Code.
- Publiczny status Claude i usług OpenAI jest sprawdzany co 3 minuty. Kliknięcie otwiera stronę incydentów. Status OpenAI dotyczy usług łącznie, nie wyłącznie Codex.
- W menu wybierz **Procent przy zegarze · Codex / Claude**. Ikona pokazuje pozostały procent najbardziej ograniczonej wspólnej puli wybranego konta. `?` oznacza brak aktualnego odczytu; podpowiedź zawiera oba konta.

Statystyki są indeksowane w tle, fragmentami. Przy dużej historii pierwsze indeksowanie może potrwać kilka minut; wyniki są wtedy oznaczone jako częściowe. Surowe treści rozmów, argumenty narzędzi i dane logowania nie są kopiowane do plików statystyk. Analiza tokenów pozostaje w pamięci.

Nowe odczyty nie wywołują modeli i nie zużywają tokenów. Widget nie uruchamia `claude -p /usage`. Próba tej komendy z zablokowaną drogą do API modelu w Claude Code 2.1.281 zwróciła samą informację o subskrypcji, bez danych potrzebnych panelowi. Zachowano istniejące źródła limitów i zasady powiadomień. Nowe ustawienia to `TrayProvider` (`Codex` lub `Claude`) i `AnimateMascots` (`true` / `false`).

## Połączenie z kontami

**Antigravity (Google AI Pro/Ultra):** uruchom Antigravity i zaloguj się w nim na swoje konto Google. Widget automatycznie wykrywa lokalny serwer aplikacji (także Antigravity IDE) i odczytuje `RetrieveUserQuotaSummary`. Karta zawiera grupy **Gemini** oraz **Claude / GPT**, każdą z osobnymi paskami **Tydzień** i **5 godzin**. Jeśli konto udostępnia oddzielną pulę **Gemini 4 Argon**, pojawia się ona na końcu karty z jednym paskiem **Tydzień**, własnym procentem i terminem resetu. Argon nie jest łączony ze standardową pulą Gemini ani wyświetlany jako limit pięciogodzinny. Modele w grupie współdzielą pulę; podpowiedź nagłówka wyjaśnia, jakie modele obejmuje. Plan pochodzi z odczytu `GetUserStatus` tego samego serwera, z pierwszeństwem `userTier`. Starsze wersje aplikacji, bez zestawienia limitów, korzystają z `GetUserStatus` / `GetCommandModelConfigs`: wtedy widget scala tylko pule pięciogodzinne i wyraźnie informuje o braku danych tygodniowych. Nie kopiuje procentu ani resetu sesji do limitu tygodniowego. Nie utożsamia tych limitów z kredytami Google AI ani z limitami osobnej subskrypcji Claude/Codex. Serwer musi działać; po zamknięciu Antigravity pozostaje oznaczony jako nieaktualny ostatni odczyt. Odczyt odbywa się co 2 minuty, wyłącznie przez `127.0.0.1`, bez promptów. Token lokalnego połączenia jest przechowywany tylko w pamięci; widget nie odczytuje ani nie zapisuje tokenów OAuth Google, adresu e-mail czy imienia. Wewnętrzny interfejs aplikacji może zmienić się po aktualizacji. Sprawdzono rzeczywiste połączenie z Antigravity na koncie Pro.

**Codex:** lokalny, zainstalowany `codex app-server`, oficjalne `account/rateLimits/read`, z istniejącym logowaniem aplikacji. Bez tworzenia rozmów i wywoływania modeli. Kolejność i nazwy okien wynikają z ich rzeczywistej długości — konto może udostępniać wyłącznie limit tygodniowy. Dodatkowe pule modeli są wyświetlane, gdy zwróci je serwer.

**Claude:** instalator podpina `claude-bridge.ps1` jako oficjalny `statusLine` w użytkownikowym `settings.json`. Claude przekazuje limity po odpowiedzi w zalogowanej sesji Claude Code; widget odczytuje je co 3 sekundy. Istniejący pasek statusu jest zachowany i wykonywany przez bridge. Ustawienia niezwiązane z paskiem pozostają zachowane. W stanie bezczynności widget uzupełnia dane przez wewnętrzny endpoint `https://api.anthropic.com/api/oauth/usage`, używając lokalnego logowania Claude Code. Wygasły token odświeża przez inicjalizację protokołu zainstalowanego Claude Code, bez wiadomości użytkownika i wywołania modelu, w trybie wyłączającym niestandardowe hooki oraz MCP. Rotacja i zapis tokenów pozostają zadaniem Claude Code. Endpoint limitów nie stanowi stabilnego publicznego API: przy odmowie lub ograniczeniu odczytów widget czeka na oficjalne dane sesji. Gdy wygaśnie również uprawnienie do odnowienia logowania, potrzebne jest ponowne zalogowanie w Claude Code.

**Devin:** widget odczytuje istniejące logowanie Devin Desktop z `AppData/Roaming/devin/User/globalStorage/state.vscdb` przez systemową bibliotekę SQLite Windows, w trybie tylko do odczytu. Z tym logowaniem wywołuje `GetUserStatus` na serwerze konta. Pro udostępnia limit dzienny i tygodniowy, Max tygodniowy; wyświetlane są tylko rzeczywiste aktywne limity. Procent oznacza pozostałą pulę, a resety pochodzą z serwera. Ten interfejs klienta jest nieudokumentowany i może zmienić się po aktualizacji Devin. Widget nie modyfikuje danych aplikacji i nie zapisuje klucza konta.

**JetBrains Air:** odczyt przez oficjalny JetBrains Central CLI 1.11.0, wyłącznie `central limit --json`. Przenośne narzędzie znajduje się w `tools/central/central.exe`; można je pobrać ponownie przez `install-central.ps1`. Pierwsze połączenie wymaga osobnego logowania JetBrains: uruchom `Połącz JetBrains Air.cmd` lub wybierz **JetBrains Air · połącz konto** w menu widgetu. Jeśli masz kilka źródeł kredytów, wybierz to samo co w Air. Central przechowuje i odnawia swoje szyfrowane logowanie. Widget nie uruchamia proxy ani nie zmienia konfiguracji agentów. Licznik dotyczy wspólnej puli JetBrains AI używanej także w Air. Pula miesięczna i dodatkowe kredyty mają osobne paski oraz salda. Tylko miesięczna pula otrzymuje odliczanie do resetu; dodatkowe kredyty mają własną ważność, której ten odczyt nie podaje. Nazwa właściciela licencji i adres e-mail nie są zapisywane w danych widgetu. Rzeczywisty odczyt obu puli potwierdzono na zalogowanym koncie.

Przy uruchomieniu Windows widget łączy się automatycznie. Nieaktywny internet lub brak logowania jednego dostawcy nie blokuje pozostałych kart. Zapisany odczyt zachowuje swój czas, a po przekroczeniu resetu procent zastępuje kreska do czasu rzeczywistego odczytu. Gdy wszystkie karty nie mieszczą się na ekranie, lista przewija się, pozostawiając nagłówek i stopkę dostępne.

Dokumentacja: [Codex App Server](https://developers.openai.com/codex/app-server), [Claude Code status line](https://code.claude.com/docs/en/statusline), [limity Antigravity](https://www.antigravity.google/docs/cli/commands/usage), [opis lokalnego interfejsu Antigravity](https://github.com/mesomya/openusage-windows/blob/main/docs/providers/antigravity.md), [plany Devin](https://docs.devin.ai/admin/billing/self-serve), [przykład odczytu Devin](https://github.com/wakamex/devin-cli-usage), [JetBrains Central](https://www.jetbrains.com/help/central-cli/cli-commands.html), [kredyty Air](https://www.jetbrains.com/help/air/ai-credits.html). Format JSON kredytów sprawdzono także w [parserze ccstatusline](https://github.com/CrazyCoder/ccstatusline).

## Instalacja i budowanie

Wymagane Windows 10/11 oraz .NET Desktop Runtime 9; do kompilacji .NET SDK 9. Projekt nie pobiera pakietów NuGet i nie wymaga Pythona ani Node.

Przed ponowną kompilacją zamknij działający widget przez **Zakończ** w menu jego ikony przy zegarze.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

Zalecanym poleceniem jest teraz `setup.ps1`, opisany na początku tego pliku. Instalator kopiuje aplikację do stałego katalogu bieżącego użytkownika i tworzy skrót w menu Start. Autostart (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, `AIUsageWidget`) oraz pasek Claude włącza się jawnie opcjami `-Autostart Enable` i `-ClaudeBridge Enable`. Nie wymaga uprawnień administratora. `dist` jest wynikiem kompilacji; zainstalowana aplikacja działa niezależnie od położenia repozytorium.

```powershell
# Wyłącz autostart i przywróć poprzedni pasek statusu Claude:
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\AIUsageWidget\uninstall.ps1"
# Diagnostyka rzeczywistych połączeń bez wyświetlania tokenów:
dotnet .\dist\AIUsageWidget.dll --diagnose
```

`data/settings.json` zawiera położenie, `Compact`, `Topmost`, `RefreshSeconds` (domyślnie 60), opcjonalny `CodexPath` oraz `ClaudeDirectory`. Zmiany tego pliku obowiązują po ponownym uruchomieniu. API Claude jest odczytywane najwyżej co 5 minut, z wydłużaniem przerwy po HTTP 429. Codex jest odczytywany co 60 sekund, a Antigravity, Devin i JetBrains co 2 minuty. Widget nie zapisuje tokenów; lokalny cache zawiera tylko odczytane limity i daty. Kopia ustawień Claude sprzed instalacji może zawierać prywatne ustawienia i nie jest przeznaczona do udostępniania.

## Weryfikacja

Porównanie wydajności, zakres pomiarów i ograniczenia opisano w [PERFORMANCE_2026-10-18.md](PERFORMANCE_2026-10-18.md). Powtarzalne testy A/B uruchamia `tests/run_performance.ps1`; nie wywołują modeli.

Dodatkowe polecenia: `--diagnose-activity` sprawdza sesje i pojedynczy fragment analizy; `--diagnose-activity --full-history` kończy indeksowanie ostatniego tygodnia. Wynik diagnostyczny zawiera tylko liczniki i błędy źródeł. `--visual-test` sprawdza widok kompaktowy, przycisk historii, zakładki, SVG, ukrywanie i przywracanie okna na osobnych danych testowych oraz wejście z przycisków dostawców, rozdzielenie modeli i sesji, wspólny filtr okresu, rankingi sesji i modeli projektu, paginację, otwieranie dokładnej sesji Codex i Claude oraz obsługę błędu bez uruchamiania zewnętrznej aplikacji podczas testu, rozwijanie szczegółów po odświeżeniu, małe okno oraz pustą i nieciągłą historię; zapisuje 24 podglądy (w tym przeglądy Codex/Claude, szczegóły projektów i podpowiedzi) `data/visual-*.png`. Test nie zmienia Twoich ustawień ani prawdziwej historii limitów.

`build.ps1` uruchamia testy parserów, terminów resetu i ograniczeń odnowienia logowania, w tym limity Devin, wartości zerowe pomijane przez protobuf, brak dziennego limitu Max, bezpieczne adresy serwera, stringi liczbowe JetBrains, milisekundy resetu, osobne pule miesięczne i dodatkowe oraz brakujące i nielimitowane pule. `tests/test_bridge.ps1` sprawdza zapis cache, zachowanie wcześniejszego paska i odfiltrowanie wrażliwych pól na oddzielnych danych testowych. `--diagnose` sprawdza pięć rzeczywistych połączeń. `--diagnose-antigravity` sprawdza tylko lokalny odczyt Antigravity i zapisuje pozbawiony danych konta wynik do `data/antigravity-diagnostic.json`. Testy Antigravity obejmują dwie podstawowe grupy i cztery niezależne limity, oddzielny tygodniowy limit Argon, terminy resetu, pominięte zera protobuf, brakujące i wyłączone pule, niepoprawne wartości, stabilną kolejność, zgodność cache oraz uczciwy odczyt zastępczy bez limitów tygodniowych. Połączenia są ograniczone do lokalnych metod odczytu. `--capture` zapisuje podgląd aktualnego okna do `data/preview.png` i sprawdzenie widoczności do `data/window-check.json` po 12 sekundach.
