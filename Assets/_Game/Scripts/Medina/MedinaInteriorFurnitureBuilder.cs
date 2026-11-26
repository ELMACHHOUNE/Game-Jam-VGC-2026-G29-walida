using System.Collections.Generic;
using UnityEngine;

public enum MedinaInteriorTheme
{
    BlueSalon,
    DarFekhara,
    AndalusianRiad,
    CafeMaure,
    SoukBazaar,
    MosqueHall,
    PalaceMuseum,
    FortressChamber
}

public static class MedinaInteriorFurnitureBuilder
{
    private static readonly Rect UvCedar = TileUV(0, 0);
    private static readonly Rect UvCounterWood = TileUV(1, 0);
    private static readonly Rect UvWicker = TileUV(2, 0);
    private static readonly Rect UvStone = TileUV(3, 0);
    private static readonly Rect UvSedariBlue = TileUV(0, 1);
    private static readonly Rect UvCushionTerra = TileUV(1, 1);
    private static readonly Rect UvBerberRug = TileUV(2, 1);
    private static readonly Rect UvEmeraldSilk = TileUV(3, 1);
    private static readonly Rect UvBrass = TileUV(0, 2);
    private static readonly Rect UvBlueZellige = TileUV(1, 2);
    private static readonly Rect UvGreenZellige = TileUV(2, 2);
    private static readonly Rect UvLeather = TileUV(3, 2);
    private static readonly Rect UvSpice = TileUV(0, 3);
    private static readonly Rect UvClay = TileUV(1, 3);
    private static readonly Rect UvVitrine = TileUV(2, 3);
    private static readonly Rect UvFoliage = TileUV(3, 3);

    private struct BuilderState
    {
        public List<Vector3> Verts;
        public List<Vector3> Norms;
        public List<Vector2> Uvs;
        public List<Color> Cols;
        public List<int> Tris;
        public Vector2 Center;
        public Vector2 AxisU;
        public Vector2 AxisV;
        public float Scale;
        public float BaseY;
    }

    private static Rect TileUV(int col, int pngRow)
    {
        float pad = 0.010f;
        float u0 = col * 0.25f + pad;
        float v0 = (3 - pngRow) * 0.25f + pad;
        float sz = 0.25f - pad * 2f;
        return new Rect(u0, v0, sz, sz);
    }

    public static void BuildFurnitureMesh(Mesh targetMesh, Vector2[] insetPoly, Vector2 centroid, Vector2 axisU, Vector2 axisV, MedinaInteriorTheme theme, float baseY)
    {
        float safeRadius = ComputeInscribedRadius(insetPoly, centroid);
        float scale = Mathf.Clamp(safeRadius / 2.25f, 0.65f, 1.55f);

        BuilderState s = new BuilderState
        {
            Verts = new List<Vector3>(512),
            Norms = new List<Vector3>(512),
            Uvs = new List<Vector2>(512),
            Cols = new List<Color>(512),
            Tris = new List<int>(768),
            Center = centroid,
            AxisU = axisU,
            AxisV = axisV,
            Scale = scale,
            BaseY = baseY
        };

        switch (theme)
        {
            case MedinaInteriorTheme.DarFekhara:
                BuildDarFekhara(ref s);
                break;
            case MedinaInteriorTheme.AndalusianRiad:
                BuildAndalusianRiad(ref s);
                break;
            case MedinaInteriorTheme.CafeMaure:
                BuildCafeMaure(ref s);
                break;
            case MedinaInteriorTheme.SoukBazaar:
                BuildSoukBazaar(ref s);
                break;
            case MedinaInteriorTheme.MosqueHall:
                BuildMosqueHall(ref s);
                break;
            case MedinaInteriorTheme.PalaceMuseum:
                BuildPalaceMuseum(ref s);
                break;
            case MedinaInteriorTheme.FortressChamber:
                BuildFortressChamber(ref s);
                break;
            default:
                BuildBlueSalon(ref s);
                break;
        }

        targetMesh.Clear();
        targetMesh.SetVertices(s.Verts);
        targetMesh.SetNormals(s.Norms);
        targetMesh.SetUVs(0, s.Uvs);
        targetMesh.SetColors(s.Cols);
        targetMesh.SetTriangles(s.Tris, 0);
        targetMesh.RecalculateBounds();
    }

