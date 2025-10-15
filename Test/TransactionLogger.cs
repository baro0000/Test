using System.Text.Json;
using System.Text.Json.Serialization;

public class TransactionLogEntry
{
    public DateTime Date { get; set; }
    public string Category { get; set; } = "";
    public double Amount { get; set; }
    public string Recipient { get; set; } = "";
    public string Sheet { get; set; } = "";
}

public static class TransactionJournal
{
    private static readonly string JournalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "transactions_journal.json");

    public static List<TransactionLogEntry> LoadJournal()
    {
        if (!File.Exists(JournalPath))
            return new List<TransactionLogEntry>();

        string json = File.ReadAllText(JournalPath);
        return JsonSerializer.Deserialize<List<TransactionLogEntry>>(json) ?? new List<TransactionLogEntry>();
    }

    public static void SaveJournal(List<TransactionLogEntry> entries)
    {
        string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(JournalPath, json);
    }

    public static bool Exists(List<TransactionLogEntry> journal, DateTime date, string category, double amount, string recipient)
    {
        return journal.Any(e =>
            e.Date.Date == date.Date &&
            e.Category.Equals(category, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(e.Amount - amount) < 0.001 &&
            e.Recipient.Equals(recipient, StringComparison.OrdinalIgnoreCase));
    }

    public static bool AddEntry(TransactionLogEntry entry)
    {
        var journal = LoadJournal();
        if (Exists(journal, entry.Date, entry.Category, entry.Amount, entry.Recipient))
            return false;

        journal.Add(entry);
        SaveJournal(journal);
        return true;
    }
}
