using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Test
{
    public class Program
    {
        static void Main(string[] args)
        {
            string fileName = "Wyciag_11_10_25.csv";
            var loader = new LoadDataFromFile();

            // Wczytanie transakcji z pliku
            List<Transaction> transactions = loader.LoadData(fileName);

            // Inicjalizacja menedżera kategorii
            var categoryMenager = new CategoryMenager();

            Console.WriteLine($"\nWczytano {transactions.Count} transakcji z pliku.\n");

            // --- Automatyczne przypisanie kategorii lub dodanie do listy nieznanych ---
            foreach (var t in transactions)
            {
                var cat = categoryMenager.GetCategoryForTransaction(t);

                if (cat != null)
                {
                    // Transakcja rozpoznana na podstawie reguł
                    t.Kategoria = cat;
                }
                else
                {
                    // Brak dopasowania — dodaj do listy nieznanych
                    categoryMenager.UnknownTransactions.Add(t);
                }
            }

            ProgramMenager.HandleUnknownInstances(categoryMenager);

            // --- Podsumowanie ---
            Console.WriteLine($"\n✅ Klasyfikacja zakończona. Łącznie: {transactions.Count} transakcji.\n");

            foreach (var t in transactions)
            {
                t.Prezentuj();
                Console.WriteLine(new string('-', 50));
            }

            Console.WriteLine("\n📘 Aktualne reguły klasyfikacji:");
            categoryMenager.ListRules();

            Console.WriteLine("\nCzy chcesz zaktualizować bodżet? kliknij enter...");
            Console.ReadLine();

            // Po przypisaniu t.Kategoria w twoim flow (jak teraz)
            var budgetPath = "Budżet 2025.xlsx"; // ścieżka do pliku (może być względna)
            var updater = new BudgetUpdater(budgetPath);

            updater.UpdateBudget(transactions);
            Console.WriteLine("Aktualizacja budżetu zakończona.");
        }
    }
}