    private static void BuildBlueSalon(ref BuilderState s)
    {
        Color w = Color.white;
        Color warm = new Color(1.04f, 0.98f, 0.88f);
        Color silver = new Color(0.86f, 0.90f, 0.94f);

        AddBox(ref s, 0f, 1.55f, 0f, 3.65f, 0.36f, 1.08f, UvCedar, w);
        AddBox(ref s, 0f, 1.55f, 0.36f, 3.50f, 0.30f, 0.98f, UvSedariBlue, w);
        AddBox(ref s, 0f, 1.94f, 0.56f, 3.50f, 0.58f, 0.26f, UvCedar, w);
        for (int i = -1; i <= 1; i++)
        {
            Rect cUv = i == 0 ? UvCushionTerra : UvSedariBlue;
            AddBox(ref s, i * 1.06f, 1.72f, 0.64f, 0.88f, 0.46f, 0.26f, cUv, warm);
        }

        AddBox(ref s, -1.65f, 0.25f, 0f, 1.08f, 0.36f, 2.24f, UvCedar, w);
        AddBox(ref s, -1.65f, 0.25f, 0.36f, 0.98f, 0.30f, 2.10f, UvSedariBlue, w);
        AddBox(ref s, -2.04f, 0.25f, 0.56f, 0.26f, 0.58f, 2.10f, UvCedar, w);
        AddBox(ref s, -1.84f, 0.65f, 0.64f, 0.26f, 0.44f, 0.80f, UvCushionTerra, w);
        AddBox(ref s, -1.84f, -0.25f, 0.64f, 0.26f, 0.44f, 0.80f, UvSedariBlue, warm);

        AddCylinder(ref s, 0.20f, 0.10f, 0f, 0.24f, 0.52f, 8, UvCedar, w);
        AddCylinder(ref s, 0.20f, 0.10f, 0.52f, 0.88f, 0.10f, 10, UvBrass, w);
        AddCylinder(ref s, 0.20f, 0.10f, 0.62f, 0.22f, 0.34f, 8, UvSpice, silver);
        AddCone(ref s, 0.20f, 0.10f, 0.96f, 0.18f, 0.18f, 8, UvSpice, silver);
        AddCylinder(ref s, 0.52f, 0.32f, 0.62f, 0.08f, 0.16f, 6, UvGreenZellige, w);
        AddCylinder(ref s, 0.54f, -0.08f, 0.62f, 0.08f, 0.16f, 6, UvCushionTerra, w);
        AddCylinder(ref s, -0.15f, -0.22f, 0.62f, 0.08f, 0.16f, 6, UvBlueZellige, w);

        AddCylinder(ref s, 0.58f, -1.35f, 0f, 0.56f, 0.46f, 10, UvLeather, w);

        AddLanternStand(ref s, 1.95f, 1.50f);
        AddPottedPlant(ref s, 1.85f, -1.15f, UvBlueZellige);
    }

    private static void BuildDarFekhara(ref BuilderState s)
    {
        Color w = Color.white;
        Color warm = new Color(1.05f, 0.98f, 0.90f);

        AddBox(ref s, 0f, 1.70f, 0f, 2.45f, 1.95f, 0.78f, UvCedar, w);
        AddBox(ref s, 0f, 1.66f, 0.95f, 2.18f, 0.10f, 0.68f, UvCounterWood, w);
        AddBox(ref s, 0f, 1.66f, 1.48f, 2.18f, 0.10f, 0.68f, UvCounterWood, w);
        AddBox(ref s, 0f, 1.70f, 1.95f, 2.60f, 0.16f, 0.88f, UvCedar, w);
        AddCylinder(ref s, -0.65f, 1.62f, 1.05f, 0.22f, 0.30f, 8, UvBlueZellige, w);
        AddCylinder(ref s, 0.0f, 1.62f, 1.05f, 0.26f, 0.26f, 8, UvBrass, warm);
        AddCylinder(ref s, 0.65f, 1.62f, 1.05f, 0.22f, 0.30f, 8, UvBlueZellige, w);
        AddCylinder(ref s, -0.45f, 1.62f, 1.58f, 0.18f, 0.28f, 8, UvBrass, w);
        AddCylinder(ref s, 0.45f, 1.62f, 1.58f, 0.22f, 0.24f, 8, UvClay, w);

        AddCylinder(ref s, 0f, -0.15f, 0f, 0.62f, 0.62f, 8, UvCounterWood, w);
        AddCylinder(ref s, 0f, -0.15f, 0.62f, 0.76f, 0.09f, 8, UvCedar, w);
        AddCylinder(ref s, 0f, -0.15f, 0.71f, 0.48f, 0.07f, 10, UvBrass, w);
        AddCylinder(ref s, 0.05f, -0.10f, 0.78f, 0.18f, 0.28f, 8, UvBrass, warm);

        AddBox(ref s, -1.45f, -0.10f, 0f, 0.96f, 0.44f, 0.96f, UvCushionTerra, w);
        AddBox(ref s, 1.45f, -0.10f, 0f, 0.96f, 0.44f, 0.96f, UvCushionTerra, w);
        AddCylinder(ref s, 0f, -1.45f, 0f, 0.58f, 0.44f, 10, UvLeather, w);

        AddLanternStand(ref s, -1.75f, 1.45f);
        AddLanternStand(ref s, 1.75f, 1.45f);
    }

