using System;

namespace Simulation.City {
    /// <summary>
    /// Population vulnerability models for asteroid impact effects (Rumpf, Lewis, Atkinson 2017).
    /// </summary>
    public static class CasualtyModel {
        public const int EffectCount = 6;

        public static readonly string[] EffectNamesPl = {
            "krater", "promieniowanie cieplne", "wstrząs sejsmiczny",
            "nadciśnienie", "wiatr", "wyrzut"
        };

        /// <summary>
        /// Impact hazard categories tracked by the casualty model.
        /// </summary>
        public enum Effect {
            Crater = 0,
            Thermal = 1,
            Seismic = 2,
            Overpressure = 3,
            Wind = 4,
            Ejecta = 5
        }

        /// <summary>Computes overpressure vulnerability for pressure <paramref name="pPa"/> in Pa.</summary>
        public static double Overpressure(double pPa) => Sigmoid(1.0, -2.424e-5, -4.404e5, pPa);

        /// <summary>Computes wind vulnerability for wind speed <paramref name="uMs"/> in m/s.</summary>
        public static double Wind(double uMs) => Sigmoid(1.0, -5.483e-2, -1.124e2, uMs);

        /// <summary>Computes thermal vulnerability for radiant exposure <paramref name="phiJm2"/> in J/m2.</summary>
        public static double Thermal(double phiJm2) => Sigmoid(0.47, -5.623e-6, -7.316e5, phiJm2);

        /// <summary>Computes seismic vulnerability for effective magnitude <paramref name="mEff"/>.</summary>
        public static double Seismic(double mEff) => Sigmoid(1.0, -2.516, -8.686, mEff);

        /// <summary>Computes ejecta vulnerability for deposit thickness <paramref name="thicknessM"/> in meters.</summary>
        public static double Ejecta(double thicknessM) {
            if (thicknessM <= 0.0) return 0.0;
            double pe = thicknessM * 1600.0 * 9.81 / 1000.0;
            return 0.078 * Math.Pow(1.0 + Math.Exp(-1.37 * (pe - 3.14)), -4.6);
        }

        private static double Sigmoid(double a, double b, double c, double s) {
            double x = b * (s + c);
            if (x > 700.0) return 0.0;
            if (x < -700.0) return a;
            return a / (1.0 + Math.Exp(x));
        }
    }
}
