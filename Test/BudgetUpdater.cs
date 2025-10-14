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

                    // ustalenie kwoty i typu ******************* Po co skoro już jest ustalona *************************\/
                    double amount = 0;
                    string tipo = "";
                    if (t.Uznania > 0 && Math.Abs(t.Obciazenia) < 0.0001)
                    {
                        amount = t.Uznania;
                        tipo = "Uznanie";
                    }
                    else if (t.Obciazenia < 0 && Math.Abs(t.Uznania) < 0.0001)
                    {
                        amount = Math.Abs(t.Obciazenia);
                        tipo = "Obciążenie";
                    }
                    else
                    {
                        // obie wartości puste/niejednoznaczne -> pomijamy (można tu dodać heurystykę)
                        Log($"Nieokreślony typ transakcji (Uznania/Obciazenia) dla: {t.DataTransakcji} | {t.Odbiorca}");
                        continue;
                    }

                    string catName = t.Kategoria?.Name.ToString().Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(catName))
                    {
                        Log($"Brak kategorii dla transakcji: {t.DataTransakcji} | {t.Odbiorca}");
                        continue;
                    }
                    //********************************************************************************************/\
                  
                    bool written = false;

                    // PRZYCHODY i KOSZTY STAŁE -> wpis obok nazwy kategorii (po prawej stronie scalonego zakresu)
                    if (IncomeCategories.Any(x => string.Equals(x, catName, StringComparison.OrdinalIgnoreCase)) ||
                        FixedExpenseCategories.Any(x => string.Equals(x, catName, StringComparison.OrdinalIgnoreCase)))
                    {
                        var catInfo = FindCategoryCell(ws, catName);
                        if (catInfo.found)
                        {
                            int targetCol = catInfo.endCol + 1; // komórka po prawej stronie scalonego zakresu
                            int row = catInfo.row;

                            // zabezpieczenie: jeśli target kolumna leży w protected range - pomijamy
                            if (IsInProtectedRanges(row, targetCol))
                            {
                                Log($"Pominięto (protected autosum) komórkę obok kategorii {catName} (r{row}c{targetCol}).");
                            }
                            else
                            {
                                var targetCell = ws.Cells[row, targetCol];
                                AppendAmountToCellFormula(targetCell, amount);
                                Log($"Wpisano (stałe/przychód): {catName} -> {targetCell.Address} := {targetCell.Formula ?? targetCell.Value?.ToString()}");
                                written = true;
                            }
                        }
                        else
                        {
                            Log($"Nie znaleziono kategorii (stałe/przychód): {catName}");
                        }
                    }
                    // WYDATKI ZMIENNE -> w kolumnach dni (D..AH)
                    else if (tipo == "Obciążenie")
                    {
                        var catInfo = FindCategoryCell(ws, catName);
                        if (catInfo.found)
                        {
                            int row = catInfo.row;
                            int targetCol = 3 + dt.Day; // D=4 dla day=1
                            if (IsInProtectedRanges(row, targetCol))
                            {
                                Log($"Pominięto (protected autosum) komórkę dnia dla {catName} (r{row}c{targetCol}).");
                            }
                            else
                            {
                                var dayCell = ws.Cells[row, targetCol];
                                AppendAmountToCellFormula(dayCell, amount);
                                Log($"Wpisano (zmienne): {catName} -> {dayCell.Address} := {dayCell.Formula ?? dayCell.Value?.ToString()}");
                                written = true;

                                // opcjonalnie: jeśli kolumna C (sum) nie ma formuły, zaktualizuj sumę liczbowo
                                var sumCell = ws.Cells[row, 3];
                                if (string.IsNullOrWhiteSpace(sumCell.Formula))
                                {
                                    double s = 0;
                                    for (int c = 4; c <= 34; c++)
                                    {
                                        if (double.TryParse(ws.Cells[row, c].Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                                            s += v;
                                    }
                                    sumCell.Value = s;
                                }
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

            package.Save();
            Log("Zapisano plik budżetu.");
        }

        // DOPISUJE kwotę do komórki w postaci formuły: jeśli komórka pusta -> "=amount"
        // jeśli komórka ma liczbę -> "=existing+amount", jeśli ma formułę -> "existingFormula + +amount"
        private void AppendAmountToCellFormula(ExcelRange cell, double amount)
        {
            var curFormula = cell.Formula?.Trim();
            var curText = cell.Text?.Trim();

            if (!string.IsNullOrWhiteSpace(curFormula))
            {
                // istniejąca formuła (np. "=10+20") -> dopisz +amount
                if (!curFormula.StartsWith("=")) curFormula = "=" + curFormula;
                cell.Formula = curFormula + "+" + amount.ToString(CultureInfo.InvariantCulture);
            }
            else if (!string.IsNullOrWhiteSpace(curText) && double.TryParse(curText, NumberStyles.Any, CultureInfo.InvariantCulture, out double existing))
            {
                // istnieje liczba -> przekształć do formuły
                cell.Formula = $"={existing.ToString(CultureInfo.InvariantCulture)}+{amount.ToString(CultureInfo.InvariantCulture)}";
            }
            else
            {
                // pusta -> ustaw formułę
                cell.Formula = $"={amount.ToString(CultureInfo.InvariantCulture)}";
            }
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