    private static void BuildAndalusianRiad(ref BuilderState s)
    {
        Color w = Color.white;
        Color water = new Color(0.45f, 0.82f, 0.88f);

        AddCylinder(ref s, 0f, 0f, 0f, 1.18f, 0.48f, 8, UvStone, w);
        AddCylinder(ref s, 0f, 0f, 0.46f, 0.98f, 0.08f, 8, UvGreenZellige, w);
        AddCylinder(ref s, 0f, 0f, 0.50f, 0.84f, 0.06f, 8, UvSpice, water);
        AddCylinder(ref s, 0f, 0f, 0.52f, 0.24f, 0.62f, 8, UvStone, w);
        AddCylinder(ref s, 0f, 0f, 1.14f, 0.42f, 0.12f, 8, UvGreenZellige, w);
        AddCone(ref s, 0f, 0f, 1.26f, 0.16f, 0.24f, 8, UvBrass, w);

        AddPottedPlant(ref s, -1.70f, 1.25f, UvClay);
        AddPottedPlant(ref s, 1.70f, 1.25f, UvClay);
        AddPottedPlant(ref s, -1.65f, -1.25f, UvClay);
        AddPottedPlant(ref s, 1.65f, -1.25f, UvClay);

        AddBox(ref s, 0f, 1.75f, 0f, 2.25f, 0.48f, 0.74f, UvCedar, w);
        AddBox(ref s, 0f, 1.75f, 0.48f, 2.10f, 0.22f, 0.64f, UvCushionTerra, w);
        AddBox(ref s, 0f, 2.04f, 0.58f, 2.15f, 0.52f, 0.18f, UvCedar, w);
    }

    private static void BuildCafeMaure(ref BuilderState s)
    {
        Color w = Color.white;
        Color pastry = new Color(1.00f, 0.86f, 0.62f);
        Color honey = new Color(0.92f, 0.58f, 0.24f);
        Color silver = new Color(0.88f, 0.92f, 0.95f);

        AddBox(ref s, 0f, 1.35f, 0f, 3.75f, 1.15f, 0.96f, UvCounterWood, w);
        AddBox(ref s, 0f, 0.86f, 0.16f, 3.55f, 0.80f, 0.08f, UvBlueZellige, w);
        AddBox(ref s, 0f, 1.35f, 1.15f, 3.92f, 0.15f, 1.12f, UvCedar, w);

        AddCylinder(ref s, -1.15f, 1.35f, 1.30f, 0.42f, 0.07f, 10, UvBrass, w);
        AddCone(ref s, -1.15f, 1.35f, 1.37f, 0.34f, 0.28f, 8, UvSpice, pastry);
        AddCylinder(ref s, 0f, 1.35f, 1.30f, 0.44f, 0.07f, 10, UvBrass, w);
        AddCone(ref s, 0f, 1.35f, 1.37f, 0.36f, 0.26f, 8, UvSpice, honey);
        AddCylinder(ref s, 1.15f, 1.35f, 1.30f, 0.38f, 0.07f, 10, UvSpice, silver);
        AddCylinder(ref s, 1.05f, 1.35f, 1.37f, 0.16f, 0.32f, 8, UvSpice, silver);
        AddCylinder(ref s, 1.35f, 1.35f, 1.37f, 0.14f, 0.28f, 8, UvSpice, silver);

        for (int i = -1; i <= 1; i++)
        {
            float sx = i * 1.15f;
            AddBox(ref s, sx, 0.22f, 0f, 0.56f, 0.52f, 0.56f, UvCedar, w);
            AddBox(ref s, sx, 0.22f, 0.52f, 0.62f, 0.13f, 0.62f, UvWicker, w);
        }

        AddCylinder(ref s, -0.90f, -1.15f, 0f, 0.20f, 0.64f, 8, UvCedar, w);
        AddCylinder(ref s, -0.90f, -1.15f, 0.64f, 0.70f, 0.09f, 10, UvBrass, w);
        AddBox(ref s, 0.35f, -1.15f, 0f, 0.54f, 0.50f, 0.54f, UvCedar, w);
        AddBox(ref s, 0.35f, -1.15f, 0.50f, 0.60f, 0.13f, 0.60f, UvWicker, w);
        AddLanternStand(ref s, 1.75f, -1.05f);
    }

