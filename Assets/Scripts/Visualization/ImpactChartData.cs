using System;

namespace Simulation
{
    /// Pomocnicze obliczenia dla wykresów i wizualizacji 3D (bez zależności od Unity i XCharts).
    /// Wszystko liczone tymi samymi wzorami co ImpactMath (model Collinsa i in., 2005).
    public static class ImpactChartData
    {
        /// Polskie nazwy progów z BlastDamage.Levels (ta sama kolejność).
        public static readonly string[] BlastLevelNamesPl =
        {
            "Szyby",
            "Dom drewniany",
            "Poszycie ścian",
            "Mur niezbrojony",
            "Konstrukcja stalowa",
            "Budynek z paneli stalowych",
            "Mur ceglany",
            "Całkowite zniszczenie",
        };

        /// Polskie nazwy materiałów z ThermalDamage.Materials (ta sama kolejność).
        public static readonly string[] MaterialNamesPl =
        {
            "Ubranie",
            "Sklejka",
            "Trawa",
            "Gazeta",
            "Drzewa liściaste",
        };

        /// v(z) = v0 * exp(-3 rho0 Cd H e^(-z/H) / (4 rho_i L0 sin(theta)))
        /// Rozwiązanie równania dv/dz ze sprawozdania; dla z = 0 daje dokładnie v_f.
        public static double VelocityAtAltitude(ImpactParameters p, double altitudeM)
        {
            double sin = Math.Sin(Math.Max(p.angleDeg, 1.0) * Math.PI / 180.0);
            double k = 3.0 * ImpactMath.DragCoefficient / (4.0 * p.impactorDensity * p.diameter * sin);
            double exponent = -k * ImpactMath.AirDensity0 * ImpactMath.ScaleHeight
                              * Math.Exp(-altitudeM / ImpactMath.ScaleHeight);
            return p.velocity * Math.Exp(exponent);
        }

        /// Odległość, w której nadciśnienie spada do pPa (bisekcja, p(r) maleje z r).
        public static double RadiusForOverpressure(in ImpactResults res, double pPa)
        {
            double lo = 1.0, hi = 1e8;
            if (ImpactMath.Overpressure(hi, res.energyKt) >= pPa) return hi;
            if (ImpactMath.Overpressure(lo, res.energyKt) <= pPa) return lo;

            for (int i = 0; i < 80; i++)
            {
                double mid = Math.Sqrt(lo * hi);
                if (ImpactMath.Overpressure(mid, res.energyKt) > pPa) lo = mid;
                else hi = mid;
            }
            return Math.Sqrt(lo * hi);
        }

        /// Odległość, w której ekspozycja cieplna spada do phi [J/m^2] (odwrócone phi(r)).
        public static double RadiusForThermal(in ImpactResults res, double exposureJ)
        {
            if (exposureJ <= 0.0) return 0.0;
            return Math.Sqrt(ImpactMath.LuminousEfficiency * res.energyJ / (2.0 * Math.PI * exposureJ));
        }

        /// Najbliższa odległość na wykresach: krawędź krateru (wewnątrz wzory nie mają sensu).
        public static double ChartMinDistanceM(in ImpactResults res)
        {
            return Math.Max(res.CraterRimRadiusM, 50.0);
        }

        /// Najdalsza odległość na wykresach: trochę za strefą wybitych szyb.
        public static double ChartMaxDistanceM(in ImpactResults res)
        {
            double windows = RadiusForOverpressure(res, BlastDamage.Levels[0].thresholdPa) * 1.25;
            return Math.Max(windows, Math.Max(res.CraterRimRadiusM * 4.0, 2000.0));
        }

        /// Próbki odległości: logarytmicznie od krawędzi krateru do maxM.
        /// Logarytmiczny rozkład daje gęste punkty blisko krateru, gdzie wartości zmieniają się najszybciej.
        public static double[] Distances(double minM, double maxM, int count)
        {
            count = Math.Max(2, count);
            var result = new double[count];
            double logMin = Math.Log(minM), logMax = Math.Log(maxM);
            for (int i = 0; i < count; i++)
                result[i] = Math.Exp(logMin + (logMax - logMin) * i / (count - 1));
            return result;
        }

        public static string FormatDistance(double meters)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return meters >= 1000.0
                ? (meters / 1000.0).ToString(meters >= 100000.0 ? "F0" : "F1", ci) + " km"
                : meters.ToString("F0", ci) + " m";
        }

        public static string FormatTime(double seconds)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (seconds < 1.0) return seconds.ToString("F3", ci) + " s";
            if (seconds < 120.0) return seconds.ToString("F1", ci) + " s";
            return (seconds / 60.0).ToString("F1", ci) + " min";
        }
    }

    /// Tablica t_bw(r) liczona raz, żeby w każdej klatce szybko znaleźć promień frontu fali po czasie t.
    /// Całka t_bw(r) = ∫ dr / U(p(r)) liczona przyrostowo metodą punktu środkowego (jak ImpactMath.BlastArrivalTime).
    public sealed class BlastFrontTable
    {
        private readonly double[] radii;
        private readonly double[] times;

        public double MaxRadiusM => radii[radii.Length - 1];
        public double MaxTimeS => times[times.Length - 1];

        public BlastFrontTable(in ImpactResults res, double maxRadiusM, int steps = 4096)
        {
            steps = Math.Max(16, steps);
            radii = new double[steps + 1];
            times = new double[steps + 1];

            double dr = maxRadiusM / steps;
            for (int i = 0; i < steps; i++)
            {
                double rMid = (i + 0.5) * dr;
                double u = ImpactMath.ShockFrontVelocity(ImpactMath.Overpressure(rMid, res.energyKt));
                radii[i + 1] = (i + 1) * dr;
                times[i + 1] = times[i] + dr / u;
            }
        }

        /// Promień frontu fali [m] po czasie t [s] (interpolacja liniowa).
        public double RadiusAt(double timeS)
        {
            if (timeS <= 0.0) return 0.0;
            if (timeS >= MaxTimeS) return MaxRadiusM;

            int lo = 0, hi = times.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (times[mid] <= timeS) lo = mid;
                else hi = mid;
            }
            double f = (timeS - times[lo]) / (times[hi] - times[lo]);
            return radii[lo] + f * (radii[hi] - radii[lo]);
        }

        /// Czas dojścia fali do odległości r [s] (interpolacja liniowa).
        public double TimeAt(double radiusM)
        {
            if (radiusM <= 0.0) return 0.0;
            if (radiusM >= MaxRadiusM) return MaxTimeS;

            double step = radii[1];
            int i = Math.Min((int)(radiusM / step), radii.Length - 2);
            double f = (radiusM - radii[i]) / step;
            return times[i] + f * (times[i + 1] - times[i]);
        }
    }
}
