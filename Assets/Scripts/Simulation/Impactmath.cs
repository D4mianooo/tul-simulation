using System;

namespace Simulation
{
    ///model Collinsa i in., 2005
    /// Jednostki SI (m, kg, s, J, Pa), chyba ¿e nazwa mówi inaczej (Km, Kt, Mt).
    public static class ImpactMath
    {
        public const double G = 9.81;                       // [m/s^2]
        public const double ScaleHeight = 8000.0;           // H [m]
        public const double AirDensity0 = 1.0;              // rho0 [kg/m^3]
        public const double DragCoefficient = 2.0;          // Cd
        public const double AmbientPressure = 1e5;          // Pa [Pa]
        public const double SoundSpeed = 330.0;             // c0 [m/s]
        public const double LuminousEfficiency = 3e-3;      // eta
        public const double FireballTemperature = 3000.0;   // T* [K]
        public const double StefanBoltzmann = 5.67e-8;      // sigma [W/(m^2 K^4)]
        public const double JoulesPerKiloton = 4.184e12;
        public const double JoulesPerMegaton = 4.184e15;

        public const double SimpleToComplexTransitionM = 3200.0; // próg D_tc [m]
        public const double ComplexCraterDc = 2560.0;            // D_c [m]
        public const double FireballMinVelocity = 15000.0;       // [m/s]
        public const double BlastPx = 75000.0;                   // p_x [Pa]
        public const double BlastRx = 289.0;                     // r_x [m]
        public const double EarthRadiusKm = 6371.0;
        public const double SeismicWaveSpeedKmS = 5.0;

        private const double Deg2Rad = Math.PI / 180.0;

        private static double SinAngle(double angleDeg)
        {
            // k¹t 0 daje dzielenie przez zero; UI i tak pilnuje min. 5 stopni
            return Math.Sin(Math.Max(angleDeg, 1.0) * Deg2Rad);
        }

        // ------------------------------------------------------- 1. Impaktor
        public static double Mass(ImpactParameters p)
        {
            double L = p.diameter;
            return Math.PI / 6.0 * p.impactorDensity * L * L * L;
        }

        public static double Energy(ImpactParameters p)
        {
            double v = p.velocity;
            return 0.5 * Mass(p) * v * v;
        }

        public static double ToKilotons(double energyJ) => energyJ / JoulesPerKiloton;
        public static double ToMegatons(double energyJ) => energyJ / JoulesPerMegaton;

        // ----------------------------------------------- 2. Wejœcie w atmosferê
        /// rho(z) = rho0 * e^(-z/H)
        public static double AirDensity(double altitudeM)
        {
            return AirDensity0 * Math.Exp(-altitudeM / ScaleHeight);
        }

        /// log10(Y_i) = 0.0624 * sqrt(rho_i) + 2.107  [Pa]
        public static double Strength(double impactorDensity)
        {
            return Math.Pow(10.0, 0.0624 * Math.Sqrt(impactorDensity) + 2.107);
        }

        /// P_s(z) = rho(z) * v^2  [Pa]
        public static double StagnationPressure(double altitudeM, double velocity)
        {
            return AirDensity(altitudeM) * velocity * velocity;
        }

        /// z_br = H * ln(rho0 * v^2 / Y_i)  [m]. Wartoœæ <= 0 oznacza brak rozpadu.
        public static double BreakupAltitude(ImpactParameters p)
        {
            double v = p.velocity;
            return ScaleHeight * Math.Log(AirDensity0 * v * v / Strength(p.impactorDensity));
        }

        /// dv/dz = 3 rho(z) Cd v / (4 rho_i L0 sin(theta))
        public static double VelocityGradient(ImpactParameters p, double altitudeM, double velocity)
        {
            return 3.0 * AirDensity(altitudeM) * DragCoefficient * velocity
                   / (4.0 * p.impactorDensity * p.diameter * SinAngle(p.angleDeg));
        }

        /// v_f = v0 * exp(-3 rho0 Cd H / (4 rho_i L0 sin(theta)))  [m/s]
        public static double SurfaceVelocity(ImpactParameters p)
        {
            double exponent = -3.0 * AirDensity0 * DragCoefficient * ScaleHeight
                              / (4.0 * p.impactorDensity * p.diameter * SinAngle(p.angleDeg));
            return p.velocity * Math.Exp(exponent);
        }

        // ------------------------------------------------------ 3. Krater i wyrzut
        /// D_tc = 1.161 (rho_i/rho_t)^(1/3) L^0.78 v^0.44 g^-0.22 sin(theta)^(1/3)  [m]
        /// v = prêdkoœæ przy powierzchni (v_f)
        public static double TransientCraterDiameter(ImpactParameters p, double surfaceVelocity)
        {
            return 1.161
                   * Math.Pow(p.impactorDensity / p.targetDensity, 1.0 / 3.0)
                   * Math.Pow(p.diameter, 0.78)
                   * Math.Pow(surfaceVelocity, 0.44)
                   * Math.Pow(G, -0.22)
                   * Math.Pow(SinAngle(p.angleDeg), 1.0 / 3.0);
        }

        /// d_tc = D_tc / (2 sqrt 2)  [m]
        public static double TransientCraterDepth(double transientDiameter)
        {
            return transientDiameter / (2.0 * Math.Sqrt(2.0));
        }

        public static bool IsComplexCrater(double transientDiameter)
        {
            return transientDiameter >= SimpleToComplexTransitionM;
        }

        /// Prosty: D_fr = 1.25 D_tc.  Z³o¿ony: D_fr = 1.17 D_tc^1.13 / D_c^0.13  [m]
        public static double FinalCraterDiameter(double transientDiameter)
        {
            if (!IsComplexCrater(transientDiameter))
                return 1.25 * transientDiameter;

            return 1.17 * Math.Pow(transientDiameter, 1.13) / Math.Pow(ComplexCraterDc, 0.13);
        }

