using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using System.Text.Json;

namespace Test
{
    public class BudgetUpdater
    {
        private readonly string _budgetPath;
        private readonly string _lastTxFile = "last_tx.json";
        private readonly string _logFile = "log.txt";

        private static readonly string[] MonthSheets =
        {
            "", "STY", "LUT", "MARZ", "KWIE", "MAJ", "CZERW", "LIP", "SIE", "WRZE", "PAŹDŹ", "LIST", "GRU"
        };

        // Kategorie przychodów
        private static readonly HashSet<string> IncomeCategories = new(StringComparer.OrdinalIgnoreCase)
        {
            "BARTEK", "GOSIA", "INNE"
        };

        // Kategorie kosztów stałych
        private static readonly HashSet<string> FixedExpenseCategories = new(StringComparer.OrdinalIgnoreCase)
        {
            "CZYNSZ", "GAZ", "PRĄD", "WODA", "PLAY", "PRZEDSZKOLE", "KOŃ", "UBEZP_GOSIA",
            "RATA", "TELEFON", "ABONAMENTY_INNE"
        };

        private Transaction? _lastProcessedTransaction;

        public BudgetUpdater(string budgetPath)
        {
            _budgetPath = budgetPath ?? throw new ArgumentNullException(nameof(budgetPath));
            LoadLastTransaction();
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

        private void Log(string message)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            Console.WriteLine(line);
            File.AppendAllText(_logFile, line + Environment.NewLine);
        }

        private void LoadLastTransaction()
        {
            if (File.Exists(_lastTxFile))
            {
                try
                {
                    var json = File.ReadAllText(_lastTxFile);
                    _lastProcessedTransaction = JsonSerializer.Deserialize<Transaction>(json);
                }
                catch
                {
                    _lastProcessedTransaction = null;
                }
            }
        }

        private void SaveLastTransaction(Transaction t)
        {
            string json = JsonSerializer.Serialize(t, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_lastTxFile, json);
        }

