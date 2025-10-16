using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

public class TransactionLogEntry
{
    public DateTime Date { get; set; }
    public string Category { get; set; } = "";
    public double Amount { get; set; }
    public string Recipient { get; set; } = "";
    public string Sheet { get; set; } = "";
}

public static class TransactionLogger
{
    private static readonly string JournalPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "transactions_journal.json");

    // Lokalne cache entries, żeby nie wczytywać z pliku za każdym razem
    private static List<TransactionLogEntry> _entriesCache;

    private static List<TransactionLogEntry> Entries
    {
        get
        {
            if (_entriesCache == null)
                _entriesCache = LoadJournal();
            return _entriesCache;
        }
    }

    public static List<TransactionLogEntry> LoadJournal()
    {
        if (!File.Exists(JournalPath))
            return new List<TransactionLogEntry>();

        try
        {
            string json = File.ReadAllText(JournalPath);
            return JsonSerializer.Deserialize<List<TransactionLogEntry>>(json)
                   ?? new List<TransactionLogEntry>();
        }
        catch
        {
            // jeśli plik uszkodzony, tworzymy pusty
            return new List<TransactionLogEntry>();
        }
    }

    public static void SaveJournal(List<TransactionLogEntry> entries)
    {
        try
        {
            string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(JournalPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd zapisu Journal: {ex.Message}");
        }
    }

    public static bool Exists(DateTime date, string category, double amount, string recipient)
    {
        return Entries.Any(e =>
            e.Date.Date == date.Date &&
            e.Category.Equals(category, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(e.Amount - amount) < 0.001 &&
            e.Recipient.Equals(recipient, StringComparison.OrdinalIgnoreCase));
    }

    public static bool AddEntry(TransactionLogEntry entry)
    {
        if (Exists(entry.Date, entry.Category, entry.Amount, entry.Recipient))
            return false;

        Entries.Add(entry);
        SaveJournal(Entries);
        return true;
    }

    public static List<TransactionLogEntry> GetEntriesFromLastDays(int days)
    {
        DateTime cutoff = DateTime.Now.AddDays(-days);
        return Entries.Where(e => e.Date >= cutoff).ToList();
    }

    public static int CountEntries(string category, DateTime date, double amount)
    {
        return Entries.Count(e =>
            e.Category.Equals(category, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(e.Amount - amount) < 0.005 &&
            e.Date.Date == date.Date);
    }

    /// <summary>
    /// Sprawdza ostatnie transakcje (np. z 10 dni) i porównuje z zawartością arkusza,
    /// by upewnić się, że wszystkie wpisy istnieją. Zwraca listę brakujących transakcji.
    /// </summary>
    public static List<TransactionLogEntry> FindMissingEntriesInCell(string cellFormula, int days = 10)
    {
        if (string.IsNullOrWhiteSpace(cellFormula))
            return new List<TransactionLogEntry>();

        // wyciągamy liczby z formuły, np. "=10+50+10" -> [10, 50, 10]
        var numbers = cellFormula
            .TrimStart('=')
            .Split(new[] { '+', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => double.TryParse(p, System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out _))
            .Select(p => double.Parse(p, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        var recent = GetEntriesFromLastDays(days);
        var missing = new List<TransactionLogEntry>();

        foreach (var e in recent)
        {
            int occurrencesInFormula = numbers.Count(n => Math.Abs(n - e.Amount) < 0.005);
            int occurrencesInJournal = CountEntries(e.Category, e.Date, e.Amount);

            // jeżeli w formule mniej wystąpień niż w dzienniku -> brakujący wpis
            if (occurrencesInFormula < occurrencesInJournal)
                missing.Add(e);
        }

        return missing;
    }

    public static void ClearCache() => _entriesCache = null;
}