    private static void BuildSoukBazaar(ref BuilderState s)
    {
        Color w = Color.white;
        Color saffron = new Color(1.05f, 0.80f, 0.18f);
        Color paprika = new Color(0.92f, 0.24f, 0.16f);
        Color turmeric = new Color(1.02f, 0.52f, 0.14f);
        Color crimsonTint = new Color(0.92f, 0.42f, 0.38f);

        AddBox(ref s, 0f, 1.45f, 0f, 3.25f, 0.95f, 1.02f, UvCounterWood, w);
        AddBox(ref s, 0f, 1.45f, 0.95f, 3.40f, 0.13f, 1.14f, UvCedar, w);
        AddCylinder(ref s, 0f, 1.45f, 1.08f, 0.10f, 0.55f, 6, UvBrass, w);
        AddBox(ref s, 0f, 1.45f, 1.54f, 0.80f, 0.07f, 0.09f, UvBrass, w);
        AddCylinder(ref s, -0.36f, 1.45f, 1.32f, 0.19f, 0.07f, 8, UvBrass, w);
        AddCylinder(ref s, 0.36f, 1.45f, 1.32f, 0.19f, 0.07f, 8, UvBrass, w);

        AddSpiceBasket(ref s, -1.25f, -0.15f, paprika);
        AddSpiceBasket(ref s, 0f, -0.25f, saffron);
        AddSpiceBasket(ref s, 1.25f, -0.15f, turmeric);

        AddBox(ref s, -1.35f, -1.35f, 0f, 1.48f, 0.44f, 0.44f, UvBerberRug, w);
        AddBox(ref s, -1.35f, -1.35f, 0.44f, 1.40f, 0.40f, 0.40f, UvSedariBlue, w);
        AddBox(ref s, -1.35f, -0.90f, 0f, 1.44f, 0.44f, 0.44f, UvCushionTerra, w);

        AddCylinder(ref s, 1.15f, -1.25f, 0f, 0.58f, 0.48f, 10, UvLeather, w);
        AddCylinder(ref s, 0.20f, -1.45f, 0f, 0.54f, 0.44f, 10, UvLeather, crimsonTint);
        AddCone(ref s, -0.95f, 1.45f, 1.08f, 0.28f, 0.40f, 8, UvClay, w);
        AddLanternStand(ref s, 1.85f, 1.35f);
    }

    private static void BuildMosqueHall(ref BuilderState s)
    {
        Color w = Color.white;
        Color parchment = new Color(0.98f, 0.94f, 0.84f);

        AddBox(ref s, 0f, 1.65f, 0f, 1.38f, 1.85f, 1.18f, UvCedar, w);
        AddBox(ref s, 0f, 1.65f, 1.85f, 1.50f, 0.22f, 1.28f, UvCounterWood, w);
        AddCone(ref s, 0f, 1.65f, 2.07f, 0.26f, 0.42f, 8, UvBrass, w);
        AddBox(ref s, 0f, 0.82f, 0f, 1.22f, 1.18f, 0.66f, UvCounterWood, w);
        AddBox(ref s, 0f, 0.22f, 0f, 1.22f, 0.62f, 0.60f, UvCedar, w);
        AddBox(ref s, 0f, -0.32f, 0f, 1.22f, 0.28f, 0.52f, UvCounterWood, w);

        AddBox(ref s, -1.35f, -0.65f, 0f, 0.74f, 0.48f, 0.58f, UvCedar, w);
        AddBox(ref s, -1.35f, -0.65f, 0.48f, 0.66f, 0.08f, 0.44f, UvSpice, parchment);
        AddBox(ref s, 1.35f, -0.65f, 0f, 0.74f, 0.48f, 0.58f, UvCedar, w);
        AddBox(ref s, 1.35f, -0.65f, 0.48f, 0.66f, 0.08f, 0.44f, UvSpice, parchment);

        AddBox(ref s, 0f, -1.45f, 0f, 2.85f, 0.08f, 0.98f, UvEmeraldSilk, w);

        AddLanternStand(ref s, -1.85f, 1.45f);
        AddLanternStand(ref s, 1.85f, 1.45f);
    }

