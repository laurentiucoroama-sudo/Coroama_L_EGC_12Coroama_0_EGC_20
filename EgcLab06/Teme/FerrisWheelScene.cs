// Varianta C: roata panoramică
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Tema, varianta C: roata panoramică.
///  - butucul (un pivot) se rotește în jurul axei Z și poartă spițele, jantele și cele 8 cabine (ierarhie);
///  - fiecare cabină stă pe un pivot care se rotește invers față de roată, deci rămâne verticală;
///  - săgețile sus / jos schimbă viteza țintă, iar viteza reală se apropie lin de ea:
///    v ← v + (v_țintă − v) · (1 − e^(−k·dt)), același rezultat la orice număr de cadre pe secundă;
///  - Space oprește roata lin (și o repornește la a doua apăsare);
///  - tastele 1-8 aduc cabina aleasă jos, la peron, cu un Tween pe unghiul roții, pe drumul cel mai scurt;
///  - bonus: cabinele se leagănă când roata accelerează sau frânează (resort amortizat).
/// Toate animațiile folosesc doar timpul de joc (dt).
/// </summary>
public sealed class FerrisWheelScene : Scene
{
    // Acțiunile scenei.
    private const string SpeedUp = "roata: viteza țintă +";
    private const string SpeedDown = "roata: viteza țintă -";
    private const string StopWheel = "roata: oprește / pornește (lin)";
    private static readonly string[] CabinActions =
        Enumerable.Range(1, CabinCount).Select(n => $"cabina {n} la peron").ToArray();

    // Dimensiuni.
    private const int CabinCount = 8;
    private const int RimSegments = 24;
    private const float Radius = 4.0f;
    private const float RimHalfWidth = 0.5f;            // jantele sunt la z = ±0.5
    private static readonly Vector3 HubPosition = new(0.0f, 5.6f, 0.0f);

    // Viteza roții, în grade pe secundă.
    private const float SpeedStep = 5.0f;
    private const float MaxSpeed = 40.0f;
    private const float Smoothing = 1.5f;               // k din formula de apropiere

    // Legănarea cabinelor (resort amortizat), în grade.
    private const float SwingStiffness = 30.0f;
    private const float SwingDamping = 3.0f;
    private const float SwingGain = 3.0f;
    private const float MaxSwing = 25.0f;

    private static readonly Color4[] CabinColors =
    {
        new(1.0f, 0.35f, 0.35f, 1.0f),
        new(1.0f, 0.65f, 0.25f, 1.0f),
        new(1.0f, 0.9f, 0.3f, 1.0f),
        new(0.45f, 0.9f, 0.4f, 1.0f),
        new(0.3f, 0.85f, 0.85f, 1.0f),
        new(0.35f, 0.55f, 1.0f, 1.0f),
        new(0.65f, 0.45f, 1.0f, 1.0f),
        new(1.0f, 0.45f, 0.8f, 1.0f),
    };

    private SceneObject _hub = null!;
    private readonly SceneObject[] _cabinPivots = new SceneObject[CabinCount];

    private float _angle;            // unghiul roții (grade)
    private float _speed;            // viteza reală (grade / s)
    private float _targetSpeed = 15.0f;
    private float _savedSpeed = 15.0f;
    private Tween? _tween;           // aducerea unei cabine la peron (X = unghiul roții)
    private int _tweenCabin = -1;

    private float _lastOmega;        // viteza unghiulară măsurată la cadrul anterior
    private float _swing;            // unghiul de legănare al cabinelor
    private float _swingVelocity;

    public override string Name => "tema C: roata panoramică";

    public override IEnumerable<string> ActionNames =>
        new[] { SpeedUp, SpeedDown, StopWheel }.Concat(CabinActions);

