
namespace Test
{
    public class Transaction
    {
        public string NumerKonta = "PL45 1160 2202 0000 0003 5269 8493";
        public string DataTransakcji;
        public string DataRozliczenia;
        public string RodzajTransakcji;
        public string ZNumeruKonta;
        public string Odbiorca;
        public string Opis;
        public double Kwota;
        public double Saldo;
        public string Waluta = "PLN";
        public Category? Kategoria = null;

        public Transaction()
        {
            
        }

        public Transaction( string dataTransakcji, string dataRozliczenia, string rodzajTransakcji, string zNumeruKonta, string odbiorca, string opis, double obciazenia, double uznania, double saldo)
        {
            DataTransakcji = dataTransakcji;
            DataRozliczenia = dataRozliczenia;
            RodzajTransakcji = rodzajTransakcji;
            ZNumeruKonta = zNumeruKonta;
            Odbiorca = odbiorca;
            Opis = opis;
            if (obciazenia < 0)
            {
                Kwota = obciazenia;
            }
            else if (uznania > 0)
            {
                Kwota = uznania;
            }
            else
            {
                Console.WriteLine("Błąd Przypisania kwoty");
            }
            Saldo = saldo;
        }

        public void Uzupelnij()
        {
            Console.WriteLine("Numer rachunku: " + NumerKonta);
            Console.WriteLine("Data transakcji: " + DataTransakcji);
            Console.WriteLine("Data rozliczenia: " + DataRozliczenia);
            Console.WriteLine("Rodzaj transakcji: " + RodzajTransakcji);
            Console.WriteLine("Z numeru konta: " + ZNumeruKonta);
            Console.WriteLine("Odbiorca: " + Odbiorca);
            Console.WriteLine("Opis: " + Opis);
            Console.WriteLine("Kwota: " + Kwota);
            Console.WriteLine("Saldo: " + Saldo);
            Console.WriteLine("Waluta: " + Waluta);
        }

        public void Prezentuj()
        {
            Uzupelnij();
            Console.WriteLine("Kategoria: " + Kategoria.Name);
            Console.WriteLine("Kategoria: " + Kategoria.Type);
        }


    }
}
 