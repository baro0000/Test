using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test
{
    static class ProgramMenager
    {
        public static void HandleUnknownInstances(CategoryMenager categoryMenager)
        {
            // --- Obsługa transakcji nierozpoznanych ---
            if (categoryMenager.UnknownTransactions.Count > 0)
            {
                Console.WriteLine("\n🔍 Wykryto transakcje bez przypisanej kategorii.\n");

                foreach (var t in categoryMenager.UnknownTransactions)
                {
                    Console.WriteLine("--------------------------------------------------");
                    t.Uzupelnij();

                    Console.WriteLine("Wybierz kategorię z listy:");

                    bool isIncome = t.Kwota > 0;

                    // lista przychodów
                    var incomeCategories = new[]
                    {
    CategoryName.Bartek,
    CategoryName.Gosia,
    CategoryName.INNE
};

                    // lista kosztów
                    var expenseCategories = Enum.GetValues(typeof(CategoryName))
                        .Cast<CategoryName>()
                        .Except(incomeCategories)
                        .ToList();

                    // wybór tylko odpowiednich kategorii
                    var availableCategories = isIncome
    ? incomeCategories.ToList()
    : expenseCategories;

                    foreach (var cat in availableCategories)
                    {
                        Console.WriteLine($" - {cat}");
                    }

                    CategoryName selectedCategory;
                    while (true)
                    {
                        Console.Write("Podaj nazwę kategorii: ");
                        string input = Console.ReadLine();

                        if (Enum.TryParse(input, true, out selectedCategory) &&
                            availableCategories.Contains(selectedCategory))
                        {
                            break;
                        }

                        Console.WriteLine("❌ Niepoprawna lub niedozwolona kategoria. Spróbuj ponownie.");
                    }

                    // Ustalenie typu transakcji na podstawie kwot
                    string type = (t.Kwota > 0) ? "Uznanie" :
                                        (t.Kwota < 0) ? "Obciążenie" : "";

                    if (string.IsNullOrEmpty(type))
                    {
                        Console.WriteLine("⚠️  Nie można automatycznie określić typu transakcji. Podaj ręcznie:");
                        Console.Write("Czy to uznanie (U) czy obciążenie (O)? ");
                        string userType = Console.ReadLine().Trim().ToUpper();
                        type = userType.StartsWith("U") ? "Uznanie" : "Obciążenie";
                    }

                    Console.Write("Czy chcesz, aby ten wzorzec był stosowany w przyszłości (T/N)? ");
                    bool zapamietaj = Console.ReadLine().Trim().ToUpper().StartsWith("T");

                    string keyword = "";
                    if (zapamietaj)
                    {
                        Console.Write("Podaj słowo kluczowe do rozpoznawania tej kategorii (np. biedronka): ");
                        keyword = Console.ReadLine().Trim();
                    }

                    // Utworzenie kategorii i przypisanie
                    var newCat = new Category { Name = selectedCategory, Type = type };
                    t.Kategoria = newCat;

                    // Dodanie reguły (jeśli zapamiętana — zapisze się do pliku)
                    categoryMenager.AddRule(keyword, newCat, zapamietaj);
                }
            }
        }
    }
}
 