        public void UpdateBudget(List<Transaction> transactions)
        {
            EnsureEpplusLicenseSet();

            if (!File.Exists(_budgetPath))
                throw new FileNotFoundException("Nie znaleziono pliku budżetu.", _budgetPath);

            var newTransactions = FilterNewTransactions(transactions);

            if (newTransactions.Count == 0)
            {
                Log("Brak nowych transakcji do przetworzenia.");
                return;
            }

            using var package = new ExcelPackage(new FileInfo(_budgetPath));

            foreach (var t in newTransactions)
            {
                try
                {
                    if (!DateTime.TryParse(t.DataTransakcji, out DateTime dt))
                    {
                        DateTime.TryParseExact(t.DataTransakcji, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
                    }

                    int month = dt.Month;
                    string sheetName = MonthSheets[month];
                    var ws = package.Workbook.Worksheets.FirstOrDefault(s => string.Equals(s.Name, sheetName, StringComparison.OrdinalIgnoreCase));

                    if (ws == null)
                    {
                        Log($"⚠️ Nie znaleziono arkusza dla miesiąca: {sheetName}. Pomijam transakcję.");
                        continue;
                    }

                    string tipo = (t.Uznania > 0 && t.Obciazenia == 0) ? "Uznanie" :
                                  (t.Obciazenia < 0 && t.Uznania == 0) ? "Obciążenie" : "";

                    double amount = (t.Uznania > 0 && t.Obciazenia == 0) ? t.Uznania : Math.Abs(t.Obciazenia);
                    string catName = t.Kategoria?.Name.ToString().Trim() ?? "";

                    bool written = false;

                    if (IncomeCategories.Contains(catName))
                    {
                        written = UpdateFixedOrIncome(ws, catName, amount, sheetName);
                    }
                    else if (FixedExpenseCategories.Contains(catName))
                    {
                        written = UpdateFixedOrIncome(ws, catName, amount, sheetName);
                    }
                    else if (tipo == "Obciążenie")
                    {
                        written = UpdateExpense(ws, catName, amount, dt.Day, sheetName);
                    }

                    if (written)
                    {
                        Log($"✅ Dodano: {t.DataTransakcji} | {catName} | {amount:F2} zł ({tipo}) → {sheetName}");
                        _lastProcessedTransaction = t;
                    }
                    else
                    {
                        Log($"⚠️ Nie dopasowano kategorii: {catName} ({t.DataTransakcji})");
                    }
                }
                catch (Exception ex)
                {
                    Log($"❌ Błąd przetwarzania transakcji: {ex.Message}");
                }
            }

            package.Save();

            if (_lastProcessedTransaction != null)
                SaveLastTransaction(_lastProcessedTransaction);

            Log("✅ Zaktualizowano budżet i zapisano plik Excel.");
        }

        // ===================== METODY POMOCNICZE =====================

        private List<Transaction> FilterNewTransactions(List<Transaction> all)
        {
            if (_lastProcessedTransaction == null)
                return all;

            if (!DateTime.TryParse(_lastProcessedTransaction.DataTransakcji, out DateTime lastDate))
                return all;

            var ordered = all.OrderBy(t => DateTime.Parse(t.DataTransakcji)).ToList();
            int lastIndex = ordered.FindLastIndex(t =>
                t.DataTransakcji == _lastProcessedTransaction.DataTransakcji &&
                Math.Abs(t.Obciazenia - _lastProcessedTransaction.Obciazenia) < 0.01 &&
                string.Equals(t.Odbiorca, _lastProcessedTransaction.Odbiorca, StringComparison.OrdinalIgnoreCase)
            );

            int startIndex = Math.Max(0, lastIndex - 10);
            return ordered.Skip(startIndex).ToList();
        }

        // ✅ Dla przychodów i kosztów stałych
        private bool UpdateFixedOrIncome(ExcelWorksheet ws, string category, double amount, string sheetName)
        {
            if (ws.Dimension == null) return false;

            for (int r = 1; r <= ws.Dimension.End.Row; r++)
            {
                string cellA = ws.Cells[r, 1].Text.Trim().ToUpper();
                string cellB = ws.Cells[r, 2].Text.Trim().ToUpper();
                string merged = (cellA + " " + cellB).Trim();

                if (category.ToUpper() == merged || category.ToUpper() == cellA || category.ToUpper() == cellB)
                {
                    int col = string.IsNullOrWhiteSpace(ws.Cells[r, 2].Text) ? 2 : 3;
                    var valCell = ws.Cells[r, col];

                    double existing = 0;
                    double.TryParse(valCell.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out existing);

                    if (Math.Abs(existing - amount) < 0.01)
                    {
                        Log($"🔁 Pomijam (już istnieje): {category} ({amount:F2} zł) → {sheetName}");
                        return true;
                    }

                    valCell.Value = existing + amount;
                    return true;
                }
            }

            return false;
        }

        // ✅ Wydatki zmienne
        private bool UpdateExpense(ExcelWorksheet ws, string category, double amount, int day, string sheetName)
        {
            int headerRow = FindRowWithText(ws, "WYDATKI ZMIENNE");
            if (headerRow == -1) return false;

            int firstDayCol = 4; // D
            int lastDayCol = 34; // AH
            int targetCol = 3 + day; // D = 4 dla dnia 1

            for (int r = headerRow + 1; r <= ws.Dimension.End.Row; r++)
            {
                string a = ws.Cells[r, 1].Text?.Trim() ?? "";
                string b = ws.Cells[r, 2].Text?.Trim() ?? "";
                string merged = (a + " " + b).Trim();

                if (string.Equals(merged, category, StringComparison.OrdinalIgnoreCase))
                {
                    var dayCell = ws.Cells[r, targetCol];

                    if (double.TryParse(dayCell.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double existing))
                    {
                        if (Math.Abs(existing - amount) < 0.01)
                        {
                            Log($"🔁 Pomijam (już istnieje): {category}, dzień {day}, {amount:F2} zł → {sheetName}");
                            return true;
                        }
                        dayCell.Value = existing + amount;
                    }
                    else
                    {
                        dayCell.Value = amount;
                    }

                    var sumCell = ws.Cells[r, 3];
                    if (string.IsNullOrWhiteSpace(sumCell.Formula))
                    {
                        double total = 0;
                        for (int c = firstDayCol; c <= lastDayCol; c++)
                        {
                            if (double.TryParse(ws.Cells[r, c].Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                                total += v;
                        }
                        sumCell.Value = total;
                    }

                    return true;
                }
            }
            return false;
        }

        private int FindRowWithText(ExcelWorksheet ws, string text)
        {
            if (ws.Dimension == null) return -1;
            for (int r = 1; r <= ws.Dimension.End.Row; r++)
            {
                string val = ws.Cells[r, 1].Text?.Trim() ?? "";
                if (val.ToUpper().Contains(text.ToUpper()))
                    return r;
            }
            return -1;
        }
    }
}