    private static void BuildPalaceMuseum(ref BuilderState s)
    {
        Color w = Color.white;

        for (int i = -1; i <= 1; i++)
        {
            float vx = i * 1.45f;
            AddBox(ref s, vx, 1.35f, 0f, 0.96f, 0.52f, 0.78f, UvCedar, w);
            AddBox(ref s, vx, 1.35f, 0.52f, 0.92f, 1.36f, 0.72f, UvVitrine, w);
            AddBox(ref s, vx, 1.35f, 1.88f, 1.00f, 0.14f, 0.82f, UvCedar, w);
        }

        AddBox(ref s, 0f, -0.75f, 0f, 2.30f, 0.42f, 0.88f, UvCedar, w);
        AddBox(ref s, 0f, -0.75f, 0.42f, 2.18f, 0.24f, 0.80f, UvEmeraldSilk, w);
        AddLanternStand(ref s, -1.85f, -0.95f);
        AddLanternStand(ref s, 1.85f, -0.95f);
    }

    private static void BuildFortressChamber(ref BuilderState s)
    {
        Color w = Color.white;
        Color iron = new Color(0.32f, 0.30f, 0.32f);
        Color ember = new Color(1.00f, 0.54f, 0.14f);

        AddBox(ref s, -1.15f, 1.15f, 0f, 1.58f, 0.80f, 0.94f, UvCedar, w);
        AddBox(ref s, -1.15f, 1.15f, 0.80f, 1.52f, 0.24f, 0.86f, UvCounterWood, w);
        AddBox(ref s, -1.64f, 1.15f, 0f, 0.14f, 1.06f, 0.98f, UvStone, iron);
        AddBox(ref s, -0.66f, 1.15f, 0f, 0.14f, 1.06f, 0.98f, UvStone, iron);

        AddBox(ref s, 1.25f, 1.10f, 0f, 1.28f, 0.96f, 1.12f, UvCounterWood, w);
        AddBox(ref s, 1.35f, 1.15f, 0.96f, 0.88f, 0.78f, 0.84f, UvCedar, w);

        AddAmphora(ref s, -1.45f, -0.95f, 1.0f);
        AddAmphora(ref s, -0.65f, -1.25f, 0.82f);
        AddAmphora(ref s, 1.35f, -1.05f, 0.95f);

        AddCylinder(ref s, 0.15f, -0.35f, 0f, 0.44f, 0.58f, 8, UvStone, iron);
        AddCylinder(ref s, 0.15f, -0.35f, 0.58f, 0.36f, 0.16f, 8, UvSpice, ember);
    }

    private static void AddSpiceBasket(ref BuilderState s, float u, float v, Color spiceTint)
    {
        AddCylinder(ref s, u, v, 0f, 0.48f, 0.48f, 10, UvWicker, Color.white);
        AddCone(ref s, u, v, 0.46f, 0.44f, 0.50f, 10, UvSpice, spiceTint);
    }

    private static void AddAmphora(ref BuilderState s, float u, float v, float mult)
    {
        AddCylinder(ref s, u, v, 0f, 0.24f * mult, 0.36f * mult, 8, UvClay, Color.white);
        AddCylinder(ref s, u, v, 0.36f * mult, 0.42f * mult, 0.56f * mult, 8, UvClay, Color.white);
        AddCylinder(ref s, u, v, 0.92f * mult, 0.18f * mult, 0.28f * mult, 8, UvClay, Color.white);
    }

