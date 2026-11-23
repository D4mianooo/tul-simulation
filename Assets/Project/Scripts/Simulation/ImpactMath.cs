using System;

namespace Simulation {

    public static class ImpactMath {
        public const double G = 9.81;
        public const double ScaleHeight = 8000.0;
        public const double AirDensity0 = 1.0;
        public const double DragCoefficient = 2.0;
        public const double AmbientPressure = 1e5;
        public const double SoundSpeed = 330.0;
        public const double LuminousEfficiency = 3e-3;
        public const double FireballTemperature = 3000.0;
        public const double StefanBoltzmann = 5.67e-8;
        public const double JoulesPerKiloton = 4.184e12;
        public const double JoulesPerMegaton = 4.184e15;

        public const double SimpleToComplexTransitionM = 3200.0;
        public const double ComplexCraterDc = 2560.0;
        public const double FireballMinVelocity = 15000.0;
        public const double BlastPx = 75000.0;
        public const double BlastRx = 289.0;
        public const double EarthRadiusKm = 6371.0;
        public const double SeismicWaveSpeedKmS = 5.0;

        private const double Deg2Rad = Math.PI / 180.0;

        private static double SinAngle(double angleDeg) {

            return Math.Sin(Math.Max(angleDeg, 1.0) * Deg2Rad);
        }

        public static double Mass(ImpactParameters p) {
            double L = p.diameter;
            return Math.PI / 6.0 * p.impactorDensity * L * L * L;
        }

        public static double Energy(ImpactParameters p) {
            double v = p.velocity;
            return 0.5 * Mass(p) * v * v;
        }

        public static double ToKilotons(double energyJ) => energyJ / JoulesPerKiloton;
        public static double ToMegatons(double energyJ) => energyJ / JoulesPerMegaton;

        public static double AirDensity(double altitudeM) {
            return AirDensity0 * Math.Exp(-altitudeM / ScaleHeight);
        }

        public static double Strength(double impactorDensity) {
            return Math.Pow(10.0, 0.0624 * Math.Sqrt(impactorDensity) + 2.107);
        }

        public static double StagnationPressure(double altitudeM, double velocity) {
            return AirDensity(altitudeM) * velocity * velocity;
        }

        public static double BreakupAltitude(ImpactParameters p) {
            double v = p.velocity;
            return ScaleHeight * Math.Log(AirDensity0 * v * v / Strength(p.impactorDensity));
        }

        public static double VelocityGradient(ImpactParameters p, double altitudeM, double velocity) {
            return 3.0 * AirDensity(altitudeM) * DragCoefficient * velocity
                   / (4.0 * p.impactorDensity * p.diameter * SinAngle(p.angleDeg));
        }

        public static double SurfaceVelocity(ImpactParameters p) {
            double exponent = -3.0 * AirDensity0 * DragCoefficient * ScaleHeight
                              / (4.0 * p.impactorDensity * p.diameter * SinAngle(p.angleDeg));
            return p.velocity * Math.Exp(exponent);
        }

        public static double TransientCraterDiameter(ImpactParameters p, double surfaceVelocity) {
            return 1.161
                   * Math.Pow(p.impactorDensity / p.targetDensity, 1.0 / 3.0)
                   * Math.Pow(p.diameter, 0.78)
                   * Math.Pow(surfaceVelocity, 0.44)
                   * Math.Pow(G, -0.22)
                   * Math.Pow(SinAngle(p.angleDeg), 1.0 / 3.0);
        }

        public static double TransientCraterDepth(double transientDiameter) {
            return transientDiameter / (2.0 * Math.Sqrt(2.0));
        }

        public static bool IsComplexCrater(double transientDiameter) {
            return transientDiameter >= SimpleToComplexTransitionM;
        }

        public static double FinalCraterDiameter(double transientDiameter) {
            if (!IsComplexCrater(transientDiameter))
                return 1.25 * transientDiameter;

            return 1.17 * Math.Pow(transientDiameter, 1.13) / Math.Pow(ComplexCraterDc, 0.13);
        }

