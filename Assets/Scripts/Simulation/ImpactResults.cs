using System;
using System.Globalization;
using System.Text;

namespace Simulation
{
    /// Wyniki niezale¿ne od odleg³oœci (kroki 1-3 z "Kolejnoœci obliczeñ").
    [Serializable]
    public struct ImpactResults
    {
        public ImpactParameters input;

        // 1. Impaktor
        public double massKg;
        public double energyJ;
        public double energyKt;
        public double energyMt;

        // 2. Atmosfera
        public double strengthPa;
        public double breakupAltitudeM;
        public bool breaksUpInAtmosphere;
        public double surfaceVelocity;      // v_f [m/s]

        // 3. Krater
        public double transientDiameterM;
        public double transientDepthM;
        public double finalDiameterM;
        public double finalDepthM;
        public bool isComplexCrater;

        // Kula ognia + sejsmika
        public bool hasFireball;
        public double fireballRadiusM;
        public double timeOfMaxRadiationS;
        public double irradiationDurationS;
        public double magnitude;

        public double CraterRimRadiusM => finalDiameterM * 0.5;

        public static ImpactResults Compute(ImpactParameters p)
        {
            var r = new ImpactResults { input = p };

            // 1. m, E, E_kt
            r.massKg = ImpactMath.Mass(p);
            r.energyJ = ImpactMath.Energy(p);
            r.energyKt = ImpactMath.ToKilotons(r.energyJ);
            r.energyMt = ImpactMath.ToMegatons(r.energyJ);

            // 2. z_br i decyzja; v_f
            r.strengthPa = ImpactMath.Strength(p.impactorDensity);
            r.breakupAltitudeM = ImpactMath.BreakupAltitude(p);
            r.breaksUpInAtmosphere = r.breakupAltitudeM > 0.0;
            r.surfaceVelocity = ImpactMath.SurfaceVelocity(p);

            // 3. Krater
            r.transientDiameterM = ImpactMath.TransientCraterDiameter(p, r.surfaceVelocity);
            r.transientDepthM = ImpactMath.TransientCraterDepth(r.transientDiameterM);
            r.isComplexCrater = ImpactMath.IsComplexCrater(r.transientDiameterM);
            r.finalDiameterM = ImpactMath.FinalCraterDiameter(r.transientDiameterM);
            r.finalDepthM = ImpactMath.FinalCraterDepth(r.transientDiameterM, r.finalDiameterM);

            // Kula ognia
            r.hasFireball = ImpactMath.HasFireball(r.surfaceVelocity);
            if (r.hasFireball)
            {
                r.fireballRadiusM = ImpactMath.FireballRadius(r.energyJ);
                r.timeOfMaxRadiationS = ImpactMath.TimeOfMaxRadiation(r.fireballRadiusM, r.surfaceVelocity);
                r.irradiationDurationS = ImpactMath.IrradiationDuration(r.energyJ, r.fireballRadiusM);
            }

            // Sejsmika
            r.magnitude = ImpactMath.Magnitude(r.energyJ);
            return r;
        }

        public override string ToString()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("=== IMPACT RESULTS ===");
            sb.AppendLine(string.Format(ci, "Input: L0={0:F1} m, rho_i={1:F0} kg/m3, v0={2:F2} km/s, theta={3:F1} deg, rho_t={4:F0} kg/m3",
                input.diameter, input.impactorDensity, input.velocity / 1000.0, input.angleDeg, input.targetDensity));
            sb.AppendLine(string.Format(ci, "Mass: {0:E3} kg", massKg));
            sb.AppendLine(string.Format(ci, "Energy: {0:E3} J = {1:F1} kt = {2:F2} Mt TNT", energyJ, energyKt, energyMt));
            sb.AppendLine(string.Format(ci, "Strength Y_i: {0:E3} Pa", strengthPa));
            sb.AppendLine(breaksUpInAtmosphere
                ? string.Format(ci, "Breakup altitude: {0:F0} m", breakupAltitudeM)
                : "No breakup (Y_i > rho0 v^2)");
            sb.AppendLine(string.Format(ci, "Surface velocity v_f: {0:F2} km/s", surfaceVelocity / 1000.0));
            sb.AppendLine(string.Format(ci, "Transient crater: D_tc={0:F0} m, d_tc={1:F0} m", transientDiameterM, transientDepthM));
            sb.AppendLine(string.Format(ci, "Final crater: D_fr={0:F0} m, d_fr={1:F0} m ({2})",
                finalDiameterM, finalDepthM, isComplexCrater ? "complex" : "simple"));
            sb.AppendLine(hasFireball
                ? string.Format(ci, "Fireball: R_fb={0:F0} m, t_max={1:F3} s, t_irr={2:F1} s",
                    fireballRadiusM, timeOfMaxRadiationS, irradiationDurationS)
                : "No fireball (v_f <= 15 km/s)");
            sb.AppendLine(string.Format(ci, "Seismic magnitude M: {0:F2}", magnitude));
            return sb.ToString();
        }
    }

    /// Skutki w odleg³oœci r od punktu zerowego (krok 4).
    [Serializable]
    public struct EffectsAtDistance
    {
        public double distanceM;
        public bool insideCrater;

        public double overpressurePa;
        public double blastArrivalS;
        public double windVelocity;

        public double thermalExposure;      // [J/m^2], 0 gdy brak kuli ognia
        public double thermalArrivalS;      // t_max

        public double effectiveMagnitude;
        public double seismicArrivalS;

        public double ejectaThicknessM;

        public static EffectsAtDistance Evaluate(in ImpactResults res, double distanceM)
        {
            double rKm = distanceM / 1000.0;
            var e = new EffectsAtDistance
            {
                distanceM = distanceM,
                insideCrater = distanceM <= res.CraterRimRadiusM
            };

            e.overpressurePa = ImpactMath.Overpressure(distanceM, res.energyKt);
            e.blastArrivalS = ImpactMath.BlastArrivalTime(distanceM, res.energyKt);
            e.windVelocity = ImpactMath.WindVelocity(e.overpressurePa);

            if (res.hasFireball)
            {
                e.thermalExposure = ImpactMath.ThermalExposure(res.energyJ, distanceM);
                e.thermalArrivalS = res.timeOfMaxRadiationS;
            }

            e.effectiveMagnitude = ImpactMath.EffectiveMagnitude(res.magnitude, rKm);
            e.seismicArrivalS = ImpactMath.SeismicArrivalTime(rKm);

            e.ejectaThicknessM = e.insideCrater ? 0.0 : ImpactMath.EjectaThickness(res.transientDiameterM, distanceM);
            return e;
        }

        public override string ToString()
        {
            var ci = CultureInfo.InvariantCulture;
            return string.Format(ci,
                "r={0:F0} m | p={1:F1} kPa, t_bw={2:F2} s, u={3:F1} m/s | phi={4:F2} MJ/m2 | M_eff={5:F2}, t_s={6:F2} s | tau={7:F3} m{8}",
                distanceM, overpressurePa / 1000.0, blastArrivalS, windVelocity,
                thermalExposure / 1e6, effectiveMagnitude, seismicArrivalS, ejectaThicknessM,
                insideCrater ? " (inside crater)" : "");
        }
    }
}
