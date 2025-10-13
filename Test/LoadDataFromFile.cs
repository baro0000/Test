using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test
{
    public class LoadDataFromFile
    {
        public List<Transaction> LoadData(string fileName)       
        {

            // Sprawdzenie czy plik istnieje w katalogu programu
            if (!File.Exists(fileName))
            {
                Console.WriteLine($"Błąd: Plik '{fileName}' nie został znaleziony w folderze programu.");
                List<Transaction> transactionsError1 = new List<Transaction>();
                return transactionsError1;
            }

            List<Transaction> transactions = new List<Transaction>();

            try
            {
                // Wczytanie wszystkich linii z pliku
                string[] lines = File.ReadAllLines(fileName);

                // Pomijamy nagłówek (pierwsza linia)
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];

                    // Pomijamy puste linie lub linie z samymi przecinkami
                    if (string.IsNullOrWhiteSpace(line) || line.Replace(",", "").Trim().Length == 0)
                        continue;

                    // Rozdzielanie pól CSV — uwzględniając cudzysłowy
                    string[] fields = ParseCsvLine(line);

                    if (fields.Length < 11)
                    {
                        Console.WriteLine($"Pomijam błędną linię: {line}");
                        continue;
                    }

                    string numerKonta = fields[0];
                    string dataTransakcji = fields[1];
                    string dataRozliczenia = fields[2];
                    string rodzajTransakcji = fields[3];
                    string zNumeruKonta = fields[4];
                    string odbiorca = fields[5];
                    string opis = fields[6];

                    // Pola numeryczne: zamiana pustych na 0
                    double obciazenia = ParseDouble(fields[7]);
                    double uznania = ParseDouble(fields[8]);
                    double saldo = ParseDouble(fields[9]);
                    string waluta = fields[10];

                    Transaction t = new Transaction(
                        dataTransakcji,
                        dataRozliczenia,
                        rodzajTransakcji,
                        zNumeruKonta,
                        odbiorca,
                        opis,
                        obciazenia,
                        uznania,
                        saldo
                    );

                    transactions.Add(t);
                    
                }
                return transactions;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Wystąpił błąd podczas odczytu pliku: {ex.Message}");
                List<Transaction> transactionsError2 = new List<Transaction>();
                return transactionsError2;
            }
            List<Transaction> transactionsError3 = new List<Transaction>();
            return transactionsError3;
        }
        // Pomocnicza metoda do konwersji string -> double
        private static double ParseDouble(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0.0;

            // Usuwamy ewentualne cudzysłowy i znaki waluty
            value = value.Replace("\"", "").Replace(",", ".").Trim();

            double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double result);
            return result;
        }

        // Parsowanie linii CSV z uwzględnieniem cudzysłowów
        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            string current = "";

            foreach (char c in line)
            {
                if (c == '\"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.Trim());
                    current = "";
                }
                else
                {
                    current += c;
                }
            }

            result.Add(current.Trim());
            return result.ToArray();
        }
    }
}