    private static void AddLanternStand(ref BuilderState s, float u, float v)
    {
        Color w = Color.white;
        AddCylinder(ref s, u, v, 0f, 0.26f, 0.14f, 8, UvBrass, w);
        AddCylinder(ref s, u, v, 0.14f, 0.08f, 1.35f, 6, UvCedar, w);
        AddCylinder(ref s, u, v, 1.49f, 0.24f, 0.40f, 6, UvBrass, new Color(1.12f, 1.06f, 0.92f));
        AddCone(ref s, u, v, 1.89f, 0.26f, 0.22f, 6, UvBrass, w);
    }

    private static void AddPottedPlant(ref BuilderState s, float u, float v, Rect potUv)
    {
        Color w = Color.white;
        AddCylinder(ref s, u, v, 0f, 0.38f, 0.54f, 8, potUv, w);
        AddCylinder(ref s, u, v, 0.54f, 0.10f, 0.40f, 6, UvCedar, w);
        AddCylinder(ref s, u, v, 0.82f, 0.54f, 0.58f, 8, UvFoliage, w);
        AddCone(ref s, u, v, 1.40f, 0.48f, 0.36f, 8, UvFoliage, w);
    }

    private static Vector3 ToLocal(ref BuilderState s, float u, float v, float y)
    {
        Vector2 p = s.Center + s.AxisU * (u * s.Scale) + s.AxisV * (v * s.Scale);
        return new Vector3(p.x, s.BaseY + y * s.Scale, p.y);
    }

    private static void AddBox(ref BuilderState s, float cu, float cv, float yMin, float sizeU, float sizeY, float sizeV, Rect uv, Color col)
    {
        float hu = sizeU * 0.5f;
        float hv = sizeV * 0.5f;
        float yMax = yMin + sizeY;

        Vector3 p000 = ToLocal(ref s, cu - hu, cv - hv, yMin);
        Vector3 p100 = ToLocal(ref s, cu + hu, cv - hv, yMin);
        Vector3 p110 = ToLocal(ref s, cu + hu, cv - hv, yMax);
        Vector3 p010 = ToLocal(ref s, cu - hu, cv - hv, yMax);

        Vector3 p001 = ToLocal(ref s, cu - hu, cv + hv, yMin);
        Vector3 p101 = ToLocal(ref s, cu + hu, cv + hv, yMin);
        Vector3 p111 = ToLocal(ref s, cu + hu, cv + hv, yMax);
        Vector3 p011 = ToLocal(ref s, cu - hu, cv + hv, yMax);

        Vector3 nU = new Vector3(s.AxisU.x, 0f, s.AxisU.y);
        Vector3 nV = new Vector3(s.AxisV.x, 0f, s.AxisV.y);

        Color botCol = col * (yMin < 0.05f ? 0.82f : 0.92f);
        botCol.a = 1f;
        Color topCol = col * 1.04f;
        topCol.a = 1f;

        AddQuad(ref s, p000, p100, p110, p010, -nV, uv, botCol, col);
        AddQuad(ref s, p101, p001, p011, p111, nV, uv, botCol, col);
        AddQuad(ref s, p001, p000, p010, p011, -nU, uv, botCol, col);
        AddQuad(ref s, p100, p101, p111, p110, nU, uv, botCol, col);
        AddQuad(ref s, p010, p110, p111, p011, Vector3.up, uv, topCol, topCol);
    }

    private static void AddCylinder(ref BuilderState s, float cu, float cv, float yMin, float radius, float height, int sides, Rect uv, Color col)
    {
        float yMax = yMin + height;
        Vector3 topCenter = ToLocal(ref s, cu, cv, yMax);
        Color botCol = col * (yMin < 0.05f ? 0.82f : 0.92f);
        botCol.a = 1f;
        Color topCol = col * 1.04f;
        topCol.a = 1f;

        for (int i = 0; i < sides; i++)
        {
            float f0 = i / (float)sides;
            float f1 = (i + 1) / (float)sides;
            float a0 = f0 * Mathf.PI * 2f;
            float a1 = f1 * Mathf.PI * 2f;
            float u0 = cu + Mathf.Cos(a0) * radius;
            float v0 = cv + Mathf.Sin(a0) * radius;
            float u1 = cu + Mathf.Cos(a1) * radius;
            float v1 = cv + Mathf.Sin(a1) * radius;

            Vector3 b0 = ToLocal(ref s, u0, v0, yMin);
            Vector3 b1 = ToLocal(ref s, u1, v1, yMin);
            Vector3 t1 = ToLocal(ref s, u1, v1, yMax);
            Vector3 t0 = ToLocal(ref s, u0, v0, yMax);

            float am = (a0 + a1) * 0.5f;
            Vector2 n2 = s.AxisU * Mathf.Cos(am) + s.AxisV * Mathf.Sin(am);
            Vector3 n = new Vector3(n2.x, 0f, n2.y).normalized;

            Rect sliceUv = new Rect(Mathf.Lerp(uv.xMin, uv.xMax, f0), uv.yMin, uv.width / sides, uv.height);
            AddQuad(ref s, b0, b1, t1, t0, n, sliceUv, botCol, col);
            AddTri(ref s, topCenter, t0, t1, Vector3.up, uv, topCol);
        }
    }

