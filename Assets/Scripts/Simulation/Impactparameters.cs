using System;

/// Dane wejœciowe symulacji (zmienne opisowe ze sprawozdania).
[Serializable]
public struct ImpactParameters
{
    public float diameter;          
    public float impactorDensity;   
    public float velocity;         
    public float angleDeg;         
    public float targetDensity;  

    public ImpactParameters(float diameter, float impactorDensity, float velocity,
                            float angleDeg, float targetDensity)
    {
        this.diameter = diameter;
        this.impactorDensity = impactorDensity;
        this.velocity = velocity;
        this.angleDeg = angleDeg;
        this.targetDensity = targetDensity;
    }
}

/// Statyczny kontener na parametry wybrane na ekranie startowym.
public static class SimulationSettings
{
    public static ImpactParameters Current =
        new ImpactParameters(100f, 3000f, 17000f, 45f, 2500f);
}