using OfficeOpenXml;
using System.Globalization;

namespace Test
{
    public class BudgetUpdater
    {
        private readonly string _budgetPath;
        private readonly string _logPath;
        private readonly HashSet<string> IncomeCategories;
        private readonly HashSet<string> FixedExpenseCategories;
        private readonly List<ExcelAddress> ProtectedRanges; // ranges we must not overwrite (autosums)

        // mapowanie miesiąca -> nazwa arkusza (dopasuj jeśli masz inne nazwy)
        private static readonly string[] MonthSheets =
        {
            "", "STY", "LUT", "MARZ", "KWIE", "MAJ", "CZERW", "LIP", "SIE", "WRZE", "PAŹDŹ", "LIST", "GRU"
        };

        public BudgetUpdater(string budgetPath)
        {
            _budgetPath = budgetPath ?? throw new ArgumentNullException(nameof(budgetPath));
            _logPath = Path.Combine(Path.GetDirectoryName(budgetPath) ?? ".", "budget_update_log.txt");

            EnsureEpplusLicenseSet();

            // Użyj HashSet zamiast List, by porównania były case-insensitive
            IncomeCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Bartek", "Gosia", "INNE"
    };

            FixedExpenseCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Czynsz","Gaz","Prąd","Woda","Play","Przedszkole","Koń","Ubezp_Gosia","Rata","Telefon","Abonamenty_inne"
    };

            // Zdefiniuj dokładne zakresy autosum jako excel addressy:
            ProtectedRanges = new List<ExcelAddress>
    {
        new ExcelAddress("C8"),
        new ExcelAddress("C12:C17"),
        new ExcelAddress("E20:G20"),
        new ExcelAddress("H20:J20"),
        new ExcelAddress("G5:I5"),
        new ExcelAddress("G7:I7")
    };
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


