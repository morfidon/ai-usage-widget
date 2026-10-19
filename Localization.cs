using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AIUsageWidget;

// Built-in UI text only. Never translate provider IDs, file paths or conversation titles.
public static class L
{
    public static string Language { get; private set; } = "en";
    public static bool IsPolish => Language == "pl";
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(IsPolish ? "pl-PL" : "en-US");
    public static string TimestampPattern => IsPolish ? "dd.MM.yyyy HH:mm:ss" : "d MMM yyyy HH:mm:ss";
    public static string ShortTimestampPattern => IsPolish ? "dd.MM HH:mm" : "d MMM HH:mm";
    public static string Normalize(string? language) => language == "pl" ? "pl" : "en";
    public static void SetLanguage(string? language)
    {
        Language = Normalize(language);
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
    }
    public static string T(string polish) => IsPolish ? polish : English.GetValueOrDefault(polish, polish);
    public static string F(string polish, params object?[] values) => string.Format(Culture, T(polish), values);
    // Old cache/history labels survive language changes. Translate only built-in
    // fields at their presentation boundary, never user-supplied session titles.
    public static string Display(string value)
    {
        var entries = IsPolish ? Polish : English;
        if (entries.TryGetValue(value, out var exact)) return exact;
        var balance = Regex.Match(value, @"^([\d.,]+) / ([\d.,]+) (?:kredytów AI|AI credits)$", RegexOptions.CultureInvariant);
        if (balance.Success && double.TryParse(balance.Groups[1].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var left)
            && double.TryParse(balance.Groups[2].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var total))
            return left.ToString("0.##", Culture) + " / " + total.ToString("0.##", Culture) + T(" kredytów AI");
        foreach (var pair in IsPolish ? PolishPrefixes : EnglishPrefixes)
            if (value.StartsWith(pair.Key, StringComparison.Ordinal)) return pair.Value + value[pair.Key.Length..];
        return value;
    }
    public static ProviderState Localize(ProviderState state)
    {
        List<UsageWindow>? windows = null;
        for (int i = 0; i < state.Windows.Count; i++) {
            var window = state.Windows[i];
            string label = QuotaLabel(window.Label);
            string? balance = window.BalanceText is null ? null : Display(window.BalanceText);
            string? detail = window.DetailText is null ? null : Display(window.DetailText);
            if (label == window.Label && balance == window.BalanceText && detail == window.DetailText) continue;
            windows ??= new(state.Windows);
            windows[i] = window with { Label = label, BalanceText = balance, DetailText = detail };
        }
        string status = Display(state.Status);
        string? error = state.Error is null ? null : Display(state.Error);
        return windows is null && status == state.Status && error == state.Error ? state
            : state with { Status = status, Error = error, Windows = windows ?? state.Windows };
    }
    public static string QuotaLabel(string value)
    {
        string translated = Display(value);
        if (translated != value) return translated;
        int separator = value.LastIndexOf(" · ", StringComparison.Ordinal);
        if (separator >= 0) return value[..(separator + 3)] + QuotaLabel(value[(separator + 3)..]);
        var duration = Regex.Match(value, @"^([\d.,]+) (godz\.|dni|h|days)$", RegexOptions.CultureInvariant);
        if (!duration.Success || !double.TryParse(duration.Groups[1].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)) return value;
        bool hours = duration.Groups[2].Value is "godz." or "h";
        return number.ToString("0.#", Culture) + " " + (IsPolish ? hours ? "godz." : "dni" : hours ? "h" : "days");
    }
    static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        ["Tokeny we wszystkich projektach"] = "Tokens across all projects",
        ["20 projektów"] = "20 projects",
        ["50 projektów"] = "50 projects",
        ["Aktywność w czasie"] = "Activity over time",
        ["Brak słupka oznacza brak zapisanych tokenów, nie potwierdzoną bezczynność."] = "An empty bar means no recorded tokens, not confirmed inactivity",
        ["Brak sesji w wybranym stanie."] = "No sessions with this status.",
        ["Pokazano {0} z {1} · udział ze wszystkich projektów"] = "Showing {0} of {1} · shares across all projects",
        ["Brak projektów pasujących do wyszukiwania."] = "No projects match your search.",
        ["Tokeny lokalne · nie oznaczają kosztu ani procentu abonamentu"] = "Local tokens · not a cost or subscription usage estimate",
        ["Limity konta · odczyty dostawcy, nie tokeny"] = "Account limits · provider readings, separate from tokens",
        ["Stan na żywo · niezależny od okresu statystyk"] = "Live status · independent of the selected period",
        ["Stan sesji"] = "Session status",
        ["Liczba projektów"] = "Project limit",
        ["Sortowanie projektów"] = "Sort projects",
        ["Szukaj projektu"] = "Find a project",
        ["Bezczynne"] = "Idle",
        ["Wszystkie stany"] = "All states",
        ["Widok zwarty"] = "Compact view",
        ["Wszystkie"] = "All",
        ["Nazwa projektu"] = "Project name",
        ["Ostatnia aktywność"] = "Recent activity",
        ["Ostatnia godzina"] = "Last hour",
        ["\nKliknij, aby otworzyć stronę statusu."] = "\nClick to open the status page.",
        ["\nOdczyt: {0:HH:mm:ss}"] = "\nUpdated: {0:HH:mm:ss}",
        ["\nWejście >200 tys. tokenów: {0}. Stały kontekst sprawdzisz w /usage w Claude Code."] = "\nInput >200K tokens: {0}. Check persistent context with /usage in Claude Code.",
        [" kredytów AI"] = " AI credits",
        [" tokenów"] = " tokens",
        [" więcej · szczegóły"] = " more · details",
        [" {0} godz."] = " {0} h",
        [" {0}–{1} z {2} "] = " {0}–{1} of {2} ",
        [" · automatyczne odświeżanie"] = " · auto-refresh",
        [" · incydent"] = " · incident",
        [". Spróbuj ponownie lub skopiuj ID sesji."] = ". Try again or copy the session ID.",
        ["10 z {0} narzędzi"] = "10 of {0} tools",
        ["20 z {0} projektów · udział liczony ze wszystkich"] = "20 of {0} projects · share includes all projects",
        ["24 godziny"] = "24 hours",
        ["26,6 / 35 kredytów AI"] = "26.6 / 35 AI credits",
        ["5 godzin"] = "5 hours",
        ["7 dni"] = "7 days",
        ["? Czeka: {0}"] = "? Waiting: {0}",
        ["AI Usage · szczegóły"] = "AI Usage · details",
        ["Aktualizacja danych"] = "Updating data",
        ["Aktywne sesje na tym komputerze"] = "Active sessions on this computer",
        ["Aktywność"] = "Activity",
        ["Animacje postaci"] = "Character animations",
        ["Antigravity ograniczył odczyty · ponowię połączenie"] = "Antigravity rate-limited reads · will retry later",
        ["Bez resetu miesięcznego"] = "No monthly reset",
        ["Bez resetu miesięcznego · ważność dodatkowych kredytów sprawdzisz na koncie JetBrains"] = "No monthly reset · check extra credit expiry in your JetBrains account",
        ["Bezczynna"] = "Idle",
        ["Brak Codex / Claude"] = "No Codex / Claude",
        ["Brak aktualnych danych"] = "No current data",
        ["Brak aktywnych sesji."] = "No active sessions.",
        ["Brak czasu resetu"] = "Reset time unavailable",
        ["Brak danych statusu usługi"] = "No service status data",
        ["Brak danych tokenów w tym okresie."] = "No token data for this period.",
        ["Brak danych wejścia"] = "No input data",
        ["Brak narzędzia JetBrains Central"] = "JetBrains Central not found",
        ["Brak odczytu"] = "No reading",
        ["Brak odczytu Antigravity · ponowię połączenie"] = "Antigravity unavailable · will retry",
        ["Brak odczytu Antigravity · sprawdź logowanie w aplikacji"] = "Antigravity unavailable · check sign-in in the app",
        ["Brak odczytu Devin · ponowię połączenie"] = "Devin unavailable · will retry",
        ["Brak odczytu JetBrains · ponowię połączenie"] = "JetBrains unavailable · will retry",
        ["Brak odczytu limitów tygodniowych"] = "Weekly limits unavailable",
        ["Brak odczytu sesji"] = "Session data unavailable",
        ["Brak odczytów"] = "No readings",
        ["Brak odpowiedzi w wybranym okresie."] = "No responses in the selected period.",
        ["Brak odpowiedzi · ponowię połączenie"] = "No response · will retry",
        ["Brak poprawnego ID rozmowy Claude. Możesz skopiować identyfikator sesji."] = "Invalid Claude conversation ID. You can copy the session ID.",
        ["Brak poprawnego ID rozmowy. Możesz skopiować identyfikator sesji."] = "Invalid conversation ID. You can copy the session ID.",
        ["Brak połączenia · czekam na Claude Code"] = "Disconnected · waiting for Claude Code",
        ["Brak połączenia · spróbuję ponownie"] = "Disconnected · will retry",
        ["Brak wybranych dostawców"] = "No providers selected",
        ["Brak zapisanych odpowiedzi w tym okresie."] = "No saved responses for this period.",
        ["Brak zgłoszonych awarii"] = "No incidents reported",
        ["Claude nie udostępnił limitów · czekam na sesję"] = "Claude did not return limits · waiting for a session",
        ["Claude ograniczył odczyt · ponowię później"] = "Claude rate-limited reads · will retry later",
        ["Claude · strona limitów"] = "Claude · usage page",
        ["Codex · strona limitów"] = "Codex · usage page",
        ["Czeka na Ciebie"] = "Waiting for you",
        ["Czekam na aktualny odczyt"] = "Waiting for a current reading",
        ["Czekam na dane z Claude Code · otwórz sesję"] = "Waiting for Claude Code data · open a session",
        ["Czekam na odczyt po resecie.\n"] = "Waiting for a reading after reset.\n",
        ["Dane nieaktualne"] = "Outdated data",
        ["Dane pojawią się po odpowiedzi w Claude Code."] = "Data appears after a response in Claude Code.",
        ["Dane z ostatniego odczytu · czekam na aktualizację"] = "Last reading · waiting for an update",
        ["Decyzja dotycząca zadania"] = "Task decision needed",
        ["Devin · strona limitów"] = "Devin · usage page",
        ["Dodatkowe"] = "Extra credits",
        ["Dokończ logowanie JetBrains w przeglądarce"] = "Complete JetBrains sign-in in your browser",
        ["Dostawca"] = "Provider",
        ["Dostawcy"] = "Providers",
        ["Dostawcy, widok, zawsze na wierzchu i autostart Windows."] = "Providers, language, view, always on top and Windows startup.",
        ["Dzień"] = "Day",
        ["Dzisiaj"] = "Today",
        ["Folder projektu"] = "Project folder",
        ["Folder sesji"] = "Session folder",
        ["Gotowy"] = "Ready",
        ["Historia i szczegóły"] = "History and details",
        ["Historia limitów"] = "Quota history",
        ["Historia limitów i szczegóły"] = "Quota history and details",
        ["Incydent"] = "Incident",
        ["Incydent usługi"] = "Service incident",
        ["Indeksowanie… wyniki częściowe"] = "Indexing… partial results",
        ["Jak czytać te dane"] = "How to read this data",
        ["Jeszcze bez odczytów"] = "No readings yet",
        ["JetBrains Air · kredyty konta"] = "JetBrains Air · account credits",
        ["JetBrains Air · połącz konto"] = "JetBrains Air · connect account",
        ["Jutro"] = "Tomorrow",
        ["Konto nie udostępnia limitu procentowego"] = "Account does not expose a percentage limit",
        ["Konto nie udostępnia limitów"] = "Account does not expose limits",
        ["Konto nie udostępnia limitów modeli"] = "Account does not expose model limits",
        ["Konto nie udostępnia limitów procentowych"] = "Account does not expose percentage limits",
        ["Kopiuj ID"] = "Copy ID",
        ["Kopiuj ID sesji "] = "Copy session ID ",
        ["Kredyty AI"] = "AI credits",
        ["LIMITY KONT AI"] = "AI ACCOUNT LIMITS",
        ["Limit dodatkowy"] = "Secondary limit",
        ["Limit główny"] = "Primary limit",
        ["Limit wyczerpany"] = "Quota exhausted",
        ["Limit wydatków"] = "Spending limit",
        ["Limity"] = "Limits",
        ["Lokalna aktywność · "] = "Local activity · ",
        ["Mało limitu"] = "Low quota",
        ["Miesiąc"] = "Month",
        ["Modele"] = "Models",
        ["Modele ({0})"] = "Models ({0})",
        ["Modele ({0}) i skład tokenów"] = "Models ({0}) and token breakdown",
        ["Modele i skład tokenów"] = "Models and token breakdown",
        ["Modele i tokeny"] = "Models and tokens",
        ["Modele w tej sesji"] = "Models in this session",
        ["Monitor sesji Codex niedostępny · sprawdź wersję aplikacji"] = "Codex session monitor unavailable · check the app version",
        ["Najedź na wykres, aby zobaczyć odczyt. Przerwy oznaczają reset lub brak danych."] = "Hover over the chart to see a reading. Gaps indicate a reset or missing data.",
        ["Najwięcej tokenów"] = "Most tokens",
        ["Największe zużycie · udział w tokenach projektu"] = "Highest usage · share of project tokens",
        ["Następna strona sesji"] = "Next page of sessions",
        ["Nie można odczytać historii ("] = "Cannot read history (",
        ["Nie można odczytać sesji Claude"] = "Cannot read Claude sessions",
        ["Nie można odczytać statusu usługi"] = "Cannot read service status",
        ["Nie można uruchomić serwera Codex."] = "Cannot start the Codex server.",
        ["Nie można zapisać historii ("] = "Cannot save history (",
        ["Nie można zmienić autostartu."] = "Cannot change Windows startup.",
        ["Nie udało się odczytać limitów Codex."] = "Could not read Codex limits.",
        ["Nie udało się otworzyć "] = "Could not open ",
        ["Nie udało się otworzyć Claude Desktop. Uruchom aplikację i spróbuj ponownie lub skopiuj ID sesji."] = "Could not open Claude Desktop. Start the app and try again, or copy the session ID.",
        ["Nie udało się otworzyć Codex. Uruchom aplikację i spróbuj ponownie lub skopiuj ID sesji."] = "Could not open Codex. Start the app and try again, or copy the session ID.",
        ["Nie udało się zapisać wyboru dostawców. Sprawdź dostęp do katalogu danych."] = "Could not save provider selection. Check access to the data folder.",
        ["Nie znaleziono Claude Code"] = "Claude Code not found",
        ["Nie znaleziono aplikacji Codex."] = "Codex app not found.",
        ["Nie znaleziono danych aplikacji Devin"] = "Devin app data not found",
        ["Nieobsługiwany serwer konta Devin"] = "Unsupported Devin account server",
        ["Nieznany projekt"] = "Unknown project",
        ["Nowa mapa"] = "New map",
        ["Oczekiwanie"] = "Waiting",
        ["Oczekiwanie na odczyt"] = "Waiting for a reading",
        ["Oczekiwanie na pierwszy odczyt…"] = "Waiting for the first reading…",
        ["Oczekiwanie na status usługi"] = "Waiting for service status",
        ["Odczyt cache"] = "Cache read",
        ["Odczyt sesji Claude niedostępny"] = "Claude session data unavailable",
        ["Odczyt zastępczy · limity 5-godzinne"] = "Fallback reading · five-hour limits",
        ["Odczyt {0:HH:mm}"] = "Updated {0:HH:mm}",
        ["Odczyt: {0:HH:mm:ss}"] = "Updated: {0:HH:mm:ss}",
        ["Odczyt: {0}"] = "Updated: {0}",
        ["Odpoczywa"] = "Sleeping",
        ["Odpowiedzi"] = "Responses",
        ["Odpowiedź lub zgoda"] = "Response or approval needed",
        ["Odpowiedź na pytanie"] = "Answer needed",
        ["Odśwież limity"] = "Refresh limits",
        ["Odświeżanie…"] = "Refreshing…",
        ["Okres statystyk"] = "Statistics period",
        ["Ostatni odczyt · {0:dd.MM HH:mm}"] = "Last reading · {0:d MMM HH:mm}",
        ["Ostatni zapis · oczekiwanie na aktualny odczyt"] = "Last saved data · waiting for a current reading",
        ["Otwarty dialog"] = "Open dialog",
        ["Otwórz Claude Code i zaloguj się"] = "Open Claude Code and sign in",
        ["Otwórz folder widgetu"] = "Open widget folder",
        ["Otwórz listę sesji"] = "Open session list",
        ["Otwórz projekt "] = "Open project ",
        ["Otwórz przegląd konta"] = "Open account overview",
        ["Otwórz rozmowę w "] = "Open conversation in ",
        ["Otwórz sesje"] = "Open sessions",
        ["Otwórz szczegóły "] = "Open details for ",
        ["Otwórz w "] = "Open in ",
        ["Pokaż widget"] = "Show widget",
        ["Pominięte pliki lub rekordy: {0}"] = "Skipped files or records: {0}",
        ["Poprawa panelu"] = "Improve dashboard",
        ["Poprzednia strona sesji"] = "Previous page of sessions",
        ["Pozostało"] = "Remaining",
        ["Pozostało {0:0}% ({1}{2}). {3}"] = "{0:0}% left ({1}{2}). {3}",
        ["Pozostały limit"] = "Remaining quota",
        ["Połączono"] = "Connected",
        ["Połączono · Claude Code"] = "Connected · Claude Code",
        ["Połączono · konto Devin"] = "Connected · Devin account",
        ["Połączono · kredyty JetBrains AI"] = "Connected · JetBrains AI credits",
        ["Połączono · limity modeli Antigravity"] = "Connected · Antigravity model limits",
        ["Połączono · wspólne pule modeli"] = "Connected · shared model pools",
        ["Pracuje"] = "Working",
        ["Pracuje: {0} · Czeka: {1}"] = "Working: {0} · Waiting: {1}",
        ["Procent przy zegarze"] = "Tray percentage",
        ["Procent przy zegarze · Claude"] = "Tray percentage · Claude",
        ["Procent przy zegarze · Codex"] = "Tray percentage · Codex",
        ["Projekty"] = "Projects",
        ["Przegląd"] = "Overview",
        ["Przegląd interfejsu"] = "Review interface",
        ["Przełącz tryb kompaktowy"] = "Toggle compact mode",
        ["Ranking modeli projektu"] = "Project model ranking",
        ["Ranking sesji projektu"] = "Project session ranking",
        ["Reset minął · czekam na dane"] = "Reset passed · waiting for data",
        ["Reset za "] = "Resets in ",
        ["Serwer Codex zakończył połączenie."] = "Codex server closed the connection.",
        ["Sesja "] = "Session ",
        ["Sesje"] = "Sessions",
        ["Sesje ({0})"] = "Sessions ({0})",
        ["Sesje Claude · brak aktualnego odczytu"] = "Claude sessions · no current reading",
        ["Sesje i modele  →"] = "Sessions and models  →",
        ["Sesje teraz"] = "Sessions now",
        ["Sesje · brak odczytu"] = "Sessions · no reading",
        ["Skopiowano"] = "Copied",
        ["Skład tokenów"] = "Token breakdown",
        ["Skład zużycia tokenów"] = "Token usage breakdown",
        ["Sprawdź oczekujące sesje"] = "Check waiting sessions",
        ["Spróbuj ponownie"] = "Try again",
        ["Stan nieznany"] = "Unknown state",
        ["Status niedostępny"] = "Status unavailable",
        ["Status usług Claude"] = "Claude service status",
        ["Status usług OpenAI"] = "OpenAI service status",
        ["Subagenci"] = "Subagents",
        ["Tokeny"] = "Tokens",
        ["Tokeny pochodzą z lokalnych zapisów rozmów. Nie określają procentu abonamentu ani kosztu. Wejście bez cache, odczyt cache, zapis cache i wyjście to rozłączne części sumy. Liczba wywołań narzędzi nie określa ich kosztu."] = "Tokens come from local conversation logs. They do not represent subscription usage or cost. Uncached input, cache reads, cache writes and output are separate parts of the total. Tool call counts do not represent their cost.",
        ["Tokeny subagentów"] = "Subagent tokens",
        ["Tryb kompaktowy"] = "Compact mode",
        ["Tryb kompaktowy / pełny"] = "Compact / full mode",
        ["Tydzień"] = "Weekly",
        ["Tydzień · "] = "Weekly · ",
        ["Tydzień · Opus"] = "Weekly · Opus",
        ["Tydzień · Sonnet"] = "Weekly · Sonnet",
        ["Tydzień · model"] = "Weekly · model",
        ["Udział w tokenach z tego komputera"] = "Share of tokens on this computer",
        ["Ukryj przy zegarze"] = "Hide to tray",
        ["Ukryj przy zegarze · kliknij ikonę, aby przywrócić"] = "Hide to tray · click the icon to restore",
        ["Umiejętności"] = "Skills",
        ["Uruchamiaj z Windows"] = "Start with Windows",
        ["Uruchom Antigravity i zaloguj się na konto Google"] = "Start Antigravity and sign in to Google",
        ["Ustaw w prawym dolnym rogu"] = "Move to bottom-right corner",
        ["Ustawienia"] = "Options",
        ["W menu ••• → Dostawcy zaznacz konta, których chcesz używać. Możesz też wybrać wykryte aplikacje."] = "Choose the accounts you use in ••• → Providers. You can also select detected apps.",
        ["Wejście bez cache"] = "Uncached input",
        ["Weryfikacja tłumaczeń"] = "Verify translations",
        ["Widget napotkał błąd. Uruchom go ponownie. Szczegóły zapisano w data/errors.log."] = "The widget encountered an error. Restart it. Details were saved to data/errors.log.",
        ["Wolne · "] = "Remaining · ",
        ["Wspólna pula modeli"] = "Shared model pool",
        ["Wspólna pula: "] = "Shared pool: ",
        ["Wszystkie projekty"] = "All projects",
        ["Wybierz dostawców"] = "Choose providers",
        ["Wybierz wykryte aplikacje"] = "Select detected apps",
        ["Wyjście"] = "Output",
        ["Wykres pojawi się po pierwszym odczycie limitów w tym okresie."] = "The chart appears after the first quota reading in this period.",
        ["Zadanie zakończone"] = "Task completed",
        ["Zakończ"] = "Quit",
        ["Zakłócenia usług"] = "Service disruption",
        ["Zaloguj się ponownie w aplikacji Devin"] = "Sign in to Devin again",
        ["Zaloguj się w aplikacji Codex."] = "Sign in to the Codex app.",
        ["Zaloguj się w aplikacji Devin"] = "Sign in to the Devin app",
        ["Zapis cache"] = "Cache write",
        ["Zapisany odczyt · łączenie…"] = "Saved reading · connecting…",
        ["Zastępuje obecny wybór aplikacjami znalezionymi na tym komputerze. Nie sprawdza logowania."] = "Replaces your selection with apps found on this computer. Does not check sign-in.",
        ["Zawsze na wierzchu"] = "Always on top",
        ["Zgoda na dostęp"] = "Access approval needed",
        ["Zgoda na działanie"] = "Action approval needed",
        ["dni"] = "days",
        ["dzień"] = "day",
        ["nieznany"] = "unknown",
        ["odpowiedzi"] = "responses",
        ["odpowiedź"] = "response",
        ["pozostało"] = "left",
        ["sesja"] = "session",
        ["sesje"] = "sessions",
        ["sesji"] = "sessions",
        ["wywołania"] = "calls",
        ["wywołanie"] = "call",
        ["wywołań"] = "calls",
        ["{0:0.##} mln"] = "{0:0.##}M",
        ["{0:0.#} dni"] = "{0:0.#} days",
        ["{0:0.#} godz."] = "{0:0.#} h",
        ["{0:0.#} tys."] = "{0:0.#}K",
        ["{0:0.#}% tokenów to odczyt cache · udział nie oznacza kosztu"] = "{0:0.#}% of tokens are cache reads · this share does not represent cost",
        ["{0:0.#}% wejścia odczytano z cache"] = "{0:0.#}% of input was read from cache",
        ["{0:0.#}% wykorzystano"] = "{0:0.#}% used",
        ["{0} godz."] = "{0} h",
        ["{0} tokenów  ·  {1:0.#}%"] = "{0} tokens  ·  {1:0.#}%",
        ["{0} tokenów · {1}"] = "{0} tokens · {1}",
        ["{0} · niski limit"] = "{0} · low quota",
        ["{0} · {1} · pozostały limit"] = "{0} · {1} · remaining quota",
        ["{0}: historia wykorzystania od 0 do 100 procent"] = "{0}: usage history from 0 to 100 percent",
        ["Łączenie z kontami…"] = "Connecting to accounts…",
        ["Łączenie…"] = "Connecting…",
        ["Źródło danych"] = "Data source",
        ["← Projekty"] = "← Projects",
        ["▶ Pracuje: {0}"] = "▶ Working: {0}",
        ["○  Status usługi niedostępny"] = "○  Service status unavailable",
        ["○ Bezczynne: {0}"] = "○ Idle: {0}",
        ["●  Usługa działa"] = "●  Service operational",
    };
    static readonly Dictionary<string, string> Polish = English.GroupBy(p => p.Value, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);
    static KeyValuePair<string, string>[] Prefixes(Dictionary<string, string> entries) => entries
        .Where(p => !p.Key.Contains('{') && p.Key.Length > 3 && (p.Key.EndsWith(' ') || p.Key.EndsWith('(')))
        .OrderByDescending(p => p.Key.Length).ToArray();
    static readonly KeyValuePair<string, string>[] EnglishPrefixes = Prefixes(English), PolishPrefixes = Prefixes(Polish);
}