    public override void Load(Input input)
    {
        CameraPosition = new Vector3(3.0f, 5.0f, 15.0f);
        CameraTarget = new Vector3(0.0f, 4.6f, 0.0f);

        input.Bind(SpeedUp, Keys.Up);
        input.Bind(SpeedDown, Keys.Down);
        input.Bind(StopWheel, Keys.Space);
        for (int i = 0; i < CabinCount; i++)
        {
            input.Bind(CabinActions[i], Keys.D1 + i, Keys.KeyPad1 + i);
        }

        Mesh grid = AddMesh(Shapes.Grid(10, 1.0f, new Color4(0.30f, 0.33f, 0.38f, 1.0f)));
        Mesh cube = AddMesh(Shapes.ShadedCube());
        Mesh cylinder = AddMesh(Shapes.ShadedCylinder(0.5f, 1.0f, 24));

        AddObject("podea", grid);
        BuildSupport(cube);
        BuildWheel(cube, cylinder);
    }

    // --- Construcția scenei ----------------------------------------------------------------

    /// <summary>Peronul și picioarele (fixe, fără părinte).</summary>
    private void BuildSupport(Mesh cube)
    {
        SceneObject platform = AddObject("peron", cube);
        platform.Transform.Position = new Vector3(0.0f, 0.15f, 0.0f);
        platform.Transform.Scale = new Vector3(3.0f, 0.3f, 2.4f);
        platform.Tint = new Color4(0.6f, 0.5f, 0.4f, 1.0f);

        // Două perechi de picioare în formă de A, în fața și în spatele roții.
        foreach (float z in new[] { -0.9f, 0.9f })
        {
            foreach (float footX in new[] { -3.2f, 3.2f })
            {
                Vector3 foot = new(footX, 0.0f, z);
                Vector3 top = new(HubPosition.X, HubPosition.Y, z);
                Vector3 d = top - foot;

                SceneObject leg = AddObject("picior", cube);
                leg.Transform.Position = (foot + top) * 0.5f;
                leg.Transform.Scale = new Vector3(d.Length, 0.18f, 0.18f);
                leg.Transform.Rotation = new Vector3(0.0f, 0.0f, MathHelper.RadiansToDegrees(MathF.Atan2(d.Y, d.X)));
                leg.Tint = new Color4(0.75f, 0.75f, 0.8f, 1.0f);
            }
        }
    }