        public void UpdateBudget(List<Transaction> transactions)
        {
            if (!File.Exists(_budgetPath))
                throw new FileNotFoundException("Plik budżetu nie istnieje.", _budgetPath);

            // backup
            try
            {
                var backupDir = Path.Combine(Path.GetDirectoryName(_budgetPath) ?? ".", "backups");
                Directory.CreateDirectory(backupDir);
                var backupName = Path.Combine(backupDir, $"Budzet_backup_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                File.Copy(_budgetPath, backupName, true);
                Log($"Backup utworzony: {backupName}");
            }
            catch (Exception ex)
            {
                Log($"Nie udało się utworzyć backupu: {ex.Message}");
            }

            using var package = new ExcelPackage(new FileInfo(_budgetPath));

            foreach (var t in transactions)
            {
                try
                {
                    // parsowanie daty z pola DataTransakcji (Twoja klasa ma to pole jako string)
                    if (!TryParseDate(t.DataTransakcji, out DateTime dt))
                    {
                        Log($"Niepoprawna data: '{t.DataTransakcji}' - pomijam transakcję.");
                        continue;
                    }

                    string sheetName = MonthSheets.Length > dt.Month ? MonthSheets[dt.Month] : null;
                    if (string.IsNullOrWhiteSpace(sheetName))
                    {
                        Log($"Nieznana nazwa arkusza dla miesiąca {dt.Month} - pomijam.");
                        continue;
                    }

                    var ws = package.Workbook.Worksheets
                        .FirstOrDefault(s => string.Equals(s.Name, sheetName, StringComparison.OrdinalIgnoreCase));

                    if (ws == null)
                    {
                        Log($"Brak arkusza '{sheetName}' - pomijam transakcję {t.DataTransakcji} | {t.Odbiorca}");
                        continue;
                    }

                    double amount = Math.Abs(t.Kwota);
                    string tipo = t.Kategoria?.Type ?? (t.Kwota >= 0 ? "Uznanie" : "Obciążenie");

                    string catName = t.Kategoria?.Name.ToString().Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(catName))
                    {
                        Log($"Brak kategorii dla transakcji: {t.DataTransakcji} | {t.Odbiorca}");
                        continue;
                    }

                    bool written = false;

                    if (IncomeCategories.Contains(catName) || FixedExpenseCategories.Contains(catName))
                    {
                        var catInfo = FindCategoryCell(ws, catName);
                        if (catInfo.found)
                        {
                            int targetCol = catInfo.endCol + 1;
                            int row = catInfo.row;

                            if (IsInProtectedRanges(row, targetCol))
                            {
                                Log($"Pominięto (protected autosum) komórkę obok kategorii {catName} (r{row}c{targetCol}).");
                            }
                            else
                            {
                                var targetCell = ws.Cells[row, targetCol];
                                bool added = TransactionJournal.AddEntry(new TransactionLogEntry
                                {
                                    Date = dt,
                                    Category = catName,
                                    Amount = amount,
                                    Recipient = t.Odbiorca,
                                    Sheet = sheetName
                                });

                                if (!added)
                                {
                                    Log($"Pominięto duplikat: {catName} | {amount} | {t.Odbiorca}");
                                    continue;
                                }

                                AppendAmountToCellFormula(targetCell, amount);
                                targetCell.Style.Numberformat.Format = "0.00";
                                targetCell.Style.Font.Color.SetColor(System.Drawing.Color.Black);

                                Log($"Wpisano (stałe/przychód): {catName} -> {targetCell.Address} := {targetCell.Formula ?? targetCell.Value?.ToString()}");
                                written = true;
                            }
                        }
                        else
                        {
                            Log($"Nie znaleziono kategorii (stałe/przychód): {catName}");
                        }
                    }
                    else if (tipo == "Obciążenie")
                    {
                        var catInfo = FindCategoryCell(ws, catName);
                        if (catInfo.found)
                        {
                            int row = catInfo.row;
                            int targetCol = 3 + dt.Day;
                            if (IsInProtectedRanges(row, targetCol))
                            {
                                Log($"Pominięto (protected autosum) komórkę dnia dla {catName} (r{row}c{targetCol}).");
                            }
                            else
                            {
                                var dayCell = ws.Cells[row, targetCol];
                                bool added = TransactionJournal.AddEntry(new TransactionLogEntry
                                {
                                    Date = dt,
                                    Category = catName,
                                    Amount = amount,
                                    Recipient = t.Odbiorca,
                                    Sheet = sheetName
                                });

                                if (!added)
                                {
                                    Log($"Pominięto duplikat: {catName} | {amount} | {t.Odbiorca}");
                                    continue;
                                }
                                AppendAmountToCellFormula(dayCell, amount);
                                dayCell.Style.Numberformat.Format = "0.00";
                                dayCell.Style.Font.Color.SetColor(System.Drawing.Color.Black);

                                Log($"Wpisano (zmienne): {catName} -> {dayCell.Address} := {dayCell.Formula ?? dayCell.Value?.ToString()}");
                                written = true;
                            }
                        }
                        else
                        {
                            Log($"Nie znaleziono kategorii (zmienne): {catName}");
                        }
                    }

                    else
                    {
                        Log($"Pominięto transakcję (niepasuje do żadnej sekcji): {catName} | {t.DataTransakcji}");
                    }

                    if (!written)
                    {
                        Log($"Pominięto: {t.DataTransakcji} | {catName} | {amount:0.00} zł");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Błąd przy transakcji ({t.DataTransakcji}): {ex.Message}");
                }
            } // foreach

            package.Workbook.CalcMode = ExcelCalcMode.Automatic;
            package.Workbook.FullCalcOnLoad = true;
            package.Workbook.Calculate();

            package.Save();
            Log("Zapisano plik budżetu.");
        }

        // DOPISUJE kwotę do komórki w postaci formuły: 
        // - jeśli komórka pusta -> "=amount"
        // - jeśli komórka ma liczbę -> "=existing+amount"
        // - jeśli ma formułę -> "existingFormula + +amount"
        // UWAGA: usunięto detekcję "czy ta sama liczba już jest w formule" — 
        // deduplikację pozostawiamy w TransactionJournal.
        private void AppendAmountToCellFormula(ExcelRange cell, double amount)
        {
            if (cell == null) return;

            // normalizacja reprezentacji amount (Invariant -> kropka jako separator)
            string amountToken = amount.ToString("0.##", CultureInfo.InvariantCulture);

            // 1) jeśli komórka ma formułę - po prostu dopisz +amount
            var formula = cell.Formula?.Trim();
            if (!string.IsNullOrWhiteSpace(formula))
            {
                // upewnij się, że formuła zaczyna się od "="
                if (!formula.StartsWith("=")) formula = "=" + formula;
                cell.Formula = formula + "+" + amountToken;
                return;
            }

            // 2) jeśli brak formuły, sprawdź czy jest liczba (tekstowa)
            string text = cell.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(text) && double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double existingValue))
            {
                // Jeśli istnieje liczba -> przekształć do formuły =existing+amount
                string eToken = existingValue.ToString("0.##", CultureInfo.InvariantCulture);
                cell.Formula = $"={eToken}+{amountToken}";
                return;
            }

