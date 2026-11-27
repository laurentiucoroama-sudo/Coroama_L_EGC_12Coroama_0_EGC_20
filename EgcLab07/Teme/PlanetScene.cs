// Varianta C: planeta
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Tema, varianta C: planeta. O sferă colorată în întregime de fragment shader (Shaders/planet.frag):
/// zgomot 3D din poziția punctului pe sferă → ocean, plajă, câmpie, pădure, munte, zăpadă, calote de gheață,
/// plus nori care se mișcă în timp. C# calculează doar valori și le trimite ca uniforme:
///  - G = planetă nouă: sămânța zgomotului (vec3) trece lin spre o valoare aleatoare (Tween);
///  - Space = ascunde / arată norii, lin (Tween pe uClouds);
///  - săgețile reglează nivelul mării, latitudinea gheții, scara zgomotului, octavele și viteza norilor.
/// </summary>
public sealed class PlanetScene : Scene
{
    private const string NewPlanet = "planetă nouă (sămânța zgomotului)";
    private const string ToggleClouds = "norii: ascunde / arată (lin)";

    private const float SeedDuration = 2.5f;    // secunde pentru transformarea continentelor
    private const float CloudDuration = 1.0f;   // secunde pentru apariția / dispariția norilor

    private Material _planet = null!;
    private Tween _seed = null!;     // sămânța zgomotului
    private Tween _clouds = null!;   // X = vizibilitatea norilor (0 .. 1)
    private bool _cloudsOn = true;
    private int _planetNumber = 1;
    private readonly Random _random = new();

    public override string Name => "tema C: planeta";

    public override IEnumerable<string> ActionNames => new[] { NewPlanet, ToggleClouds };

    protected override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 0.8f, 6.0f);
        CameraTarget = Vector3.Zero;

        input.Bind(NewPlanet, Keys.G);
        input.Bind(ToggleClouds, Keys.Space);

        Mesh sphere = AddMesh(Shapes.ShadedSphere(1.0f, 96, 48));

        // Uniformele reglabile cu săgețile (Param) + uniformele animate din C# (uSeed, uClouds).
        _planet = AddMaterial("planetă", "procedural.vert", "planet.frag")
            .Param("uSeaLevel", 0.3f, 0.7f, 0.5f)
            .Param("uIceLatitude", 0.5f, 1.0f, 0.82f)
            .Param("uNoiseScale", 0.5f, 6.0f, 2.2f)
            .Param("uOctaves", 1, 8, 6)
            .Param("uCloudSpeed", 0.0f, 0.5f, 0.06f);

        Vector3 seed = RandomSeed();
        _seed = new Tween(seed, seed, SeedDuration, Easing.EaseInOut);
        _clouds = new Tween(Vector3.One, Vector3.One, CloudDuration, Easing.EaseInOut);
        _planet.Uniforms.Set("uSeed", seed);
        _planet.Uniforms.Set("uClouds", 1.0f);

        SceneObject planet = AddObject("planetă", sphere, _planet);
        planet.Transform.Scale = new Vector3(1.8f);
        planet.Transform.Rotation = new Vector3(0.0f, 0.0f, 15.0f);   // axa înclinată
        planet.Spin = new Vector3(0.0f, 8.0f, 0.0f);                   // rotația proprie (timp de joc)
    }

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input);   // Spin

        if (input.WasPressed(NewPlanet))
        {
            _planetNumber++;
            _seed.GoTo(RandomSeed(), SeedDuration);
            ConsolePanel.PrintMessage($"planeta #{_planetNumber}: continentele se transformă");
        }
        if (input.WasPressed(ToggleClouds))
        {
            _cloudsOn = !_cloudsOn;
            _clouds.GoTo(_cloudsOn ? Vector3.One : Vector3.Zero, CloudDuration);
            ConsolePanel.PrintMessage(_cloudsOn ? "norii apar" : "norii dispar");
        }

        // Tween-urile avansează cu timpul de joc; valorile lor se copiază în uniforme la fiecare cadru.
        _seed.Update(dt);
        _clouds.Update(dt);
        _planet.Uniforms.Set("uSeed", _seed.Value);
        _planet.Uniforms.Set("uClouds", _clouds.Value.X);
    }

    private Vector3 RandomSeed() =>
        new(_random.NextSingle() * 50.0f, _random.NextSingle() * 50.0f, _random.NextSingle() * 50.0f);

    public override string Status =>
        $"planeta #{_planetNumber}{(_seed.IsFinished ? "" : " (se transformă)")} | nori: {_clouds.Value.X * 100.0f:F0}%";

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        Vector3 s = _seed.Value;
        yield return $"Planeta        : #{_planetNumber}   sămânța: ({s.X:F1}, {s.Y:F1}, {s.Z:F1})";
        yield return $"Nori           : {(_cloudsOn ? "pornit" : "oprit")}   vizibilitate {_clouds.Value.X:F2}";
    }
}