    /// <summary>Roata: butucul (pivot) și copiii lui.</summary>
    private void BuildWheel(Mesh cube, Mesh cylinder)
    {
        _hub = AddObject("butuc (pivot)", null);
        _hub.Transform.Position = HubPosition;

        // Forma butucului: un cilindru culcat pe axa Z (scalarea stă pe copil, nu pe pivot).
        SceneObject hubShape = AddObject("butuc", cylinder, _hub);
        hubShape.Transform.Rotation = new Vector3(90.0f, 0.0f, 0.0f);
        hubShape.Transform.Scale = new Vector3(0.6f, 2.2f, 0.6f);   // axul ajunge până la picioare
        hubShape.Tint = new Color4(0.5f, 0.5f, 0.55f, 1.0f);

        var steel = new Color4(0.85f, 0.85f, 0.9f, 1.0f);

        foreach (float z in new[] { -RimHalfWidth, RimHalfWidth })
        {
            // Spițele: câte una spre fiecare cabină.
            for (int i = 0; i < CabinCount; i++)
            {
                float a = i * 360.0f / CabinCount;
                float r = MathHelper.DegreesToRadians(a);
                SceneObject spoke = AddObject("spiță", cube, _hub);
                spoke.Transform.Position = new Vector3(0.5f * Radius * MathF.Cos(r), 0.5f * Radius * MathF.Sin(r), z);
                spoke.Transform.Rotation = new Vector3(0.0f, 0.0f, a);
                spoke.Transform.Scale = new Vector3(Radius, 0.08f, 0.08f);
                spoke.Tint = steel;
            }

            // Janta: un poligon cu RimSegments laturi.
            float half = MathF.PI / RimSegments;
            for (int k = 0; k < RimSegments; k++)
            {
                float a = (k + 0.5f) * 360.0f / RimSegments;
                float r = MathHelper.DegreesToRadians(a);
                float distance = Radius * MathF.Cos(half);
                SceneObject segment = AddObject("jantă", cube, _hub);
                segment.Transform.Position = new Vector3(distance * MathF.Cos(r), distance * MathF.Sin(r), z);
                segment.Transform.Rotation = new Vector3(0.0f, 0.0f, a + 90.0f);
                segment.Transform.Scale = new Vector3(2.0f * Radius * MathF.Sin(half) + 0.05f, 0.12f, 0.12f);
                segment.Tint = new Color4(0.95f, 0.95f, 1.0f, 1.0f);
            }
        }

        // Cabinele: pivot pe jantă (copil al butucului) + formele cabinei (copiii pivotului).
        for (int i = 0; i < CabinCount; i++)
        {
            float a = i * 360.0f / CabinCount;
            float r = MathHelper.DegreesToRadians(a);
            var attach = new Vector3(Radius * MathF.Cos(r), Radius * MathF.Sin(r), 0.0f);

            SceneObject axle = AddObject($"ax cabina {i + 1}", cube, _hub);
            axle.Transform.Position = attach;
            axle.Transform.Scale = new Vector3(0.1f, 0.1f, 2.0f * RimHalfWidth);
            axle.Tint = steel;

            SceneObject pivot = AddObject($"pivot cabina {i + 1}", null, _hub);
            pivot.Transform.Position = attach;
            _cabinPivots[i] = pivot;

            SceneObject rod = AddObject("tijă", cube, pivot);
            rod.Transform.Position = new Vector3(0.0f, -0.2f, 0.0f);
            rod.Transform.Scale = new Vector3(0.06f, 0.4f, 0.06f);
            rod.Tint = steel;

            SceneObject roof = AddObject("acoperiș", cube, pivot);
            roof.Transform.Position = new Vector3(0.0f, -0.4f, 0.0f);
            roof.Transform.Scale = new Vector3(1.05f, 0.08f, 0.8f);
            roof.Tint = new Color4(0.35f, 0.35f, 0.4f, 1.0f);

            SceneObject body = AddObject($"cabina {i + 1}", cube, pivot);
            body.Transform.Position = new Vector3(0.0f, -0.8f, 0.0f);
            body.Transform.Scale = new Vector3(0.9f, 0.8f, 0.7f);
            body.Tint = CabinColors[i];
        }

        ApplyAngles();
    }

    // --- Actualizarea, la fiecare cadru ------------------------------------------------------

    public override void Update(float dt, Input input)
    {
        base.Update(dt, input);
        HandleInput(input);

        float omega;   // viteza unghiulară din acest cadru (grade / s), pentru legănare

        if (_tween != null)
        {
            // Aducerea unei cabine la peron: unghiul roții vine din Tween.
            float previousAngle = _angle;
            _tween.Update(dt);
            _angle = _tween.Value.X;
            omega = dt > 0.0f ? (_angle - previousAngle) / dt : 0.0f;
            if (_tween.IsFinished)
            {
                ConsolePanel.PrintMessage($"cabina {_tweenCabin + 1} a ajuns la peron");
                _tween = null;
                _tweenCabin = -1;
            }
        }
        else
        {
            // Viteza reală se apropie lin de viteza țintă (independent de numărul de cadre pe secundă).
            _speed += (_targetSpeed - _speed) * (1.0f - MathF.Exp(-Smoothing * dt));
            _angle += _speed * dt;
            omega = _speed;
        }

        UpdateSwing(omega, dt);

        // Unghiul rămâne în [0, 360) când nu rulează un Tween (doar pentru afișare și precizie).
        if (_tween == null)
        {
            _angle %= 360.0f;
            if (_angle < 0.0f) _angle += 360.0f;
        }

        ApplyAngles();
    }

    private void HandleInput(Input input)
    {
        if (input.WasPressed(SpeedUp)) ChangeTargetSpeed(+SpeedStep);
        if (input.WasPressed(SpeedDown)) ChangeTargetSpeed(-SpeedStep);

        if (input.WasPressed(StopWheel))
        {
            if (_targetSpeed != 0.0f)
            {
                _savedSpeed = _targetSpeed;
                _targetSpeed = 0.0f;
                ConsolePanel.PrintMessage("roata se oprește");
            }
            else if (_tween == null)
            {
                _targetSpeed = _savedSpeed != 0.0f ? _savedSpeed : 15.0f;
                ConsolePanel.PrintMessage($"roata pornește: {_targetSpeed:F0}°/s");
            }
        }

        for (int i = 0; i < CabinCount; i++)
        {
            if (input.WasPressed(CabinActions[i])) BringToPlatform(i);
        }
    }

