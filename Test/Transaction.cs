
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
        public double Obciazenia;
        public double Uznania;
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
            Obciazenia = obciazenia;
            Uznania = uznania;
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
            Console.WriteLine("Obciazenia: " + Obciazenia);
            Console.WriteLine("Uznania: " + Uznania);
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
