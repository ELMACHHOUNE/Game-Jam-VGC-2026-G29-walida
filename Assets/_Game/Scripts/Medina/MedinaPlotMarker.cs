using System.Collections.Generic;
using UnityEngine;

public enum MedinaPlotKind
{
    HouseBlueWhite,
    HouseSandArch,
    Souk,
    Mosque,
    DioramaBakery,
    DioramaSpiceSouk,
    DioramaCafeMaure,
    DioramaCarpetAtelier,
    HeritagePalace,
    KasbahTower,
    OpenPlazaOrPOI
}

public class MedinaPlotMarker : MonoBehaviour
{
    public MedinaPlotKind kind = MedinaPlotKind.HouseBlueWhite;
    public float buildingHeight = 6.8f;
    public string notes = "";
    public List<Vector2> localCorners = new List<Vector2>
    {
        new Vector2(-5f, -4f),
        new Vector2(5f, -4f),
        new Vector2(5f, 4f),
        new Vector2(-5f, 4f)
    };

    public Vector3[] GetWorldCorners(float y = 0.2f)
    {
        int count = localCorners != null ? localCorners.Count : 0;
        Vector3[] result = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 world = transform.TransformPoint(new Vector3(localCorners[i].x, 0f, localCorners[i].y));
            world.y = y;
            result[i] = world;
        }

        return result;
    }

    public Color GetKindColor()
    {
        switch (kind)
        {
            case MedinaPlotKind.HouseBlueWhite: return new Color(0.18f, 0.52f, 0.78f, 1f);
            case MedinaPlotKind.HouseSandArch: return new Color(0.84f, 0.68f, 0.45f, 1f);
            case MedinaPlotKind.Souk: return new Color(0.82f, 0.36f, 0.24f, 1f);
            case MedinaPlotKind.Mosque: return new Color(0.20f, 0.68f, 0.42f, 1f);
            case MedinaPlotKind.DioramaBakery: return new Color(0.90f, 0.48f, 0.58f, 1f);
            case MedinaPlotKind.DioramaSpiceSouk: return new Color(0.92f, 0.56f, 0.18f, 1f);
            case MedinaPlotKind.DioramaCafeMaure: return new Color(0.18f, 0.72f, 0.78f, 1f);
            case MedinaPlotKind.DioramaCarpetAtelier: return new Color(0.90f, 0.76f, 0.22f, 1f);
            case MedinaPlotKind.HeritagePalace: return new Color(0.50f, 0.62f, 0.38f, 1f);
            case MedinaPlotKind.KasbahTower: return new Color(0.66f, 0.42f, 0.22f, 1f);
            default: return new Color(0.95f, 0.90f, 0.35f, 1f);
        }
    }

    public void EnsureCounterClockwise()
    {
        if (localCorners == null || localCorners.Count < 3)
        {
            return;
        }

        float signedArea = 0f;
        for (int i = 0; i < localCorners.Count; i++)
        {
            Vector2 a = localCorners[i];
            Vector2 b = localCorners[(i + 1) % localCorners.Count];
            signedArea += (a.x * b.y - b.x * a.y);
        }

        if (signedArea < 0f)
        {
            localCorners.Reverse();
        }
    }

    public void RecenterPivot()
    {
        if (localCorners == null || localCorners.Count == 0)
        {
            return;
        }

        Vector2 sum = Vector2.zero;
        for (int i = 0; i < localCorners.Count; i++)
        {
            sum += localCorners[i];
        }

        Vector2 centroidLocal = sum / localCorners.Count;
        Vector3 worldDelta = transform.TransformVector(new Vector3(centroidLocal.x, 0f, centroidLocal.y));
        transform.position += new Vector3(worldDelta.x, 0f, worldDelta.z);

        for (int i = 0; i < localCorners.Count; i++)
        {
            localCorners[i] -= centroidLocal;
        }
    }

    private void OnDrawGizmos()
    {
        if (localCorners == null || localCorners.Count < 3)
        {
            return;
        }

        Vector3[] pts = GetWorldCorners(0.25f);
        Color col = GetKindColor();
        Gizmos.color = col;

        for (int i = 0; i < pts.Length; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[(i + 1) % pts.Length];
            Gizmos.DrawLine(a, b);
            Gizmos.DrawSphere(a, 0.35f);
        }
    }
}