            // 3) pusta komórka -> wpisz formułę =amount
            cell.Formula = "=" + amountToken;
        }



        // Szuka kategorii w arkuszu, obsługuje scalone komórki.
        // Zwraca tuple: (found,row,startCol,endCol)
        private (bool found, int row, int startCol, int endCol) FindCategoryCell(ExcelWorksheet ws, string category)
        {
            if (ws.Dimension == null) return (false, -1, -1, -1);

            int maxCol = ws.Dimension.End.Column;
            int maxRow = ws.Dimension.End.Row;

            // przygotuj listę scalonych zakresów jako ExcelAddress do szybszego sprawdzania
            var merged = ws.MergedCells.Select(addr => new ExcelAddress(addr)).ToArray();

            for (int r = 1; r <= maxRow; r++)
            {
                for (int c = 1; c <= maxCol; c++)
                {
                    var cell = ws.Cells[r, c];
                    string text = cell.Text?.Trim();

                    if (string.IsNullOrEmpty(text))
                    {
                        // jeśli komórka jest częścią scalonego zakresu pobierz wartość z jego pierwszej komórki
                        var mergedRange = merged.FirstOrDefault(m => r >= m.Start.Row && r <= m.End.Row && c >= m.Start.Column && c <= m.End.Column);
                        if (mergedRange != null)
                        {
                            var master = ws.Cells[mergedRange.Start.Row, mergedRange.Start.Column];
                            text = master.Text?.Trim();
                            if (string.IsNullOrEmpty(text)) continue;

                            if (string.Equals(text, category, StringComparison.OrdinalIgnoreCase))
                                return (true, mergedRange.Start.Row, mergedRange.Start.Column, mergedRange.End.Column);
                        }
                    }
                    else
                    {
                        if (string.Equals(text, category, StringComparison.OrdinalIgnoreCase))
                        {
                            // sprawdź czy to jest start scalonego zakresu
                            var mergedRange = merged.FirstOrDefault(m => r >= m.Start.Row && r <= m.End.Row && c >= m.Start.Column && c <= m.End.Column);
                            if (mergedRange != null)
                                return (true, mergedRange.Start.Row, mergedRange.Start.Column, mergedRange.End.Column);
                            else
                                return (true, r, c, c);
                        }
                    }
                }
            }

            return (false, -1, -1, -1);
        }

        // Sprawdza, czy dana komórka (row,col) leży wewnątrz jednego z chronionych zakresów (autosum)
        private bool IsInProtectedRanges(int row, int col)
        {
            foreach (var ra in ProtectedRanges)
            {
                if (row >= ra.Start.Row && row <= ra.End.Row &&
                    col >= ra.Start.Column && col <= ra.End.Column)
                    return true;
            }
            return false;
        }

        // Parsowanie daty defensywnie - bierze pod uwagę format yyyy-MM-dd i inne
        private bool TryParseDate(string s, out DateTime dt)
        {
            dt = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (DateTime.TryParse(s, out dt)) return true;
            string[] fmts = { "yyyy-MM-dd", "yyyy/MM/dd", "dd.MM.yyyy", "dd/MM/yyyy", "MM/dd/yyyy" };
            foreach (var f in fmts)
                if (DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                    return true;
            return false;
        }

        private void Log(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            Console.WriteLine(line);
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch { /* ignore logging errors */ }
        }
    }
}
