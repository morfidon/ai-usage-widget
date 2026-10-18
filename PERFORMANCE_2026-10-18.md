# Optymalizacja AI Usage Widget — 18 października 2026

Wersja bazowa: `a348497`. Zmiany zachowują źródła danych, częstotliwości odświeżania, format liczników, reguły powiadomień i animacje.

## Zmiany

- Analiza używa metadanych zwróconych przez enumerację katalogu. Nie otwiera ponownie plików, których długość nie zmieniła się od ostatniego odczytu. Dopisane i skrócone pliki nadal są przetwarzane; usunięte pliki i wpisy starsze niż siedem dni znikają ze statystyk.
- Bufor 64 KB jest wypożyczany z puli i czyszczony przy zwrocie. Pełne linie mieszczące się w bloku są sprawdzane i parsowane bez dodatkowego kopiowania. Pliki zakończone pełną linią nie zatrzymują dużego bufora roboczego. Nie utrwalamy treści rozmów ani indeksu statystyk na dysku.
- Niezmienione statystyki ponownie wykorzystują gotowe listy. Deduplicacja zmienionych danych używa słownika zamiast tworzenia grup dla każdej odpowiedzi. Cache uwzględnia najbliższy termin wygaśnięcia wpisu.
- Panel wymienia wyłącznie karty, których dane lub widoczne informacje się zmieniły. Uwzględnia limity, odliczanie, wygasanie, nieaktualność danych, sesje, status usług, tryb kompaktowy i stan postaci. Reset fazy animacji przy logicznym odświeżeniu pozostał zachowany.
- Stałe części SVG, oczy i znaki stanów są rysowane do zamrożonych grup i używane ponownie. Ruch oraz animowane elementy klawiatury nadal aktualizują się w tym samym tempie. Zmiana DPI odświeża tekst znaku stanu.
- Okno szczegółów oblicza treść wybranej zakładki; pozostałe odświeża przy wejściu do nich.

## Kontrolowane pomiary

Trzy naprzemienne pary wersji bazowej i zmienionej. Ten sam kod pomiarowy, te same 80 syntetycznych plików, 204 544 000 bajtów, 32 000 odpowiedzi. Dane znajdowały się w pamięci podręcznej systemu plików. Poniżej mediany; czas obejmuje wskazaną operację, nie cały program.

| Próba | Przed | Po | Skrócenie czasu | Mniej przydzielanej pamięci zarządzanej |
|---|---:|---:|---:|---:|
| 15 odczytów niezmienionej historii | 710,7 ms | 4,3 ms | 99,4% | 99,7%: 221,1 → 0,71 MB |
| 100 odświeżeń niezmienionych kart | 2433,3 ms | 31,6 ms | 98,7% | 98,5%: 149,1 → 2,31 MB |
| 4800 rysowań postaci | 187,2 ms | 22,0 ms | 88,2% | 82,5%: 27,5 → 4,81 MB |

Pierwsze indeksowanie przydzieliło o 24,7% mniej pamięci: 56,25 → 42,35 MB. Mediana czasu wyniosła 714 → 606 ms, ale jedna z trzech par była wolniejsza. Nie traktuję tego jako stabilnego przyspieszenia startu całej aplikacji. Rzeczywista historia nadal jest czytana od początku po restarcie; nie zmieniono zasad przechowywania danych.

W osobnym teście całego animowanego okna, bez kolektorów, wykonano cztery naprzemienne pary po 15 sekund. Mediana czasu CPU spadła z 1297 do 1156 ms, około 11%. Pierwsza para miała duży rozrzut rozruchowy; w kolejnych redukcja wynosiła około 3%, 18% i 20%. Praktyczne oszacowanie dla tej sceny to około 10%, z wyraźną zmiennością. Nie obejmuje ono podprocesu Claude CLI, sieci ani indeksowania. Duże procenty z tabeli nie oznaczają takiego samego spadku CPU całej aplikacji.

## Zgodność i ograniczenia

- Wszystkie trzy pary zwróciły identyczny skrót pełnych statystyk i te same 32 000 odpowiedzi.
- Wszystkie 64 obrazy postaci są identyczne piksel w piksel: dwa źródła, osiem stanów, cztery fazy ruchu.
- 103 testy przeszły. Nowe regresje obejmują dopisywanie niepełnej linii, jej dokończenie, skrócenie i usunięcie pliku, pozostałą kopię odpowiedzi, dokładną granicę siedmiu dni oraz rekord większy od limitu bufora.
- Kontrola WPF potwierdziła siedem podglądów, sesje kompaktowe, przycisk historii, zmianę limitu i stanu sesji, przewijanie oraz ukrywanie i przywracanie okna.
- Obserwacja działającego programu: stary proces średnio 782 MB working set, nowy około 291 MB podczas pierwszej minuty indeksowania. Różniły się czasem działania i etapem pracy, więc nie jest to ścisłe A/B ani gwarancja długotrwałego zużycia RAM. Z tego samego powodu nie wyliczam z tych dwóch próbek oszczędności całego CPU.
- Próba oddzielenia cienia okna od treści została odrzucona: nie zapewniła zgodności rasteryzacji tekstu ani pewnego zysku. Wygląd cienia pozostał zachowany.

## Powtórzenie

`powershell -NoProfile -ExecutionPolicy Bypass -File tests/run_performance.ps1`

Skrypt eksportuje wskazany commit bazowy, dodaje do niego ten sam kod pomiarowy i wykonuje trzy pary. Wyniki JSON trafiają do `data/performance`. Test jest lokalny i nie wywołuje modeli ani API kont.

`--benchmark <katalog-fixture> --animate-window` mierzy przez 15 sekund animowane okno na danych testowych, bez kolektorów sieciowych i indeksowania. Jego wynik opisuje tę scenę; nie zastępuje profilu wszystkich dostawców.