    private static void AddCone(ref BuilderState s, float cu, float cv, float yMin, float radius, float height, int sides, Rect uv, Color col)
    {
        Vector3 apex = ToLocal(ref s, cu, cv, yMin + height);
        for (int i = 0; i < sides; i++)
        {
            float a0 = (i / (float)sides) * Mathf.PI * 2f;
            float a1 = ((i + 1) / (float)sides) * Mathf.PI * 2f;
            float u0 = cu + Mathf.Cos(a0) * radius;
            float v0 = cv + Mathf.Sin(a0) * radius;
            float u1 = cu + Mathf.Cos(a1) * radius;
            float v1 = cv + Mathf.Sin(a1) * radius;

            Vector3 b0 = ToLocal(ref s, u0, v0, yMin);
            Vector3 b1 = ToLocal(ref s, u1, v1, yMin);

            float am = (a0 + a1) * 0.5f;
            Vector2 n2 = s.AxisU * Mathf.Cos(am) + s.AxisV * Mathf.Sin(am);
            Vector3 n = new Vector3(n2.x, 0.55f, n2.y).normalized;
            AddTri(ref s, b0, b1, apex, n, uv, col);
        }
    }

    private static void AddQuad(ref BuilderState s, Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Vector3 normal, Rect uv, Color botCol, Color topCol)
    {
        int b = s.Verts.Count;
        s.Verts.Add(bl); s.Verts.Add(br); s.Verts.Add(tr); s.Verts.Add(tl);
        s.Norms.Add(normal); s.Norms.Add(normal); s.Norms.Add(normal); s.Norms.Add(normal);
        s.Uvs.Add(new Vector2(uv.xMin, uv.yMin));
        s.Uvs.Add(new Vector2(uv.xMax, uv.yMin));
        s.Uvs.Add(new Vector2(uv.xMax, uv.yMax));
        s.Uvs.Add(new Vector2(uv.xMin, uv.yMax));
        s.Cols.Add(botCol); s.Cols.Add(botCol); s.Cols.Add(topCol); s.Cols.Add(topCol);
        s.Tris.Add(b); s.Tris.Add(b + 2); s.Tris.Add(b + 1);
        s.Tris.Add(b); s.Tris.Add(b + 3); s.Tris.Add(b + 2);
    }

    private static void AddTri(ref BuilderState s, Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Rect uv, Color col)
    {
        int idx = s.Verts.Count;
        s.Verts.Add(a); s.Verts.Add(b); s.Verts.Add(c);
        s.Norms.Add(normal); s.Norms.Add(normal); s.Norms.Add(normal);
        s.Uvs.Add(new Vector2(uv.xMin, uv.yMin));
        s.Uvs.Add(new Vector2(uv.xMax, uv.yMin));
        s.Uvs.Add(new Vector2(uv.center.x, uv.yMax));
        s.Cols.Add(col); s.Cols.Add(col); s.Cols.Add(col);
        s.Tris.Add(idx); s.Tris.Add(idx + 2); s.Tris.Add(idx + 1);
    }

    private static float ComputeInscribedRadius(Vector2[] poly, Vector2 center)
    {
        float minDist = float.MaxValue;
        int n = poly.Length;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % n];
            Vector2 e = b - a;
            float len = e.magnitude;
            if (len < 0.01f) continue;
            float dist = Mathf.Abs((b.x - a.x) * (a.y - center.y) - (a.x - center.x) * (b.y - a.y)) / len;
            if (dist < minDist) minDist = dist;
        }

        return minDist == float.MaxValue ? 2.5f : minDist;
    }
}