    private void ChangeTargetSpeed(float delta)
    {
        _tween = null;   // o comandă de viteză anulează aducerea la peron
        _tweenCabin = -1;
        _targetSpeed = Math.Clamp(_targetSpeed + delta, -MaxSpeed, MaxSpeed);
        ConsolePanel.PrintMessage($"viteza țintă: {_targetSpeed:F0}°/s");
    }

    /// <summary>Tween pe unghiul roții, astfel încât cabina <paramref name="index"/> să ajungă jos (-90°).</summary>
    private void BringToPlatform(int index)
    {
        float cabinAngle = index * 360.0f / CabinCount + _angle;
        float delta = WrapAngle(-90.0f - cabinAngle);   // drumul cel mai scurt: cel mult 180°

        if (_targetSpeed != 0.0f) _savedSpeed = _targetSpeed;
        _targetSpeed = 0.0f;
        _speed = 0.0f;

        float duration = MathF.Max(0.6f, MathF.Abs(delta) / 45.0f);   // 180° → 4 s
        _tween = new Tween(new Vector3(_angle, 0.0f, 0.0f), new Vector3(_angle + delta, 0.0f, 0.0f),
                           duration, Easing.EaseInOut);
        _tweenCabin = index;
        ConsolePanel.PrintMessage($"cabina {index + 1} vine la peron ({delta:F0}°, {duration:F1} s)");
    }

    /// <summary>Bonus: cabinele se leagănă la accelerare / frânare (resort amortizat).</summary>
    private void UpdateSwing(float omega, float dt)
    {
        if (dt <= 0.0f) return;   // pauză: totul stă pe loc

        float acceleration = (omega - _lastOmega) / dt;
        _lastOmega = omega;

        float force = -SwingStiffness * _swing - SwingDamping * _swingVelocity - SwingGain * acceleration;
        _swingVelocity += force * dt;
        _swing = Math.Clamp(_swing + _swingVelocity * dt, -MaxSwing, MaxSwing);
    }

    /// <summary>Copiază unghiurile în transformări: roata se rotește, cabinele invers (+ legănarea).</summary>
    private void ApplyAngles()
    {
        _hub.Transform.Rotation = new Vector3(0.0f, 0.0f, _angle);
        foreach (SceneObject pivot in _cabinPivots)
        {
            pivot.Transform.Rotation = new Vector3(0.0f, 0.0f, -_angle + _swing);
        }
    }

    /// <summary>Aduce un unghi în intervalul [-180, 180).</summary>
    private static float WrapAngle(float degrees)
    {
        float a = (degrees + 180.0f) % 360.0f;
        if (a < 0.0f) a += 360.0f;
        return a - 180.0f;
    }

    /// <summary>Cabina cea mai apropiată de peron (unghiul -90°).</summary>
    private int CabinAtPlatform()
    {
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < CabinCount; i++)
        {
            float distance = MathF.Abs(WrapAngle(i * 360.0f / CabinCount + _angle + 90.0f));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    // --- Afișare -----------------------------------------------------------------------------

    public override string Status =>
        _tween != null
            ? $"cabina {_tweenCabin + 1} vine la peron"
            : $"roata {_speed:F1}°/s (țintă {_targetSpeed:F0}°/s) | la peron: cabina {CabinAtPlatform() + 1}";

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        yield return $"Unghi roată    : {_angle:F1}°";
        yield return $"Viteză         : {_speed:F2}°/s   țintă: {_targetSpeed:F0}°/s   (k = {Smoothing})";
        yield return $"Tween          : {(_tween != null ? $"cabina {_tweenCabin + 1}, progres {_tween.Progress:P0}" : "-")}";
        yield return $"Legănare       : {_swing:F1}°   la peron: cabina {CabinAtPlatform() + 1}";
    }
}
