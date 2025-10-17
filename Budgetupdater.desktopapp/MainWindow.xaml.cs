using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;
using Test;

namespace BudgetUpdater.DesktopApp
{
    public partial class MainWindow : Window
    {
        private string? _csvPath;
        private string? _excelPath;
        private CoreWebView2? _core;
        private TaskCompletionSource<JsonElement>? _classificationTcs;

        public MainWindow()
        {
            InitializeComponent();
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                await WebView.EnsureCoreWebView2Async();
                _core = WebView.CoreWebView2;

                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
                _core.SetVirtualHostNameToFolderMapping("appassets", folder, CoreWebView2HostResourceAccessKind.Allow);

                _core.NavigationCompleted += Core_NavigationCompleted;
                _core.WebMessageReceived += Core_WebMessageReceived;

                WebView.Source = new Uri("https://appassets/index.html");

                AppendLog("Interfejs przeglądarkowy został uruchomiony.");
            }
            catch (Exception ex)
            {
                AppendLog($"Błąd inicjalizacji WebView2: {ex.Message}");
            }
        }

        private void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            AppendLog("Załadowano interfejs użytkownika.");
        }

        private void Core_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var doc = JsonDocument.Parse(e.WebMessageAsJson).RootElement;
                if (!doc.TryGetProperty("cmd", out var cmd)) return;
                var command = cmd.GetString();

                switch (command)
                {
                    case "chooseCsv": BtnChooseCsv_Click(null!, null!); break;
                    case "chooseExcel": BtnChooseExcel_Click(null!, null!); break;
                    case "loadTransactions": _ = HandleLoadTransactions(); break;
                    case "updateBudget": _ = HandleFullUpdate(); break;
                    case "getLog": SendLogToWeb(); break;
                    case "classifyResult":
                        _classificationTcs?.TrySetResult(doc);
                        break;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Błąd przetwarzania wiadomości: {ex.Message}");
            }
        }

        private void SendLogToWeb()
        {
            if (_core == null) return;
            var json = JsonSerializer.Serialize(new { cmd = "log", text = LogBox.Text });
            _core.PostWebMessageAsJson(json);
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogBox.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
                LogBox.ScrollToEnd();
            });
        }

        // ==================== GŁÓWNA LOGIKA ====================

        private async Task HandleLoadTransactions()
        {
            if (string.IsNullOrWhiteSpace(_csvPath))
            {
                AppendLog("Najpierw wybierz plik CSV.");
                return;
            }

            try
            {
                var loader = new LoadDataFromFile();
                var txs = loader.LoadData(_csvPath);
                AppendLog($"Wczytano {txs.Count} transakcji.");

                var cm = new CategoryMenager();
                var unknowns = new List<int>();

                for (int i = 0; i < txs.Count; i++)
                {
                    var t = txs[i];
                    var cat = cm.GetCategoryForTransaction(t);
                    if (cat != null)
                        t.Kategoria = cat;
                    else
                        unknowns.Add(i);
                }

                if (unknowns.Count > 0)
                {
                    AppendLog($"Znaleziono {unknowns.Count} nieznanych transakcji — wymagana klasyfikacja ręczna.");

                    // 🔹 Kategorie przychodów i kosztów
                    var incomeCategories = new[] { "Bartek", "Gosia", "Inne" };
                    var expenseCategories = new[]
                    {
                        "Czynsz",
                        "Gaz",
                        "Prąd",
                        "Woda",
                        "Play",
                        "Przedszkole",
                        "Koń",
                        "Ubezp_Gosia",
                        "Rata",
                        "Telefon",
                        "Abonamenty_inne",
                        "Inne_wydatki"
                    };

                    // 🔹 Przygotowanie danych dla UI (każda transakcja wie, które kategorie ma wyświetlić)
                    var itemsForUi = unknowns.Select(i => new
                    {
                        idx = i,
                        date = txs[i].DataTransakcji,
                        recipient = txs[i].Odbiorca,
                        opis = txs[i].Opis,
                        kwota = txs[i].Kwota,
                        availableCategories = txs[i].Kwota >= 0 ? incomeCategories : expenseCategories
                    }).ToArray();

                    var payload = JsonSerializer.Serialize(new
                    {
                        cmd = "classify",
                        items = itemsForUi
                    });

                    _core?.PostWebMessageAsJson(payload);

                    // 🔹 Czekaj na klasyfikację użytkownika
                    _classificationTcs = new TaskCompletionSource<JsonElement>();
                    var result = await _classificationTcs.Task;

                    if (result.TryGetProperty("mappings", out var mappings))
                    {
                        foreach (var m in mappings.EnumerateArray())
                        {
                            int idx = m.GetProperty("idx").GetInt32();
                            string selectedCategory = m.GetProperty("category").GetString() ?? "";
                            bool applyToAll = m.GetProperty("applyToAll").GetBoolean();
                            string keyword = m.TryGetProperty("keyword", out var k) ? (k.GetString() ?? "") : "";

                            if (idx < 0 || idx >= txs.Count) continue;
                            var tx = txs[idx];

                            if (Enum.TryParse(typeof(CategoryName), selectedCategory, true, out var enumVal))
                            {
                                var cat = new Category
                                {
                                    Name = (CategoryName)enumVal,
                                    Type = tx.Kwota >= 0 ? "Uznanie" : "Obciążenie"
                                };

                                tx.Kategoria = cat;

                                if (applyToAll)
                                {
                                    string key = string.IsNullOrWhiteSpace(keyword)
                                        ? (tx.Odbiorca ?? tx.Opis ?? "")
                                        : keyword;

                                    cm.AddRule(key.ToLowerInvariant(), cat, true);
                                    AppendLog($"Zapisano regułę: '{key}' → {cat.Name}");
                                }
                            }
                        }
                    }

                    _classificationTcs = null;
                }

                // 🔹 Wyślij gotowe dane do przeglądarki
                var items = txs.Select(t => new
                {
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

        private async Task HandleFullUpdate()
        {
            if (string.IsNullOrWhiteSpace(_excelPath))
            {
                AppendLog("Wybierz plik budżetu Excel.");
                return;
            }
            if (string.IsNullOrWhiteSpace(_csvPath))
            {
                AppendLog("Wybierz plik CSV.");
                return;
            }

            try
            {
                await HandleLoadTransactions();

                var loader = new LoadDataFromFile();
                var txs = loader.LoadData(_csvPath);

                var missing = txs.Where(t => t.Kategoria == null).ToList();
                if (missing.Any())
                {
                    AppendLog($"Pozostały niezaklasyfikowane transakcje ({missing.Count}).");
                    return;
                }

                var updater = new Test.BudgetUpdater(_excelPath);
                AppendLog("Aktualizuję budżet...");
                await Task.Run(() => updater.UpdateBudget(txs));
                AppendLog("Aktualizacja zakończona pomyślnie.");
            }
            catch (Exception ex)
            {
                AppendLog($"Błąd aktualizacji: {ex.Message}");
            }
        }

        // ==================== GUI ====================

        private void BtnChooseCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "CSV Files|*.csv|All files|*.*" };
            if (dlg.ShowDialog() == true)
            {
                _csvPath = dlg.FileName;
                TxtCsvPath.Text = _csvPath;
                AppendLog($"Wybrano CSV: {_csvPath}");
            }
        }

        private void BtnChooseExcel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Excel Files|*.xlsx;*.xlsm;*.xls|All files|*.*" };
            if (dlg.ShowDialog() == true)
            {
                _excelPath = dlg.FileName;
                TxtExcelPath.Text = _excelPath;
                AppendLog($"Wybrano plik budżetu: {_excelPath}");
            }
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e) => _ = HandleLoadTransactions();
        private void BtnUpdate_Click(object sender, RoutedEventArgs e) => _ = HandleFullUpdate();
    }
}
