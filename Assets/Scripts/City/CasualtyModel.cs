using System;

namespace Simulation
{
    /// Modele śmiertelności (vulnerability) wg Rumpf, Lewis, Atkinson (2017),
    /// "Population vulnerability models for asteroid impact risk assessment",
    /// Meteoritics & Planetary Science 52(6), 1082–1102, doi:10.1111/maps.12861.
    ///
    /// Postać ogólna (równanie 3):  V(S) = a / (1 + e^(b·(S + c))),
    /// gdzie S to natężenie zjawiska (te same wielkości, które liczy model Collinsa).
    /// Użyte są parametry wariantu "expected" (oczekiwanego).
    /// Łączenie zjawisk (równanie 4): V = 1 − Π(1 − V_i)  (szansa przeżycia wszystkich po kolei).
    public static class CasualtyModel
    {
        public enum Effect
        {
            Crater = 0,
            Thermal = 1,
            Seismic = 2,
            Overpressure = 3,
            Wind = 4,
            Ejecta = 5
        }

        public const int EffectCount = 6;

        public static readonly string[] EffectNamesPl =
        {
            "krater", "promieniowanie cieplne", "wstrząs sejsmiczny",
            "nadciśnienie", "wiatr", "wyrzut"
        };

        private static double Sigmoid(double a, double b, double c, double s)
        {
            double x = b * (s + c);
            if (x > 700.0) return 0.0;
            if (x < -700.0) return a;
            return a / (1.0 + Math.Exp(x));
        }

        /// Tabela 1: nadciśnienie p [Pa]. V = 0,5 przy p ≈ 440 kPa.
        public static double Overpressure(double pPa) => Sigmoid(1.0, -2.424e-5, -4.404e5, pPa);

        /// Tabela 3: prędkość wiatru u [m/s]. V = 0,5 przy u ≈ 112 m/s.
        public static double Wind(double uMs) => Sigmoid(1.0, -5.483e-2, -1.124e2, uMs);

        /// Tabela 4: ekspozycja cieplna φ [J/m²]. a = 0,47: 22% ludzi na zewnątrz + 25% za oknami;
        /// pozostali są osłonięci ścianami.
        public static double Thermal(double phiJm2) => Sigmoid(0.47, -5.623e-6, -7.316e5, phiJm2);

        /// Tabela 5: magnituda efektywna M_eff. V = 0,5 przy M_eff ≈ 8,7.
        public static double Seismic(double mEff) => Sigmoid(1.0, -2.516, -8.686, mEff);

        /// Równanie 34: obciążenie warstwą wyrzutu p_e = τ·ρ_e·g0 (ρ_e = 1600 kg/m³), przyjęte w kPa.
        /// Maksimum 0,078 = 0,78 (w budynkach) × 0,2 (przysypani) × 0,5 (śmiertelnie).
        public static double Ejecta(double thicknessM)
        {
            if (thicknessM <= 0.0) return 0.0;
            double pe = thicknessM * 1600.0 * 9.81 / 1000.0;
            return 0.078 * Math.Pow(1.0 + Math.Exp(-1.37 * (pe - 3.14)), -4.6);
        }
    }
}
