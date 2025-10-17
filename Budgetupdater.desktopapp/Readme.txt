BudgetUpdater Desktop App - BudzetDomowy

1) Rozpakuj folder 'BudzetDomowy_desktop' obok folderu 'Test' (Twojego projektu).
   Struktura powinna wyglądać tak:
     /your-solution-folder/
       /Test/  <-- Twój istniejący projekt
       /BudzetDomowy_desktop/
         BudgetUpdater.DesktopApp.csproj
         App.xaml
         MainWindow.xaml
         wwwroot/...
2) Otwórz solution w Visual Studio i dodaj projekt BudgetUpdater.DesktopApp (jeśli chcesz).
   Upewnij się, że w BudgetUpdater.DesktopApp.csproj referencja do Test csproj jest poprawna (..\Test\Test.csproj).
3) Zainstaluj WebView2 runtime jeśli nie masz (Edge WebView2).

Uruchamianie:
- W Visual Studio: uruchom projekt BudgetUpdater.DesktopApp.
- Po zbudowaniu, w folderze bin\Debug\net8.0-windows\ pojawi się BudżetDomowy.exe
- Dwuklik uruchamia aplikację.
