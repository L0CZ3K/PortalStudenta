# UniAvio — Portal Studenta 🎓

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-.NET%20MAUI%20Blazor%20Hybrid-blue)](https://learn.microsoft.com/dotnet/maui/)
[![Targets](https://img.shields.io/badge/Targets-macOS%20%7C%20iOS%20%7C%20Android%20%7C%20Windows-lightgrey)](https://dotnet.microsoft.com/en-us/apps/maui)
[![Tests](https://img.shields.io/badge/Tests-397%20Passed-brightgreen?logo=xunit)](https://xunit.net/)
[![License: Showcase Only](https://img.shields.io/badge/License-Showcase%20%7C%20All%20Rights%20Reserved-critical.svg)](LICENSE)

> [!IMPORTANT]
> **Transparentność i cel publikacji kodu (Security & Privacy Audit):**  
> Kod źródłowy projektu został udostępniony publicznie w celu zapewnienia **100% przejrzystości (Transparency)** wobec społeczności akademickiej. Dzięki temu każdy student i pracownik uczelni może osobiście zweryfikować, że aplikacja nie wykrada poświadczeń ani nie szpieguje użytkowników.  
> **Na żadnym zewnętrznym serwerze NIE SĄ przechowywane wrażliwe dane osobowe studentów** (hasła, numery PESEL, oceny czy tokeny dostępowe USOS).  
> 
> *Uwaga prawna:* Upublicznienie kodu w celach wglądu i audytu bezpieczeństwa nie oznacza licencji open-source na jego kopiowanie czy redystrybucję. Wszelkie prawa zastrzeżone (**All Rights Reserved**). Szczegóły w pliku [LICENSE](LICENSE).

Celem projektu **UniAvio (Portal Studenta)** jest stworzenie kompleksowej, nowoczesnej i wieloplatformowej aplikacji hybrydowej (**.NET 10 MAUI Blazor Hybrid**) zaprojektowanej specjalnie dla studentów.

Aplikacja automatyzuje, upraszcza i zbiera w jednym, spójnym miejscu wszystkie codzienne narzędzia niezbędne w życiu akademickim, łącząc oficjalne dane uczelniane z funkcjami społecznościowymi, organizacyjnymi oraz rozbudowanym środowiskiem do tworzenia notatek.

---

## Główne cele i funkcje projektu

### 1. Integracja z systemem USOS
* Pobieranie i synchronizacja planu zajęć, grup oraz informacji o sesji bezpośrednio z oficjalnych serwerów uczelni (np. Politechniki Śląskiej) z wykorzystaniem protokołu uwierzytelniania **OAuth 1.0a** (podpis kryptograficzny `HMAC-SHA1`).
* Architektura **Offline-First** z 60-minutowym oknem pamięci podręcznej (`ScheduleService.IsCacheFresh`), która chroni serwery uczelniane przed przeciążeniem i zapewnia błyskawiczne ładowanie danych nawet przy braku połączenia z internetem.

### 2. Zaawansowane zarządzanie planem zajęć (Timetable)
* **Wygodna prezentacja**: Przejrzyste widoki planu (siatka godzinowa dopasowana do ekranu, lista, widok dnia/tygodnia).
* **Parzystość tygodni**: Pełna obsługa tygodni A/B (parzystych i nieparzystych) z uwzględnieniem kalendarza roku akademickiego.
* **Modyfikacje na żywo**: Automatyczne nakładanie zmian od starosty (odwołane zajęcia, przeniesienia sal, dodatkowe terminy i kolokwia).
* **Eksport do kalendarza**: Generowanie plików `.ics` (zgodnych ze standardem RFC 5545) z bezpośrednią integracją z Kalendarzem Apple, Google Calendar oraz Microsoft Outlook.

### 3. Moduł Notatek Akademickich, Zdalny Aparat i Skrypty PDF
* **Struktura przedmiotowa z USOS**: Automatyczne organizowanie notatek w hierarchicznym drzewie folderów (*Przedmiot ➔ Typ zajęć (WYK/LAB/CW/PROJ) ➔ Grupa*) budowanym dynamicznie na podstawie planu zajęć.
* **Bogaty edytor WYSIWYG**: Formatowanie tekstu, nagłówki, interaktywne checklisty zadań, zakreślacz oraz kolorowanie składni kodu w blokach dla języków programowania (C#, Python, SQL, JS, HTML, C++).
* **Bezprzewodowy transfer zdjęć tablicy (P2P Wi-Fi & Cloud Fallback)**: Błyskawiczne przesyłanie zdjęć tablicy lub slajdów bezpośrednio z aparatu telefonu do edytowanej notatki na komputerze (macOS / Windows) — lokalnie przez sieć Wi-Fi (<50 ms, wbudowany serwer TCP/HTTP) lub automatycznie przez chmurę przy pracy w różnych podsieciach.
* **Sprzętowa optymalizacja tablic (`WhiteboardImageOptimizer`)**: Automatyczne skalowanie (`1800px`) i kompresja zdjęć tablicy (~180 KB) wraz z generowaniem miniatur (`280px`), zapewniające wysoką czytelność wzorów przy minimalnym zużyciu pamięci.
* **Eksport PDF i „Skrypt przed sesją” (`PdfExportService`)**: Generowanie pojedynczych notatek do PDF oraz kompilacja **wszystkich notatek z danego przedmiotu w jeden zbiorczy skrypt PDF** z okładką, automatycznym spisem treści i osadzonymi zdjęciami tablicy.
* **3 tryby prywatności i synchronizacja Delta Sync**:
  * *📱 Tylko na urządzeniu (`LocalOnly`)* — notatka nigdy nie opuszcza pamięci lokalnej urządzenia.
  * *🔄 Moje urządzenia (`BetweenDevices`)* — synchronizacja pomiędzy telefonem a komputerem studenta.
  * *👥 Udostępnione grupie (`SharedWithAll`)* — współdzielenie notatki z rocznikiem wraz z przełącznikiem **„🛡️ Ochrona”** (*Tylko do odczytu dla grupy*), który chroni autorskie notatki przed nadpisaniem lub usunięciem przez innych członków grupy.
* **Kosz 7-dniowy (Soft-Delete)**: Usunięte notatki trafiają do specjalnego folderu *Kosz* na 7 dni z możliwością przywrócenia jednym kliknięciem przed trwałym wyczyszczeniem.

### 4. Globalna Wyszukiwarka (`⌘K` / `Ctrl+K`)
* Dostępne z każdego miejsca w aplikacji okno szybkiego wyszukiwania (`GlobalSearchModal`), które w czasie rzeczywistym przeszukuje jednocześnie:
  * tytuły, przedmioty i treść wszystkich notatek,
  * zajęcia w planie, nazwiska prowadzących oraz numery sal wykładowych,
  * komunikaty i ogłoszenia grupowe.

### 5. Panel Starosty i Administratora (Cohort & Group Management)
* **Zarządzanie rocznikiem**: Narzędzie umożliwiające staroście lub administratorowi grupy odwoływanie zajęć, aktualizowanie numerów sal i wprowadzanie zastępstw w czasie rzeczywistym.
* **Tablica ogłoszeń**: Publikowanie pilnych komunikatów z priorytetami (*Ważne*, *Kolokwium*, *Egzamin*, *Zwykłe*).
* **Ankiety grupowe**: Błyskawiczne zbieranie głosów (np. w sprawie wyboru terminu kolokwium) z weryfikacją unikalności głosu.

### 6. Wieloplatformowość (Cross-Platform) & Apple HIG UX
* Działanie jako natywna aplikacja na systemy **macOS (MacCatalyst), iOS, Android** oraz **Windows (WinUI 3 / WebView2)**.
* Nowoczesny interfejs użytkownika inspirowany wytycznymi **Apple Human Interface Guidelines (HIG)**: obsługa motywu ciemnego (OLED Dark Mode) i jasnego, dynamiczne kolory akcentów, responsywne panele boczne, mobilny pasek nawigacyjny oraz dwujęzyczny interfejs (**PL / EN**).

---

## Architektura i struktura projektu

Aplikacja wykorzystuje hybrydowy model **.NET 10 MAUI + Blazor WebView**. Logika biznesowa, synchronizacja oraz obsługa bazy danych SQLite są w 100% współdzielone w C#, natomiast warstwa prezentacji korzysta z komponentów Razor i natywnych API poszczególnych platform.

```text
PortalStudenta/
├── Portal Studenta/               # Główny projekt aplikacji (.NET 10 MAUI Blazor Hybrid)
│   ├── Components/                # Komponenty Blazor (Warstwa Prezentacji)
│   │   ├── Pages/                 
│   │   │   ├── Home.razor         # Dashboard z odliczaniem do zajęć i podsumowaniem dnia
│   │   │   ├── Schedule.razor     # Plan zajęć, siatka, tryb God Mode, eksport .ics
│   │   │   ├── Notes.razor        # Edytor notatek, drzewo folderów, zdalny aparat, Kosz, PDF
│   │   │   ├── Admin.razor        # Panel starosty i admina: modyfikacje planu, ankiety
│   │   │   ├── Announcements.razor# Tablica ogłoszeń z filtrowaniem i priorytetami
│   │   │   ├── Profile.razor      # Profil studenta, powiązanie z USOS, preferencje
│   │   │   └── Login.razor        # Uwierzytelnianie OAuth 1.0a USOS
│   │   └── Layout/                
│   │       ├── MainLayout.razor   # Główny układ nawigacji, powiadomienia Toast, skrót ⌘K
│   │       └── GlobalSearchModal.razor # Globalna wyszukiwarka (Notatki / Plan / Ogłoszenia)
│   │
│   ├── Services/                  # Logika biznesowa, bezpieczeństwo i integracje
│   │   ├── UsosApiService.cs      # Klient HTTP USOS API z podpisem kryptograficznym
│   │   ├── UsosAuthService.cs     # Trójstronny przepływ OAuth 1.0a + SecureStorage / AES-256
│   │   ├── UsosSecrets.cs         # Dekoder kluczy XOR na stosie z czyszczeniem ZeroMemory
│   │   ├── UsosSecrets.Local.cs.example # Szablon lokalnych kluczy USOS API (ignorowany w Git)
│   │   ├── ScheduleService.cs     # Parsowanie planu USOS i 60-minutowy cache dyskowy
│   │   ├── NoteService.cs         # Baza SQLite, Delta Sync, Lazy-Loading zdjęć i Kosz 7-dniowy
│   │   ├── PdfExportService.cs    # Generator notatek i zbiorczych skryptów przedmiotowych PDF
│   │   ├── WhiteboardImageOptimizer.cs # Sprzętowa optymalizacja i kompresja zdjęć tablic
│   │   ├── LocalNetworkCameraServer.cs # Lokalny serwer P2P Wi-Fi z autoryzacją tokenem 192-bit
│   │   ├── FirebaseService.cs     # Realtime Database, sanitaryzacja kluczy, Delta Sync
│   │   └── CalendarExportService.cs # Generator kalendarza iCalendar (.ics, RFC 5545)
│   │
│   ├── Platforms/                 # Natywny kod platformowy (iOS, MacCatalyst, Android, Windows)
│   └── wwwroot/                   # Style CSS (app.css, css/notes.css) oraz notes-editor.js
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

### 🛡️ Zasada Zero-Data-Harvesting & Kontrola Prywatności
* **Brak serwerów pośredniczących dla USOS**: Aplikacja komunikuje się **bezpośrednio** z oficjalnym serwerem USOS uczelni użytkownika po szyfrowanym łączu HTTPS.
* **Bezpieczne logowanie OAuth 1.0a i `SecureStorage`**: Hasła studenta są wprowadzane wyłącznie w oficjalnym formularzu CAS/USOSapps uczelni. Tokeny dostępowe OAuth są przechowywane w sprzętowym pęku kluczy systemu operacyjnego (**Apple Keychain** na iOS/macOS, **Android Keystore** na Androidzie lub zaszyfrowanym magazynie **AES-256** na Windows).
* **Ochrona kluczy w binariach (`UsosSecrets`)**: Klucze klienckie USOS nie są przechowywane jako jawne literały tekstowe (`string`) w skompilowanym pliku `.dll` — są zaciemnione tablicą bajtów XOR i dekodowane wyłącznie na czas podpisywania żądania na stosie (`stackalloc byte`) z natychmiastowym zerowaniem pamięci (`CryptographicOperations.ZeroMemory`). Architektura wspiera ponadto opcjonalny zewnętrzny *OAuth Signing Proxy*.
* **Pełna kontrola nad danymi i notatkami**:
  * Dane profilu USOS oraz lokalna baza SQLite są zapisywane na urządzeniu studenta.
  * W module notatek student sam decyduje o poziomie prywatności każdej notatki: może przechowywać ją wyłącznie offline na urządzeniu (`LocalOnly`), synchronizować między własnymi urządzeniami (`BetweenDevices`) lub udostępnić grupie (`SharedWithAll`).
* **Kryptograficznie zabezpieczony serwer P2P Wi-Fi (`LocalNetworkCameraServer`)**: Lokalny transfer zdjęć tablicy między telefonem a komputerem wymaga jednorazowego, 192-bitowego tokenu sesyjnego (`RandomNumberGenerator`) przesyłanego w nagłówku `X-Camera-Token` i weryfikowanego w czasie stałym (`CryptographicOperations.FixedTimeEquals`), z twardym limitem rozmiaru strumienia (ochrona przed atakami DoS w sieci uczelnianej).

### ⚙️ Standardy inżynierskie i Defense-in-Depth
* **Trójwarstwowa ochrona przed XSS (Defense-in-Depth)**:
  1. *Warstwa zapisu C# (`NoteService.StripHeavyDataUris`)* — wycinanie tagów wykonywalnych (`<script>`, `<iframe>`, `<object>`, `<embed>`), atrybutów `on*=` oraz pseudoprotokołów `javascript:` przed zapisem do SQLite i po pobraniu z chmury.
  2. *Warstwa DOM JS (`notesEditor.sanitizeHtml`)* — parsowanie HTML przez izolowany `DOMParser` przed renderowaniem w edytorze.
  3. *Warstwa eksportu (`PdfExportService.SanitizeNoteHtml`)* — czyszczenie kodu HTML i kodowanie metadanych (`WebUtility.HtmlEncode`) przed generowaniem dokumentów PDF.
* **Ochrona przed Path Traversal**: Ścisła walidacja identyfikatorów załączników oraz biała lista dozwolonych rozszerzeń plików (`.jpg`, `.jpeg`, `.png`, `.webp`, `.heic`, `.pdf`) podczas synchronizacji z chmurą.
* **Wymuszenie szyfrowania transportu (TLS/HTTPS)**: Całkowita blokada nieszyfrowanego ruchu HTTP w konfiguracjach platform (`NSAllowsArbitraryLoads = false` w `Info.plist` oraz `usesCleartextTraffic = false` w `AndroidManifest.xml`).
* **Wydajność Delta Sync & Lazy-Loading**: Synchronizacja chmurowa weryfikuje 8-bajtowe znaczniki czasu (`lastModifiedTicks`) przed pobraniem danych, a zdjęcia są odseparowane od drzewa JSON notatek i dociągane z chmury dopiero na żądanie.
* **Dual-Platform Verification & Zero Warnings**: Kompilacja na platformy docelowe (`net10.0-maccatalyst` oraz `net10.0-ios`) z wymuszoną flagą `-warnaserror` (0 ostrzeżeń, 0 błędów) oraz pakiet **397 testów jednostkowych xUnit**.

---

## Weryfikacja i budowanie projektu (Dla Audytorów i Deweloperów)

1. **Konfiguracja kluczy deweloperskich USOS API:**  
   Skopiuj plik szablonu `Portal Studenta/Services/UsosSecrets.Local.cs.example` jako `Portal Studenta/Services/UsosSecrets.Local.cs` (plik jest wykluczony w `.gitignore`).

2. **Kompilacja z weryfikacją zerowej liczby ostrzeżeń:**
   ```bash
   dotnet build "Portal Studenta/Portal Studenta.csproj" -f net10.0-maccatalyst -warnaserror
   dotnet build "Portal Studenta/Portal Studenta.csproj" -f net10.0-ios -warnaserror
   ```

3. **Uruchomienie zestawu 397 testów jednostkowych i bezpieczeństwa:**
   ```bash
   dotnet test PortalStudenta.Tests/PortalStudenta.Tests.csproj
   ```

---

## Licencja i prawa autorskie

Wszelkie prawa zastrzeżone (**All Rights Reserved**). Kod źródłowy udostępniony jest w serwisie GitHub wyłącznie w celach wglądu i audytu bezpieczeństwa. Kopiowanie, modyfikowanie, redystrybucja, uruchamianie w celach komercyjnych bądź produkcyjnych bez uprzedniej pisemnej zgody autora jest zabronione. Szczegółowe warunki prawne znajdują się w pliku [LICENSE](LICENSE).