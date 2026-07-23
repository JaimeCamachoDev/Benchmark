#ifndef LLUVIA_SALPICADURAS_INCLUDED
#define LLUVIA_SALPICADURAS_INCLUDED

// Original noise / raindrop-field functions for the "Lluvia Salpicaduras" fullscreen effect.
// Hash constants follow Inigo Quilez's widely published, public "value noise" hash
// (https://iquilezles.org/articles/), not any third-party shader's specific algorithm.

float2 Hash2(float2 p, float seed)
{
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return frac(sin(p + seed) * 43758.5453123);
}

float Hash1(float2 p, float seed)
{
    return Hash2(p, seed).x;
}

// Analytic spherical-cap "lens" surface: models a droplet as a hemisphere bump, so the
// resulting normal bends light the way real water does (little bending at the center,
// strong bending near the rim). The boundary gets only a very subtle single-harmonic
// wobble - just enough to avoid a perfectly mechanical circle, not enough to read as a star.
// Returns (mask, normalX, normalY, rim) - all already weighted by the soft edge mask.
float4 DropSurface(float2 local, float radius, float edgeSeed)
{
    if (radius <= 0.0)
        return float4(0.0, 0.0, 0.0, 0.0);

    float angle = atan2(local.y, local.x);
    float wobble = sin(angle * 3.0 + edgeSeed * 11.0) * 0.035;
    float r = radius * (1.0 + wobble);

    float d2 = dot(local, local);
    float r2 = r * r;
    float mask = smoothstep(r2, r2 * 0.75, d2);
    if (mask <= 0.0)
        return float4(0.0, 0.0, 0.0, 0.0);

    float zSq = max(r2 - d2, 0.0);
    float z = sqrt(zSq);
    float3 n = normalize(float3(local, z + 1e-4));

    float rim = pow(saturate(1.0 - n.z), 4.0);

    return float4(mask, n.x * mask, n.y * mask, rim * mask);
}

// Picks whichever of two overlapping drop contributions sits "on top" (bigger mask),
// so overlapping drops don't double up their refraction.
float4 CombineDrop(float4 a, float4 b)
{
    return (b.x > a.x) ? b : a;
}

// Closest point on segment [a,b] to p. `h` (0..1) is how far along the segment that is,
// used to fade/taper things along the segment's length.
float2 SegmentClosest(float2 p, float2 a, float2 b, out float h)
{
    float2 pa = p - a;
    float2 ba = b - a;
    h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
    return a + ba * h;
}

float4 StaticLayer(float2 uv, float uvScale, float density, float seed, float layerOffset)
{
    float2 grid = uv * uvScale;
    float2 cell = floor(grid);
    float2 cellUV = frac(grid) - 0.5;

    float2 rnd = Hash2(cell + layerOffset, seed);
    float visible = (Hash1(cell + layerOffset + 19.7, seed) < density) ? 1.0 : 0.0;
    if (visible <= 0.0)
        return float4(0.0, 0.0, 0.0, 0.0);

    float2 center = (rnd - 0.5) * 0.55;
    float radius = lerp(0.06, 0.15, rnd.y);

    return DropSurface(cellUV - center, radius, rnd.x * 23.0 + layerOffset);
}

// Static droplets: a coarse layer of small splashes plus an even finer layer of specks,
// combined so both scales of detail show up like real splattered rain - kept deliberately
// small/sparse so clean, undisturbed water is still the majority of the screen.
float4 StaticDroplets(float2 uv, float uvScale, float density, float seed)
{
    float4 big = StaticLayer(uv, uvScale, density, seed, 0.0);
    float4 small = StaticLayer(uv, uvScale * 2.4, density * 0.6, seed, 97.0);
    return CombineDrop(big, small);
}

// Rolling droplets: vertical columns scroll over time; each active cell has a small round
// head and a single thin trail streak running behind it. The head/tail positions depend
// only on the cell's own hash (never on the shading pixel's own coordinate), so the shape
// is fixed in space and doesn't warp from pixel to pixel while being rasterized.
float4 RollingDroplets(float2 uv, float uvScale, float time, float density, float seed)
{
    float columnCount = uvScale;
    float rowCount = uvScale * 1.8;

    float column = floor(uv.x * columnCount);
    float colSeed = Hash1(float2(column, 11.0), seed);
    float cellsPerSecond = lerp(0.35, 0.9, colSeed);

    float scrolledY = uv.y * rowCount + time * cellsPerSecond;
    float row = floor(scrolledY);
    float2 cellID = float2(column, row);

    float visible = (Hash1(cellID + 7.3, seed) < density) ? 1.0 : 0.0;
    if (visible <= 0.0)
        return float4(0.0, 0.0, 0.0, 0.0);

    float2 rnd = Hash2(cellID, seed);
    float2 cellUV = float2(frac(uv.x * columnCount) - 0.5, frac(scrolledY) - 0.5);

    // Everything below depends only on `cellID` (via rnd/colSeed), so it is constant for
    // every pixel that belongs to this drop - no per-pixel wiggling.
    float2 headCenter = float2((rnd.x - 0.5) * 0.3, lerp(-0.4, -0.18, rnd.y));
    float headRadius = lerp(0.045, 0.10, rnd.x);

    float tilt = (Hash1(cellID + 3.1, seed) - 0.5) * 0.12;
    float trailLength = lerp(0.55, 0.95, rnd.y);
    float2 tailCenter = headCenter + float2(tilt, trailLength);

    float4 head = DropSurface(cellUV - headCenter, headRadius, rnd.x * 23.0);

    float h;
    float2 closest = SegmentClosest(cellUV, headCenter, tailCenter, h);
    float2 toPoint = cellUV - closest;
    // Thins out quickly over the first 30% of the trail, then stays thin - a round head
    // with a long near-uniform drip, instead of one long cone tapering to a point.
    float widthFactor = lerp(0.55, 0.12, smoothstep(0.0, 0.3, h));
    float halfWidth = headRadius * widthFactor;
    float4 trail = DropSurface(toPoint, halfWidth, rnd.y * 17.0);
    trail *= pow(1.0 - h, 1.5);

    return CombineDrop(head, trail) * visible;
}

// Combines both droplet layers, keeping whichever is closer to the surface at each pixel.
float4 RaindropField(float2 uv, float staticUVScale, float rollingUVScale, float time,
    float staticDensity, float rollingDensity, float seed)
{
    float4 staticDrop = StaticDroplets(uv, staticUVScale, staticDensity, seed);
    float4 rollingDrop = RollingDroplets(uv, rollingUVScale, time, rollingDensity, seed + 41.0);

    return CombineDrop(staticDrop, rollingDrop);
}

#endif // LLUVIA_SALPICADURAS_INCLUDED
