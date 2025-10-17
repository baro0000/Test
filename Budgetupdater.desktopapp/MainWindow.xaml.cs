using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;
using Test; // Twój projekt z klasami (Transaction, BudgetUpdater, CategoryMenager, itp.)

namespace BudgetUpdater.DesktopApp
{
    public partial class MainWindow : Window
    {
        private string? _csvPath;
        private string? _excelPath;
        private CoreWebView2? _core;
        private readonly BudgetInterop _interop;

        // Do komunikacji z webview przy oczekiwaniu na klasyfikację
        private TaskCompletionSource<JsonElement>? _classificationTcs;

        public MainWindow()
        {
            InitializeComponent();
            _interop = new BudgetInterop(AppendLog, RefreshUiWithData);
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                await WebView.EnsureCoreWebView2Async();
                _core = WebView.CoreWebView2;
                var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
                _core.SetVirtualHostNameToFolderMapping("appassets", folder, CoreWebView2HostResourceAccessKind.Allow);
                _core.NavigationCompleted += Core_NavigationCompleted;
                _core.WebMessageReceived += Core_WebMessageReceived;
                WebView.Source = new Uri("https://appassets/index.html");
            }
            catch (Exception ex)
            {
                AppendLog($"Błąd WebView2 init: {ex.Message}");
            }
        }