        /// d_fr = 0.294 * D_fr^0.301, oba w km (tylko krater z³o¿ony).
        public static double FinalCraterDepth(double transientDiameter, double finalDiameter)
        {
            if (!IsComplexCrater(transientDiameter))
                return TransientCraterDepth(transientDiameter);

            double finalDiameterKm = finalDiameter / 1000.0;
            return 0.294 * Math.Pow(finalDiameterKm, 0.301) * 1000.0;
        }

        /// tau(r) = D_tc^4 / (112 r^3)  [m]. Wzór ma sens poza krawêdzi¹ krateru.
        public static double EjectaThickness(double transientDiameter, double distanceM)
        {
            if (distanceM <= 0.0) return 0.0;
            double d2 = transientDiameter * transientDiameter;
            return d2 * d2 / (112.0 * distanceM * distanceM * distanceM);
        }

        // ---------------------------------------- 4. Kula ognia i promieniowanie
        public static bool HasFireball(double surfaceVelocity)
        {
            return surfaceVelocity > FireballMinVelocity;
        }

        /// R_fb = 0.002 E^(1/3)  [m]
        public static double FireballRadius(double energyJ)
        {
            return 0.002 * Math.Pow(energyJ, 1.0 / 3.0);
        }

        /// t_max = R_fb / v_f  [s]
        public static double TimeOfMaxRadiation(double fireballRadius, double surfaceVelocity)
        {
            return fireballRadius / surfaceVelocity;
        }

        /// phi(r) = eta E / (2 pi r^2)  [J/m^2]
        public static double ThermalExposure(double energyJ, double distanceM)
        {
            if (distanceM <= 0.0) return double.PositiveInfinity;
            return LuminousEfficiency * energyJ / (2.0 * Math.PI * distanceM * distanceM);
        }

        /// t_irr = eta E / (2 pi R_fb^2 sigma T*^4)  [s]
        public static double IrradiationDuration(double energyJ, double fireballRadius)
        {
            double t4 = Math.Pow(FireballTemperature, 4.0);
            return LuminousEfficiency * energyJ
                   / (2.0 * Math.PI * fireballRadius * fireballRadius * StefanBoltzmann * t4);
        }

        /// phi_ig(E) = phi_ig(1 Mt) * E_Mt^(1/6). Wejœcie w MJ/m^2, wynik w J/m^2.
        public static double IgnitionThreshold(double thresholdAt1MtMJ, double energyJ)
        {
            return thresholdAt1MtMJ * 1e6 * Math.Pow(ToMegatons(energyJ), 1.0 / 6.0);
        }

        // -------------------------------------------------- 5. Wstrz¹sy sejsmiczne
        /// M = 0.67 log10(E) - 5.87
        public static double Magnitude(double energyJ)
        {
            return 0.67 * Math.Log10(energyJ) - 5.87;
        }

        public static double EffectiveMagnitude(double magnitude, double distanceKm)
        {
            if (distanceKm < 60.0)
                return magnitude - 0.0238 * distanceKm;

            if (distanceKm < 700.0)
                return magnitude - 1.1644 - 0.0048 * distanceKm;

            double delta = distanceKm / EarthRadiusKm;
            return magnitude - 6.399 - 1.66 * Math.Log10(delta);
        }

        /// t_s = r_km / 5  [s]
        public static double SeismicArrivalTime(double distanceKm)
        {
            return distanceKm / SeismicWaveSpeedKmS;
        }

        // ---------------------------------------------------- 6. Fala uderzeniowa
        /// r1 = r / E_kt^(1/3)  [m]
        public static double ScaledDistance(double distanceM, double energyKt)
        {
            return distanceM / Math.Pow(energyKt, 1.0 / 3.0);
        }

        /// p(r) = (p_x r_x / (4 r1)) * (1 + 3 (r_x / r1)^1.3)  [Pa]
        public static double Overpressure(double distanceM, double energyKt)
        {
            double r1 = ScaledDistance(distanceM, energyKt);
            if (r1 <= 0.0) return double.PositiveInfinity;
            return BlastPx * BlastRx / (4.0 * r1) * (1.0 + 3.0 * Math.Pow(BlastRx / r1, 1.3));
        }

        /// U(p) = c0 sqrt(1 + 6p / (7 Pa))  [m/s]
        public static double ShockFrontVelocity(double overpressure)
        {
            return SoundSpeed * Math.Sqrt(1.0 + 6.0 * overpressure / (7.0 * AmbientPressure));
        }

        /// u(p) = 5 p c0 / (7 Pa sqrt(1 + 6p / (7 Pa)))  [m/s]
        public static double WindVelocity(double overpressure)
        {
            return 5.0 * overpressure * SoundSpeed
                   / (7.0 * AmbientPressure * Math.Sqrt(1.0 + 6.0 * overpressure / (7.0 * AmbientPressure)));
        }

        /// t_bw(r) = ca³ka od 0 do r z dr / U(p(r))  [s]
        /// Metoda punktu œrodkowego: omija r = 0, gdzie p -> nieskoñczonoœæ (a 1/U -> 0).
        public static double BlastArrivalTime(double distanceM, double energyKt, int steps = 256)
        {
            if (distanceM <= 0.0) return 0.0;

            double dr = distanceM / steps;
            double sum = 0.0;
            for (int i = 0; i < steps; i++)
            {
                double r = (i + 0.5) * dr;
                sum += dr / ShockFrontVelocity(Overpressure(r, energyKt));
            }
            return sum;
        }
    }
}
