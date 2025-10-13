using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Test
{
    public class CategoryMenager
    {
        private Dictionary<string, Category> rules; // np. "biedronka" -> "Żywność"
        private string rulesFile = "rules.json";    // plik do zapisu wiedzy
        public List<Transaction> UnknownTransactions { get; private set; } = new List<Transaction>();

        public CategoryMenager()
        {
            if (File.Exists(rulesFile))
            {
                string json = File.ReadAllText(rulesFile);
                rules = JsonSerializer.Deserialize<Dictionary<string, Category>>(json)
                        ?? new Dictionary<string, Category>();
            }
            else
            {
                rules = new Dictionary<string, Category>();
            }
        }

        /// <summary>
        /// Przypisuje kategorię do transakcji, jeśli znana.
        /// Jeśli nie — dodaje do listy UnknownTransactions.
        /// </summary>
        public void AssignCategory(Transaction t)
        {
            var category = GetCategoryForTransaction(t);
            if (category != null)
            {
                t.Kategoria = category;
            }
            else
            {
                UnknownTransactions.Add(t);
            }
        }

        /// <summary>
        /// Szuka kategorii pasującej do transakcji (na podstawie opisu lub odbiorcy).
        /// </summary>
        public Category GetCategoryForTransaction(Transaction t)
        {
            if (string.IsNullOrWhiteSpace(t.Opis) && string.IsNullOrWhiteSpace(t.Odbiorca))
                return null;

            foreach (var kvp in rules)
            {
                string keyword = kvp.Key.ToLower();

                if ((t.Opis?.ToLower().Contains(keyword) ?? false) ||
                    (t.Odbiorca?.ToLower().Contains(keyword) ?? false))
                {
                    return kvp.Value;
                }
            }

            return null; // nieznana kategoria
        }

        /// <summary>
        /// Dodaje nową regułę do bazy wiedzy.
        /// </summary>
        public void AddRule(string keyword, Category category, bool applyToAll)
        {
            keyword = keyword.ToLower();

            if (applyToAll)
            {
                // Zapisujemy regułę do pliku
                rules[keyword] = category;
                SaveRules();
            }

            Console.WriteLine($"✅ Dodano nową regułę: '{keyword}' → {category.Name}");
        }

        private void SaveRules()
        {
            string json = JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(rulesFile, json);
        }

        /// <summary>
        /// Wypisuje wszystkie znane reguły.
        /// </summary>
        public void ListRules()
        {
            Console.WriteLine("\n📘 Aktualne reguły klasyfikacji:");
            foreach (var r in rules)
            {
                Console.WriteLine($" - {r.Key} → {r.Value.Name} ({r.Value.Type})");
            }
        }

        /// <summary>
        /// Pokazuje listę nierozpoznanych transakcji.
        /// </summary>
        public void ShowUnknownTransactions()
        {
            if (UnknownTransactions.Count == 0)
            {
                Console.WriteLine("\n✅ Wszystkie transakcje zostały rozpoznane.");
                return;
            }

            Console.WriteLine($"\n⚠️ Nierozpoznane transakcje ({UnknownTransactions.Count}):\n");
            foreach (var t in UnknownTransactions)
            {
                Console.WriteLine($"Data: {t.DataTransakcji} | Odbiorca: {t.Odbiorca} | Opis: {t.Opis}");
                Console.WriteLine(new string('-', 50));
            }
        }
    }
}
 