#version 330 core

// Tema, varianta C: planeta procedurala.
// Culoarea vine dintr-un zgomot 3D (fbm) calculat din pozitia punctului pe sfera (vLocalPos, normalizata):
// ocean (mai deschis langa tarm), plaja, campie, padure, munte, zapada; calote de gheata la poli;
// nori dintr-un al doilea zgomot, care se misca in timp.

in vec3 vColor;
in vec2 vUV;
in vec3 vLocalPos;

uniform bool uShowUV;
uniform float uTime;          // timpul de joc
uniform vec3 uSeed;           // "samanta" zgomotului (Tween din C# la o planeta noua)
uniform float uSeaLevel;      // inaltimea nivelului marii, in [0, 1]
uniform float uIceLatitude;   // de la ce latitudine (|y| pe sfera) incep calotele de gheata
uniform float uNoiseScale;    // cat de mari sunt continentele (mai mic = mai mari)
uniform int uOctaves;         // cate straturi de zgomot (detaliul tarmului)
uniform float uCloudSpeed;    // viteza de rotatie a norilor, in radiani pe secunda
uniform float uClouds;        // vizibilitatea norilor: 0 = ascunsi, 1 = vizibili (Tween din C#)

out vec4 FragColor;

// Un numar pseudo-aleator in [0, 1), acelasi pentru acelasi punct 3D.
float hash(vec3 p)
{
    p = fract(p * 0.3183099 + 0.1);
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Zgomotul valoare 3D: 8 colturi ale celulei, interpolate pe x, apoi pe y, apoi pe z.
float valueNoise(vec3 p)
{
    vec3 i = floor(p);
    vec3 f = fract(p);
    vec3 u = f * f * (3.0 - 2.0 * f);

    float n000 = hash(i);
    float n100 = hash(i + vec3(1.0, 0.0, 0.0));
    float n010 = hash(i + vec3(0.0, 1.0, 0.0));
    float n110 = hash(i + vec3(1.0, 1.0, 0.0));
    float n001 = hash(i + vec3(0.0, 0.0, 1.0));
    float n101 = hash(i + vec3(1.0, 0.0, 1.0));
    float n011 = hash(i + vec3(0.0, 1.0, 1.0));
    float n111 = hash(i + vec3(1.0, 1.0, 1.0));

    float x00 = mix(n000, n100, u.x);
    float x10 = mix(n010, n110, u.x);
    float x01 = mix(n001, n101, u.x);
    float x11 = mix(n011, n111, u.x);

    float y0 = mix(x00, x10, u.y);
    float y1 = mix(x01, x11, u.y);

    return mix(y0, y1, u.z);
}

// Suma de straturi de zgomot, fiecare de doua ori mai fin si mai slab; rezultat normalizat in [0, 1].
float fbm(vec3 p, int octaves)
{
    float sum = 0.0;
    float amplitude = 0.5;
    float total = 0.0;
    for (int i = 0; i < 8; i++)
    {
        if (i >= octaves) break;
        sum += amplitude * valueNoise(p);
        total += amplitude;
        p = p * 2.0 + vec3(1.7, 9.2, 3.4);
        amplitude *= 0.5;
    }
    return sum / max(total, 0.0001);
}

void main()
{
    if (uShowUV)
    {
        FragColor = vec4(vUV, 0.0, 1.0);
        return;
    }

    vec3 n = normalize(vLocalPos);

    // 1. Inaltimea terenului.
    float h = fbm(n * uNoiseScale + uSeed, uOctaves);
    float aa = fwidth(h) + 0.002;   // latimea marginii netede (aproximativ un pixel)

    // 2. Oceanul: mai inchis in larg, mai deschis langa tarm.
    vec3 deep = vec3(0.02, 0.08, 0.28);
    vec3 shallow = vec3(0.10, 0.45, 0.70);
    vec3 ocean = mix(deep, shallow, smoothstep(uSeaLevel - 0.12, uSeaLevel, h));

    // 3. Uscatul, pe benzi de inaltime (e = 0 la tarm), cu treceri netede intre ele.
    float e = (h - uSeaLevel) / max(1.0 - uSeaLevel, 0.001);
    vec3 ground = vec3(0.86, 0.80, 0.55);                                        // plaja
    ground = mix(ground, vec3(0.35, 0.62, 0.25), smoothstep(0.02, 0.05, e));     // campie
    ground = mix(ground, vec3(0.10, 0.36, 0.15), smoothstep(0.08, 0.14, e));     // padure
    ground = mix(ground, vec3(0.45, 0.40, 0.36), smoothstep(0.18, 0.24, e));     // munte
    ground = mix(ground, vec3(0.96, 0.96, 0.98), smoothstep(0.28, 0.33, e));     // zapada

    float land = smoothstep(uSeaLevel - aa, uSeaLevel + aa, h);
    vec3 color = mix(ocean, ground, land);

    // 4. Calotele de gheata: peste o latitudine, cu margine neregulata (zgomot).
    float iceEdge = uIceLatitude + 0.12 * (valueNoise(n * 7.0 + uSeed) - 0.5);
    float ice = smoothstep(iceEdge - 0.015, iceEdge + 0.015, abs(n.y));
    color = mix(color, vec3(0.92, 0.96, 1.0), ice);

    // 5. Norii: un al doilea zgomot, rotit in jurul axei Y odata cu timpul.
    float a = uTime * uCloudSpeed;
    vec3 q = n;
    q.xz = mat2(cos(a), sin(a), -sin(a), cos(a)) * q.xz;
    float c = fbm(q * 3.0 + vec3(13.1, 7.3, 2.9), 5);
    float clouds = smoothstep(0.52, 0.66, c) * clamp(uClouds, 0.0, 1.0);
    color = mix(color, vec3(1.0), 0.85 * clouds);

    // Umbrirea simpla a sferei (griul primit de la vertex shader).
    FragColor = vec4(color * vColor, 1.0);
}
