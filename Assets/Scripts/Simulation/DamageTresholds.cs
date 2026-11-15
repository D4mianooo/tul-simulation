using System.Collections.Generic;

namespace Simulation
{
    /// Progi zniszczenia od fali uderzeniowej
    public static class BlastDamage
    {
        public struct Level
        {
            public string name;
            public double thresholdPa;
            public string description;

            public Level(string name, double thresholdKPa, string description)
            {
                this.name = name;
                thresholdPa = thresholdKPa * 1000.0;
                this.description = description;
            }
        }

        public static readonly Level[] Levels =
        {
            new Level("Windows",                          1.0,  "typical window glass breaks"),
            new Level("Wood frame house",                 4.8,  "light structural damage, wood panels blown in"),
            new Level("Cladding (wood, corrugated sheet)", 6.9, "wall cladding destroyed"),
            new Level("Unreinforced concrete/block wall", 13.8, "walls crack and collapse"),
            new Level("Steel frame structure",            20.7, "frame distorted, torn from foundation"),
            new Level("Self-framing steel panel building", 20.7, "collapse"),
            new Level("Unreinforced brick wall",          48.3, "walls fail"),
            new Level("Total destruction",                69.0, "buildings likely destroyed"),
        };

        /// Indeks najwy¿szego przekroczonego progu, -1 gdy ¿aden.
        public static int Evaluate(double overpressurePa)
        {
            int result = -1;
            for (int i = 0; i < Levels.Length; i++)
            {
                if (overpressurePa >= Levels[i].thresholdPa)
                    result = i;
            }
            return result;
        }
    }

    public enum BurnDegree
    {
        None = 0,
        First = 1,
        Second = 2,
        Third = 3
    }

    /// Progi zap³onu / oparzeñ phi_ig(1 Mt) [MJ/m^2], skalowane przez E_Mt^(1/6).
    public static class ThermalDamage
    {
        public struct Material
        {
            public string name;
            public double thresholdAt1MtMJ;

            public Material(string name, double thresholdAt1MtMJ)
            {
                this.name = name;
                this.thresholdAt1MtMJ = thresholdAt1MtMJ;
            }
        }

        public static readonly Material[] Materials =
        {
            new Material("Clothing",        1.0),
            new Material("Plywood",         0.67),
            new Material("Grass",           0.38),
            new Material("Newspaper",       0.33),
            new Material("Deciduous trees", 0.25),
        };

        public const double ThirdDegreeAt1MtMJ = 0.42;
        public const double SecondDegreeAt1MtMJ = 0.25;
        public const double FirstDegreeAt1MtMJ = 0.13;

        public static BurnDegree EvaluateBurns(double exposure, double energyJ)
        {
            if (exposure >= ImpactMath.IgnitionThreshold(ThirdDegreeAt1MtMJ, energyJ)) return BurnDegree.Third;
            if (exposure >= ImpactMath.IgnitionThreshold(SecondDegreeAt1MtMJ, energyJ)) return BurnDegree.Second;
            if (exposure >= ImpactMath.IgnitionThreshold(FirstDegreeAt1MtMJ, energyJ)) return BurnDegree.First;
            return BurnDegree.None;
        }

        public static bool IsIgnited(Material material, double exposure, double energyJ)
        {
            return exposure >= ImpactMath.IgnitionThreshold(material.thresholdAt1MtMJ, energyJ);
        }

        /// Nazwy wszystkich materia³ów, które siê zapal¹ przy danej ekspozycji.
        public static List<string> IgnitedMaterials(double exposure, double energyJ)
        {
            var list = new List<string>();
            foreach (var m in Materials)
            {
                if (IsIgnited(m, exposure, energyJ))
                    list.Add(m.name);
            }
            return list;
        }
    }
}
