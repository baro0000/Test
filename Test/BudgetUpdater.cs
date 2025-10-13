using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;

namespace Test
{
    public class BudgetUpdater
    {
        private readonly string _budgetPath;
        private readonly string _txLogPath = "txlog.json";
        private HashSet<string> _processedTx;

        private static readonly string[] MonthSheets =
        {
            "", "STY", "LUT", "MARZ", "KWIE", "MAJ", "CZERW", "LIP", "SIE", "WRZE", "PAŹDŹ", "LIST", "GRU"
        };

        private static readonly HashSet<string> FixedCategories = new(StringComparer.OrdinalIgnoreCase)
        {
            "CZYNSZ", "GAZ", "PRĄD", "WODA", "PLAY", "PRZEDSZKOLE", "KOŃ", "UBEZP_GOSIA",
            "RATA", "TELEFON", "ABONAMENTY_INNE"
        };

        private static readonly HashSet<string> IncomeCategories = new(StringComparer.OrdinalIgnoreCase)
        {
            "BARTEK", "GOSIA", "INNE"
        };

        public BudgetUpdater(string budgetPath)
        {
            _budgetPath = budgetPath ?? throw new ArgumentNullException(nameof(budgetPath));
            LoadTxLog();
        }

        private void EnsureEpplusLicenseSet()
        {
            try
            {
                // Ustawienie licencji — tutaj używamy non-commercial personal z nazwą użytkownika systemowego.
                // Zmodyfikuj, jeśli chcesz inny tryb (organizacja lub licencja komercyjna).
                var user = Environment.UserName;
                if (string.IsNullOrWhiteSpace(user))
                    user = "NonCommercialUser";

                // Wywołanie na statycznej właściwości License (EPPlus 8+)
                ExcelPackage.License.SetNonCommercialPersonal(user);

                // Alternatywnie (jeśli chcesz organizację):
                // ExcelPackage.License.SetNonCommercialOrganization("MojaOrganizacja");

                // Jeśli masz klucz komercyjny:
                // ExcelPackage.License.SetCommercial("<your-license-key>");
            }
            catch (Exception ex)
            {
                // Jeżeli ustawienie licencji się nie uda, wypisz ostrzeżenie - ale nadal próbujemy działać.
                Console.WriteLine($"Uwaga: problem z ustawieniem licencji EPPlus: {ex.Message}");
            }
        }

        private void LoadTxLog()
        {
            if (File.Exists(_txLogPath))
            {
                try
                {
                    var json = File.ReadAllText(_txLogPath);
                    _processedTx = new HashSet<string>(
                        System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>()
                    );
                }
                catch
                {
                    _processedTx = new HashSet<string>();
                }
            }
            else
            {
                _processedTx = new HashSet<string>();
            }
        }