        public static double FinalCraterDepth(double transientDiameter, double finalDiameter) {
            if (!IsComplexCrater(transientDiameter))
                return TransientCraterDepth(transientDiameter);

            double finalDiameterKm = finalDiameter / 1000.0;
            return 0.294 * Math.Pow(finalDiameterKm, 0.301) * 1000.0;
        }

        public static double EjectaThickness(double transientDiameter, double distanceM) {
            if (distanceM <= 0.0) return 0.0;
            double d2 = transientDiameter * transientDiameter;
            return d2 * d2 / (112.0 * distanceM * distanceM * distanceM);
        }

        public static bool HasFireball(double surfaceVelocity) {
            return surfaceVelocity > FireballMinVelocity;
        }

        public static double FireballRadius(double energyJ) {
            return 0.002 * Math.Pow(energyJ, 1.0 / 3.0);
        }

        public static double TimeOfMaxRadiation(double fireballRadius, double surfaceVelocity) {
            return fireballRadius / surfaceVelocity;
        }

        public static double ThermalExposure(double energyJ, double distanceM) {
            if (distanceM <= 0.0) return double.PositiveInfinity;
            return LuminousEfficiency * energyJ / (2.0 * Math.PI * distanceM * distanceM);
        }

        public static double IrradiationDuration(double energyJ, double fireballRadius) {
            double t4 = Math.Pow(FireballTemperature, 4.0);
            return LuminousEfficiency * energyJ
                   / (2.0 * Math.PI * fireballRadius * fireballRadius * StefanBoltzmann * t4);
        }

        public static double IgnitionThreshold(double thresholdAt1MtMJ, double energyJ) {
            return thresholdAt1MtMJ * 1e6 * Math.Pow(ToMegatons(energyJ), 1.0 / 6.0);
        }

        public static double Magnitude(double energyJ) {
            return 0.67 * Math.Log10(energyJ) - 5.87;
        }

        public static double EffectiveMagnitude(double magnitude, double distanceKm) {
            if (distanceKm < 60.0)
                return magnitude - 0.0238 * distanceKm;

            if (distanceKm < 700.0)
                return magnitude - 1.1644 - 0.0048 * distanceKm;

            double delta = distanceKm / EarthRadiusKm;
            return magnitude - 6.399 - 1.66 * Math.Log10(delta);
        }

        public static double SeismicArrivalTime(double distanceKm) {
            return distanceKm / SeismicWaveSpeedKmS;
        }

        public static double ScaledDistance(double distanceM, double energyKt) {
            return distanceM / Math.Pow(energyKt, 1.0 / 3.0);
        }

        public static double Overpressure(double distanceM, double energyKt) {
            double r1 = ScaledDistance(distanceM, energyKt);
            if (r1 <= 0.0) return double.PositiveInfinity;
            return BlastPx * BlastRx / (4.0 * r1) * (1.0 + 3.0 * Math.Pow(BlastRx / r1, 1.3));
        }

        public static double ShockFrontVelocity(double overpressure) {
            return SoundSpeed * Math.Sqrt(1.0 + 6.0 * overpressure / (7.0 * AmbientPressure));
        }

        public static double WindVelocity(double overpressure) {
            return 5.0 * overpressure * SoundSpeed
                   / (7.0 * AmbientPressure * Math.Sqrt(1.0 + 6.0 * overpressure / (7.0 * AmbientPressure)));
        }

        public static double BlastArrivalTime(double distanceM, double energyKt, int steps = 256) {
            if (distanceM <= 0.0) return 0.0;

            double dr = distanceM / steps;
            double sum = 0.0;
            for (int i = 0; i < steps; i++) {
                double r = (i + 0.5) * dr;
                sum += dr / ShockFrontVelocity(Overpressure(r, energyKt));
            }
            return sum;
        }
    }
}
