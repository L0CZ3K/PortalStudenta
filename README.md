# UniAvio — Portal Studenta 🎓

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-.NET%20MAUI%20Blazor%20Hybrid-blue)](https://learn.microsoft.com/dotnet/maui/)
[![Targets](https://img.shields.io/badge/Targets-macOS%20%7C%20iOS%20%7C%20Android%20%7C%20Windows-lightgrey)](https://dotnet.microsoft.com/en-us/apps/maui)
[![Tests](https://img.shields.io/badge/Tests-397%20Passed-brightgreen?logo=xunit)](https://xunit.net/)
[![License: Showcase Only](https://img.shields.io/badge/License-Showcase%20%7C%20All%20Rights%20Reserved-critical.svg)](LICENSE)

> [!IMPORTANT]
> **Transparentność i cel publikacji kodu (Security & Privacy Audit):**  
> Kod źródłowy projektu został udostępniony publicznie w celu zapewnienia **100% przejrzystości (Transparency)** wobec społeczności akademickiej. Dzięki temu każdy student i pracownik uczelni może osobiście zweryfikować, że aplikacja nie wykrada poświadczeń ani nie szpieguje użytkowników.  
> **Na żadnym zewnętrznym serwerze NIE SĄ przechowywane wrażliwe dane studentów** (hasła, dane osobowe, oceny czy tokeny).  
> 
> *Uwaga prawna:* Upublicznienie kodu w celach wglądu i audytu bezpieczeństwa nie oznacza licencji open-source na jego kopiowanie czy redystrybucję. Wszelkie prawa zastrzeżone (**All Rights Reserved**). Szczegóły w pliku [LICENSE](LICENSE).

Celem projektu **UniAvio (Portal Studenta)** jest stworzenie kompleksowej, nowoczesnej i wieloplatformowej aplikacji hybrydowej (**.\NET MAUI Blazor Hybrid**) zaprojektowanej specjalnie dla studentów.

Aplikacja ma za zadanie zautomatyzować, uprościć i zebrać w jednym, spójnym miejscu wszystkie codzienne narzędzia niezbędne w życiu akademickim, łącząc oficjalne dane uczelniane z funkcjami społecznościowymi i organizacyjnymi.

---

## Główne cele i funkcje projektu

### 1. Integracja z systemem USOS
* Pobieranie i synchronizacja planu zajęć, ocen oraz informacji o sesji bezpośrednio z oficjalnych serwerów uczelni (np. Politechniki Śląskiej) z wykorzystaniem bezpiecznego protokołu uwierzytelniania **OAuth 1.0a** (podpis `HMAC-SHA1`).
* Architektura offline-first z 60-minutowym oknem pamięci podręcznej (TTL), która chroni serwery uczelniane przed przeciążeniem i zapewnia natychmiastowe ładowanie danych nawet przy braku internetu.

### 2. Zaawansowane zarządzanie planem zajęć (Timetable)
* **Wygodna prezentacja**: Przejrzyste widoki planu (siatka godzinowa, lista, widok dnia/tygodnia).
* **Parzystość tygodni**: Pełna obsługa tygodni A/B (parzystych i nieparzystych) z uwzględnieniem specyfiki roku akademickiego.
* **Modyfikacje na żywo**: Automatyczne nakładanie zmian od starosty (odwołane zajęcia, przeniesienia sal, dodatkowe terminy).
* **Eksport do kalendarza**: Generowanie plików `.ics` (RFC 5545) z bezpośrednią integracją z Kalendarzem Apple, Google Calendar oraz Microsoft Outlook.

### 3. Moduł Notatek Akademickich z synchronizacją zdjęć tablicy
* **Struktura przedmiotowa**: Tworzenie i organizowanie notatek w hierarchicznym drzewie folderów dopasowanym bezpośrednio do przedmiotów z planu USOS.
* **Bogaty edytor WYSIWYG**: Formatowanie tekstu, interaktywne checklisty zadań, zakreślacz oraz kolorowanie składni kodu w blokach dla języków programowania (C#, Python, SQL, JS, HTML itp.).
* **Bezprzewodowy transfer zdjęć (P2P Wi-Fi & Cloud)**: Błyskawiczne przesyłanie zdjęć tablicy lub slajdów bezpośrednio z aparatu telefonu do edytowanej notatki na komputerze — lokalnie przez sieć Wi-Fi (wbudowany serwer HTTP) lub automatycznie przez Firebase przy innych podsieciach.
* **Filtr tablicy (`WhiteboardImageOptimizer`)**: Automatyczne wyrównywanie balansu bieli i wyciąganie kontrastu pisma markerem, redukujące wagę pliku i poprawiające czytelność.
* **Wydajność bazy SQLite**: Zdjęcia i ciężkie załączniki są odseparowane od kodu HTML notatki (tokenizacja `data-attachment-id`), co gwarantuje płynne pisanie bez zamrażania interfejsu.

### 4. Panel Starosty i Administratora (Cohort & Group Management)
* **Zarządzanie rocznikiem**: Narzędzie umożliwiające staroście lub administratorowi grupy masowe odwoływanie zajęć, aktualizowanie numerów sal i wprowadzanie zastępstw w czasie rzeczywistym.
* **Tablica ogłoszeń**: Publikowanie pilnych komunikatów z priorytetami (*Ważne*, *Kolokwium*, *Egzamin*, *Zwykłe*).
* **Ankiety grupowe**: Błyskawiczne zbieranie głosów (np. w sprawie przeniesienia terminu kolokwium) z weryfikacją unikalności głosu po identyfikatorze studenta.

### 5. Wieloplatformowość (Cross-Platform) & Apple HIG UX
* Działanie jako natywna aplikacja na systemy **macOS (MacCatalyst), iOS, Android** oraz **Windows**.
* Nowoczesny, dopracowany interfejs użytkownika inspirowany wytycznymi **Apple Human Interface Guidelines (HIG)**: obsługa motywu ciemnego (OLED Dark Mode), responsywne panele boczne, mobilny pasek nawigacyjny, modalne arkusze (bottom sheets) oraz haptyka.

---

## Architektura i struktura projektu

Aplikacja wykorzystuje hybrydowy model **.NET MAUI + Blazor WebView**. Kod logiki biznesowej i bazy danych jest w 100% współdzielony w C#, natomiast interfejs użytkownika renderowany jest za pośrednictwem zoptymalizowanych komponentów Blazor.

```text
PortalStudenta/
├── Portal Studenta/               # Główny projekt aplikacji (.NET 10 MAUI Blazor Hybrid)
│   ├── Components/                # Komponenty Blazor (Warstwa Prezentacji)
│   │   ├── Pages/                 
│   │   │   ├── Schedule.razor     # Plan zajęć, siatka, tryb God Mode, eksport .ics
│   │   │   ├── Notes.razor        # Edytor notatek, drzewo folderów, obsługa zdjęć
│   │   │   ├── Starosta.razor     # Panel starosty: modyfikacje planu, ankiety
│   │   │   ├── Announcements.razor# Tablica ogłoszeń z filtrowaniem i priorytetami
│   │   │   ├── Profile.razor      # Profil studenta, powiązanie z USOS, preferencje
│   │   │   └── Login.razor        # Uwierzytelnianie OAuth 1.0a USOS
│   │   └── Layout/                # MainLayout, pasek nawigacji i widżet LiveCountdown
│   │
│   ├── Services/                  # Logika biznesowa i integracje
│   │   ├── UsosApiService.cs      # Klient HTTP USOS API z podpisem kryptograficznym
│   │   ├── UsosAuthService.cs     # Trójstronny przepływ autoryzacji OAuth 1.0a
│   │   ├── ScheduleService.cs     # Parsowanie planu USOS i 60-minutowy cache
│   │   ├── NoteService.cs         # Baza SQLite dla notatek, folderów i załączników
│   │   ├── WhiteboardImageOptimizer.cs # Algorytmy przetwarzania zdjęć tablic
│   │   ├── LocalNetworkCameraServer.cs # Lokalny serwer HTTP P2P do odbioru zdjęć
│   │   ├── FirebaseService.cs     # Realtime Database, sanityzacja kluczy, synchronizacja
│   │   ├── CalendarExportService.cs # Generator kalendarza iCalendar (.ics, RFC 5545)
│   │   └── UsosSecrets.Local.cs.example # Szablon prywatnych kluczy USOS API
│   │
│   ├── Platforms/                 # Natywny kod platformowy (iOS, MacCatalyst, Android, Windows)
│   └── wwwroot/                   # Style CSS, motywy, czcionki oraz notes-editor.js
│
├── PortalStudenta.Tests/          # Zestaw 397 testów jednostkowych (xUnit)
│   ├── UsosOAuth1SignatureTests.cs # Testy sygnatur HMAC-SHA1 i normalizacji URL
│   ├── UsosDataParsingAndCacheTests.cs # Testy parsera planu i polityki cache
│   ├── ConcurrencyAndRaceConditionTests.cs # Testy wielowątkowości i SemaphoreSlim
│   ├── SecurityAndChaosPayloadTests.cs # Testy odporności na fuzzing i ataki XSS/NoSQL
│   ├── NoteServiceCrudAndFolderTests.cs # Testy integralności bazy SQLite i załączników
│   └── CalendarExportIcsTests.cs   # Walidacja standardu RFC 5545
│
└── .github/workflows/ci.yml       # Automatyczna integracja ciągła (CI)
```

---

## Bezpieczeństwo i architektura prywatności

### 🛡️ Zasada Zero-Data-Harvesting
* **Brak serwerów pośredniczących**: Aplikacja komunikuje się **bezpośrednio** z oficjalnym serwerem USOS uczelni użytkownika. Pomiędzy aplikacją a uczelnią nie istnieje żaden prywatny backend, serwer proxy ani usługa agregująca dane.
* **Bezpieczne logowanie OAuth 1.0a**: Hasła i loginy studenta są wprowadzane wyłącznie w oficjalnym formularzu CAS/USOSapps uczelni w oknie przeglądarki. Aplikacja nigdy nie ma dostępu do Twojego hasła i otrzymuje jedynie ograniczony token autoryzacyjny.
* **Brak wrażliwych danych na serwerach zewnętrznych**:
  * Oceny, dane osobowe, plan zajęć oraz notatki są zapisywane **wyłącznie lokalnie** na urządzeniu studenta (w chronionym magazynie `Preferences` i lokalnej bazie SQLite).
  * Chmura Firebase Realtime Database jest wykorzystywana **wyłącznie** do anonimowej synchronizacji społecznościowej (ogłoszenia starosty, anonimowe ankiety terminów, hash identyfikatora grupy). Żadne dane osobowe, numery PESEL, hasła ani oceny nie są tam wysyłane.
* **Lokalny transfer zdjęć tablicy (P2P Wi-Fi)**: Bezprzewodowe przesyłanie zdjęć z telefonu na komputer odbywa się bezpośrednio w obrębie Twojej domowej lub uczelnianej sieci Wi-Fi bez angażowania serwerów zewnętrznych.

### ⚙️ Standardy inżynierskie
* **Dual-Platform & Zero Warnings**: Weryfikacja obu głównych platform docelowych (`net10.0-maccatalyst` oraz `net10.0-ios`) przy zerowej tolerancji na ostrzeżenia kompilatora (`-warnaserror`).
* **Stabilność wątkowa Blazora**: Pełne zabezpieczenie operacji asynchronicznych — event handlery z blokami `try-catch`, modyfikacje interfejsu delegowane przez `InvokeAsync(StateHasChanged)` lub `MainThread.BeginInvokeOnMainThread`.
* **Współbieżność bazy danych**: Operacje na bazie SQLite chronione asynchronicznymi semaforami `SemaphoreSlim(1, 1)`, co zapobiega wyścigom wątków (race conditions).
* **Sanityzacja danych**: Automatyczne czyszczenie kluczy w Firebase Realtime Database (`FirebaseService.SanitizeKey`) zapobiegające błędom NoSQL injection.
* **Pakiet 397 testów jednostkowych**: Pełne pokrycie testami regresyjnymi, odpornością na skrajne odpowiedzi USOS, strefami czasowymi (DST) oraz atakami fuzzing/XSS.

---

## Licencja i prawa autorskie

Wszelkie prawa zastrzeżone (**All Rights Reserved**). Kod źródłowy udostępniony jest w serwisie GitHub wyłącznie w celach wglądu i audytu bezpieczeństwa. Kopiowanie, modyfikowanie, redystrybucja, uruchamianie w celach komercyjnych bądź produkcyjnych bez uprzedniej pisemnej zgody autora jest zabronione. Szczegółowe warunki prawne znajdują się w pliku [LICENSE](LICENSE).