        private void SaveTxLog()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_processedTx.ToList(),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_txLogPath, json);
        }

        private static void Log(string msg) =>
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}");

        // Tworzy klucz transakcji (data|kwota|odbiorca) na podstawie pól istniejących w Transaction
        private string TxKey(Transaction t)
        {
            string dateStr = t.DataTransakcji?.Trim() ?? "";
            string odb = (t.Odbiorca ?? "").Trim().ToLowerInvariant();

            double amount = 0;
            if (t.Uznania > 0 && t.Obciazenia == 0)
                amount = t.Uznania;
            else if (t.Obciazenia < 0 && t.Uznania == 0)
                amount = Math.Abs(t.Obciazenia);
            else
                amount = 0.0; // jeśli nie uda się ustalić, klucz i tak będzie zawierał 0.00

            return $"{dateStr}|{amount.ToString("F2", CultureInfo.InvariantCulture)}|{odb}";
        }

        private bool IsProcessed(Transaction t) => _processedTx.Contains(TxKey(t));

        private void MarkProcessed(Transaction t) => _processedTx.Add(TxKey(t));

        public void UpdateBudget(List<Transaction> transactions)
        {
            EnsureEpplusLicenseSet();

            if (!File.Exists(_budgetPath))
                throw new FileNotFoundException("Plik budżetu nie istnieje.", _budgetPath);

            // Backup
            try
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backup = Path.Combine(Path.GetDirectoryName(_budgetPath) ?? ".", Path.GetFileNameWithoutExtension(_budgetPath) + $"_backup_{stamp}" + Path.GetExtension(_budgetPath));
                File.Copy(_budgetPath, backup, true);
                Log($"✅ Backup utworzony: {backup}");
            }
            catch (Exception ex)
            {
                Log($"⚠️ Nie udało się utworzyć backupu: {ex.Message}");
            }

            using var package = new ExcelPackage(new FileInfo(_budgetPath));

            foreach (var t in transactions)
            {
                try
                {
                    if (IsProcessed(t)) continue;

                    if (!DateTime.TryParse(t.DataTransakcji, out var dt))
                    {
                        Log($"⚠️ Niepoprawna data transakcji: {t.DataTransakcji}");
                        continue;
                    }

                    int month = dt.Month;
                    string sheetName = MonthSheets[month];
                    var ws = package.Workbook.Worksheets.FirstOrDefault(s =>
                        string.Equals(s.Name, sheetName, StringComparison.OrdinalIgnoreCase));

                    if (ws == null)
                    {
                        Log($"⚠️ Nie znaleziono arkusza dla miesiąca {sheetName}");
                        continue;
                    }

                    // Ustal typ i kwotę transakcji bez użycia nieistniejącego pola Kwota
                    string tipo;
                    double amount;
                    if (t.Uznania > 0 && t.Obciazenia == 0)
                    {
                        tipo = "Uznanie";
                        amount = t.Uznania;
                    }
                    else if (t.Uznania == 0 && t.Obciazenia < 0)
                    {
                        tipo = "Obciążenie";
                        amount = Math.Abs(t.Obciazenia);
                    }
                    else
                    {
                        // jeżeli nie da się ustalić z pól Uznania/Obciazenia -> pomijamy (można rozszerzyć tu heurystykę)
                        Log($"?? Nieokreślony typ transakcji (brak Uznania/Obciążenia): {t.DataTransakcji} | {t.Odbiorca}");
                        continue;
                    }

                    string catName = t.Kategoria?.Name.ToString().Trim().ToUpperInvariant() ?? "";
                    bool written = false;

                    // PRZYCHODY
                    if (IncomeCategories.Contains(catName))
                    {
                        var cell = FindCellForCategory(ws, catName);
                        if (cell != null)
                        {
                            // nie nadpisujemy, jeżeli docelowa komórka ma formułę
                            var right = ws.Cells[cell.Start.Row, cell.Start.Column + 1];
                            if (!string.IsNullOrWhiteSpace(right.Formula))
                            {
                                Log($"⛔ Pomijam przychód {catName} - docelowa komórka zawiera formułę.");
                                written = true; // traktujemy jako obsłużone
                            }
                            else
                            {
                                // wpisz formułę lub dopisz do istniejącej formuły
                                string cur = right.Text?.Trim() ?? "";
                                if (string.IsNullOrEmpty(cur))
                                    right.Formula = $"={amount.ToString(CultureInfo.InvariantCulture)}";
                                else
                                {
                                    if (!cur.StartsWith("=")) cur = "=" + cur;
                                    right.Formula = cur + "+" + amount.ToString(CultureInfo.InvariantCulture);
                                }
                                written = true;
                            }
                        }
                    }
                    // WYDATKI STAŁE -> wpis obok nazwy kategorii (kolumna po prawej)
                    else if (FixedCategories.Contains(catName))
                    {
                        var cell = FindCellForCategory(ws, catName);
                        if (cell != null)
                        {
                            var right = ws.Cells[cell.Start.Row, cell.Start.Column + 1];
                            if (!string.IsNullOrWhiteSpace(right.Formula))
                            {
                                Log($"⛔ Pomijam stałą kategorię {catName} - docelowa komórka zawiera formułę.");
                                written = true;
                            }
                            else
                            {
                                string cur = right.Text?.Trim() ?? "";
                                if (string.IsNullOrEmpty(cur))
                                    right.Formula = $"={amount.ToString(CultureInfo.InvariantCulture)}";
                                else
                                {
                                    if (!cur.StartsWith("=")) cur = "=" + cur;
                                    right.Formula = cur + "+" + amount.ToString(CultureInfo.InvariantCulture);
                                }
                                written = true;
                            }
                        }
                    }
                    // WYDATKI ZMIENNE -> w kolumnę dnia (D..AH)
                    else
                    {
                        int headerRowVar = FindRowWithText(ws, "WYDATKI ZMIENNE");
                        if (headerRowVar != -1)
                        {
                            int row = FindRowWithText(ws, catName, headerRowVar + 1);
                            if (row != -1)
                            {
                                int col = 3 + dt.Day; // D=4 dla dnia 1
                                var cell = ws.Cells[row, col];

                                // jeśli komórka ma już formułę -> dopisz do formuły; inaczej utwórz formułę
                                string cur = cell.Text?.Trim() ?? "";
                                if (string.IsNullOrEmpty(cur))
                                {
                                    cell.Formula = $"={amount.ToString(CultureInfo.InvariantCulture)}";
                                }
                                else
                                {
                                    if (!cur.StartsWith("=")) cur = "=" + cur;
                                    cell.Formula = cur + "+" + amount.ToString(CultureInfo.InvariantCulture);
                                }

                                // Nie nadpisuj komórki sumującej (kolumna C) gdy zawiera formułę
                                var sumCell = ws.Cells[row, 3];
                                if (string.IsNullOrWhiteSpace(sumCell.Formula))
                                {
                                    double s = 0;
                                    for (int c = 4; c <= 34; c++)
                                        if (double.TryParse(ws.Cells[row, c].Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                                            s += v;
                                    sumCell.Value = s;
                                }

                                written = true;
                            }
                        }
                    }

                    if (written)
                    {
                        MarkProcessed(t);
                        Log($"✅ Przetworzono: {t.DataTransakcji} | {catName} | {amount:0.00} zł ␦ {sheetName}");
                    }
                    else
                    {
                        Log($"?? Nie dopasowano lub pominięto: {t.DataTransakcji} | {catName} | {amount:0.00} zł ␦ {sheetName}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"⚠️ Błąd: {ex.Message}");
                }
            }

            package.Save();
            SaveTxLog();
            Log("✅ Zapisano zmiany w pliku budżetu.");
        }

        private int FindRowWithText(ExcelWorksheet ws, string text, int startRow = 1)
        {
            if (ws.Dimension == null) return -1;
            string up = text.Trim().ToUpperInvariant();
            for (int r = startRow; r <= ws.Dimension.End.Row; r++)
            {
                for (int c = 1; c <= ws.Dimension.End.Column; c++)
                {
                    var val = ws.Cells[r, c].Text?.Trim().ToUpperInvariant();
                    if (val == up) return r;
                }
            }
            return -1;
        }

        private ExcelRange? FindCellForCategory(ExcelWorksheet ws, string catName)
        {
            if (ws.Dimension == null) return null;
            for (int r = 1; r <= ws.Dimension.End.Row; r++)
            {
                for (int c = 1; c <= ws.Dimension.End.Column; c++)
                {
                    var val = ws.Cells[r, c].Text?.Trim().ToUpperInvariant();
                    if (val == catName) return ws.Cells[r, c];
                }
            }
            return null;
        }
    }
}