        private void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e) { }

        private void Core_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                var doc = JsonSerializer.Deserialize<JsonElement>(json);
                if (!doc.TryGetProperty("cmd", out var cmd)) return;
                var command = cmd.GetString();
                switch (command)
                {
                    case "chooseCsv":
                        BtnChooseCsv_Click(null!, null!);
                        break;
                    case "chooseExcel":
                        BtnChooseExcel_Click(null!, null!);
                        break;
                    case "loadTransactions":
                        _ = HandleLoadTransactions();
                        break;
                    case "updateBudget":
                        _ = HandleFullUpdate();
                        break;
                    case "getLog":
                        SendLogToWeb();
                        break;
                    case "classifyResult":
                        // Odebrano wynik klasyfikacji z UI
                        if (_classificationTcs != null)
                        {
                            _classificationTcs.TrySetResult(doc);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Error handling message: {ex.Message}");
            }
        }

        private void SendLogToWeb()
        {
            var json = JsonSerializer.Serialize(new { cmd = "log", text = LogBox.Text });
            _core?.PostWebMessageAsJson(json);
        }

        private void RefreshUiWithData(object? data) { /* opcjonalnie push do webview */ }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
                LogBox.ScrollToEnd();
            });
        }

        // --- Flow centralny: wczytanie + klasyfikacja (bez aktualizacji) ---
        private async Task HandleLoadTransactions()
        {
            if (string.IsNullOrWhiteSpace(_csvPath))
            {
                AppendLog("Wybierz plik CSV najpierw.");
                return;
            }

            try
            {
                // 1) Wczytaj transakcje z CSV
                var loader = new Test.LoadDataFromFile();
                var txs = loader.LoadData(_csvPath);
                AppendLog($"Wczytano {txs.Count} transakcji z pliku.");

                // 2) Stwórz menagera reguł i sprawdź automatyczne dopasowanie
                var cm = new Test.CategoryMenager();
                var unknowns = new List<(int idx, Transaction t)>();
                for (int i = 0; i < txs.Count; i++)
                {
                    var t = txs[i];
                    var cat = cm.GetCategoryForTransaction(t);
                    if (cat != null)
                    {
                        t.Kategoria = cat;
                    }
                    else
                    {
                        unknowns.Add((i, t));
                    }
                }

                // 3) Jeśli są nieznane transakcje - poproś UI o ręczną klasyfikację
                if (unknowns.Count > 0)
                {
                    AppendLog($"Znaleziono {unknowns.Count} nieznanych transakcji, wymagających klasyfikacji manualnej.");
                    var categories = Enum.GetNames(typeof(CategoryName)).ToArray(); // przekaz listę dostępnych kategorii
                    var itemsForUi = unknowns.Select(u => new {
                        idx = u.idx,
                        date = u.t.DataTransakcji,
                        recipient = u.t.Odbiorca,
                        opis = u.t.Opis,
                        kwota = u.t.Kwota
                    }).ToList();

                    var payload = JsonSerializer.Serialize(new { cmd = "classify", items = itemsForUi, categories });
                    _core?.PostWebMessageAsJson(payload);

                    // 3a) teraz poczekaj asynchronicznie na odpowiedź z UI (classifyResult)
                    _classificationTcs = new TaskCompletionSource<JsonElement>();
                    var resultDoc = await _classificationTcs.Task; // czekamy na wynik przesłany przez JS

                    // resultDoc powinien zawierać property "mappings" - tablica { idx, category, applyToAll, keyword }
                    if (resultDoc.TryGetProperty("mappings", out var mappings))
                    {
                        foreach (var m in mappings.EnumerateArray())
                        {
                            int idx = m.GetProperty("idx").GetInt32();
                            string selectedCategory = m.GetProperty("category").GetString() ?? "";
                            bool applyToAll = m.GetProperty("applyToAll").GetBoolean();
                            string keyword = m.TryGetProperty("keyword", out var k) ? (k.GetString() ?? "") : "";

                            if (idx < 0 || idx >= txs.Count) continue;
                            var tx = txs[idx];

                            // zbuduj obiekt Category zgodnie z Twoją klasą Category
                            // Tutaj zakładamy enum CategoryName i klasę Category { public CategoryName Name; public string Type; }
                            if (Enum.TryParse(typeof(CategoryName), selectedCategory, true, out var enumVal))
                            {
                                var cat = new Category();
                                cat.Name = (CategoryName)enumVal;
                                // ustaw typ na podstawie kwoty
                                cat.Type = tx.Kwota >= 0 ? "Uznanie" : "Obciążenie";

                                // jeśli applyToAll -> zapisz regułę w menagerze
                                if (applyToAll)
                                {
                                    string key = string.IsNullOrWhiteSpace(keyword) ? (tx.Odbiorca ?? tx.Opis ?? "") : keyword;
                                    cm.AddRule(key.ToLowerInvariant(), cat, true);
                                    AppendLog($"Zapisano regułę: '{key}' -> {cat.Name}");
                                }

                                // przypisz kategorię do transakcji
                                tx.Kategoria = cat;
                            }
                            else
                            {
                                AppendLog($"Niepoprawna nazwa kategorii: {selectedCategory}");
                            }
                        } // foreach mapping
                    } // if mappings
                    else
                    {
                        AppendLog("Brak mapowań w odpowiedzi klasyfikacji.");
                    }

                    _classificationTcs = null;
                }

                // Na koniec wyślij do webview listę transakcji już z kategoriami (do przeglądu)
                var items = txs.Select(t => new {
                    date = t.DataTransakcji,
                    recipient = t.Odbiorca,
                    opis = t.Opis,
                    kwota = t.Kwota,
                    category = t.Kategoria?.Name.ToString() ?? ""
                }).ToList();
                var payload2 = JsonSerializer.Serialize(new { cmd = "transactionsLoaded", items });
                _core?.PostWebMessageAsJson(payload2);

            }
            catch (Exception ex)
            {
                AppendLog($"Błąd wczytywania: {ex.Message}");
            }
        }

        // --- Pełne uruchomienie aktualizacji (sprawdź reguły, journal, i aktualizuj excel) ---
        private async Task HandleFullUpdate()
        {
            if (string.IsNullOrWhiteSpace(_excelPath))
            {
                AppendLog("Wybierz plik budżetu Excel najpierw.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_csvPath))
            {
                AppendLog("Wybierz plik CSV najpierw.");
                return;
            }

            try
            {
                // Wczytaj i zaklasyfikuj (wywołaj tę samą logikę, ale bez blokującego UI)
                await HandleLoadTransactions();

                // Wczytaj transakcje ponownie, teraz już z kategoriami ustawionymi
                var loader = new Test.LoadDataFromFile();
                var transactions = loader.LoadData(_csvPath);

                // Jeżeli nadal występują transakcje bez kategorii, przerwij i powiadom
                var missing = transactions.Where(t => t.Kategoria == null).ToList();
                if (missing.Any())
                {
                    AppendLog($"Są nadal niezaklasyfikowane transakcje ({missing.Count}). Proszę je zaklasyfikować przed aktualizacją.");

                    return;
                }

                // Uruchom aktualizację budżetu (w wątku tła)
                var updater = new Test.BudgetUpdater(_excelPath);
                AppendLog("Rozpoczynam aktualizację budżetu (to może chwilę potrwać)...");
                await Task.Run(() => updater.UpdateBudget(transactions));
                AppendLog("Aktualizacja zakończona.");
            }
            catch (Exception ex)
            {
                AppendLog($"Błąd aktualizacji budżetu: {ex.Message}");
            }
        }

        // --- przyciski UI ---
        private void BtnChooseCsv_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = "CSV Files|*.csv|All files|*.*";
            if (ofd.ShowDialog() == true)
            {
                _csvPath = ofd.FileName;
                TxtCsvPath.Text = _csvPath;
                AppendLog($"Wybrano CSV: {_csvPath}");
            }
        }

        private void BtnChooseExcel_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = "Excel Files|*.xlsx;*.xlsm;*.xls|All files|*.*";
            if (ofd.ShowDialog() == true)
            {
                _excelPath = ofd.FileName;
                TxtExcelPath.Text = _excelPath;
                AppendLog($"Wybrano plik budżetu: {_excelPath}");
            }
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            _ = HandleLoadTransactions();
        }

        private void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            _ = HandleFullUpdate();
        }
    }
}
