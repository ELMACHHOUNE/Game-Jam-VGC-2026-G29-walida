using FMODUnity;
using UnityEngine;
using UnityEngine.Events;
using Walida.Audio;

[ExecuteAlways]
public class MedinaExitDoor : MonoBehaviour
{
    private static bool _isGameCompleted;

    [Header("Door Dimensions")]
    [SerializeField] private float openingWidth = 6.6f;
    [SerializeField] private float openingHeight = 5.85f;
    [SerializeField] private float doorThickness = 0.45f;
    [SerializeField] private float openSwingAngle = 104f;
    [SerializeField] private float openSmoothSpeed = 3.2f;

    [Header("Lock State")]
    [SerializeField] private bool startLocked = true;
    [SerializeField] private bool syncWithGameCompletedFlag = true;
    [SerializeField] private string lockedDialogueKnot = "";

    [Header("Visuals & Physics")]
    [SerializeField] private Material doorMaterial;
    [SerializeField] private Transform leftDoorPivot;
    [SerializeField] private Transform rightDoorPivot;
    [SerializeField] private BoxCollider blockingCollider;

    [Header("FMOD Audio")]
    [SerializeField] private EventReference doorCreakEvent;
    [SerializeField, Range(0f, 1f)] private float doorAudioVolume = 0.85f;

    [Header("Events")]
    [SerializeField] private UnityEvent onLockedAttempt;
    [SerializeField] private UnityEvent onDoorUnlocked;
    [SerializeField] private UnityEvent onUnlockedInteract;

    private float _currentOpenT;
    private float _rattleTimer;
    private Mesh _leftLeafMesh;
    private Mesh _rightLeafMesh;
    private bool _isUnlocked;
    private bool _needsRebuild;

    public bool IsUnlocked => _isUnlocked;
    public static bool IsGameCompleted => _isGameCompleted;

    public static void SetGameCompleted(bool completed)
    {
        _isGameCompleted = completed;
        MedinaExitDoor[] doors = FindObjectsByType<MedinaExitDoor>(FindObjectsInactive.Exclude);
        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] != null && doors[i].syncWithGameCompletedFlag)
            {
                if (completed)
                {
                    doors[i].UnlockDoor();
                }
                else
                {
                    doors[i].LockDoor();
                }
            }
        }
    }

    private void OnEnable()
    {
        EnsureDoorHierarchy();
        if (Application.isPlaying)
        {
            _isUnlocked = !startLocked || (syncWithGameCompletedFlag && _isGameCompleted);
            _currentOpenT = _isUnlocked ? 1f : 0f;
            ApplyDoorStateImmediate();
        }
        else
        {
            _isUnlocked = !startLocked;
            _currentOpenT = _isUnlocked ? 1f : 0f;
            ApplyDoorStateImmediate();
        }
    }

    private void OnValidate()
    {
        openingWidth = Mathf.Max(2f, openingWidth);
        openingHeight = Mathf.Max(2f, openingHeight);
        doorThickness = Mathf.Clamp(doorThickness, 0.15f, 1.5f);
        _needsRebuild = true;
    }

    private void Update()
    {
        if (_needsRebuild)
        {
            _needsRebuild = false;
            EnsureDoorHierarchy();
            ApplyDoorStateImmediate();
        }

        if (!Application.isPlaying)
        {
            UpdateSortingOrders();
            return;
        }

        if (syncWithGameCompletedFlag && _isGameCompleted && !_isUnlocked)
        {
            UnlockDoor();
        }

        float targetT = _isUnlocked ? 1f : 0f;
        if (Mathf.Abs(_currentOpenT - targetT) > 0.001f)
        {
            _currentOpenT = Mathf.Lerp(_currentOpenT, targetT, 1f - Mathf.Exp(-openSmoothSpeed * Time.deltaTime));
            if (Mathf.Abs(_currentOpenT - targetT) <= 0.001f)
            {
                _currentOpenT = targetT;
            }
        }

        float rattleAngle = 0f;
        if (_rattleTimer > 0f)
        {
            _rattleTimer = Mathf.Max(0f, _rattleTimer - Time.deltaTime);
            rattleAngle = Mathf.Sin(_rattleTimer * 46f) * 3.5f * (_rattleTimer / 0.35f);
        }

        float eased = Mathf.SmoothStep(0f, 1f, _currentOpenT);
        float angle = eased * openSwingAngle;

        if (leftDoorPivot != null)
        {
            leftDoorPivot.localRotation = Quaternion.Euler(0f, angle + rattleAngle, 0f);
        }

        if (rightDoorPivot != null)
        {
            rightDoorPivot.localRotation = Quaternion.Euler(0f, -angle - rattleAngle, 0f);
        }

        if (blockingCollider != null)
        {
            blockingCollider.enabled = !_isUnlocked;
        }

        UpdateSortingOrders();
    }

    public void Configure(float width, float height, float thickness, Material mat)
    {
        openingWidth = width;
        openingHeight = height;
        doorThickness = thickness;
        doorMaterial = mat;
        EnsureDoorHierarchy();
        ApplyDoorStateImmediate();
    }

    [ContextMenu("Unlock Medina Door (End Game)")]
    public void UnlockDoor()
    {
        if (_isUnlocked)
        {
            return;
        }

        _isUnlocked = true;
        if (blockingCollider != null)
        {
            blockingCollider.enabled = false;
        }

        if (Application.isPlaying)
        {
            FMODAudioManager.PlayOneShot(doorCreakEvent, FMODEvents.WoodenDoorCreak, doorAudioVolume);
        }

        onDoorUnlocked?.Invoke();
    }

    [ContextMenu("Lock Medina Door")]
    public void LockDoor()
    {
        _isUnlocked = false;
        if (blockingCollider != null)
        {
            blockingCollider.enabled = true;
        }
    }

    public void HandleDoorInteraction(MedinaInteractable interactable)
    {
        if (!_isUnlocked)
        {
            _rattleTimer = 0.35f;
            if (Application.isPlaying)
            {
                FMODAudioManager.PlayOneShot(doorCreakEvent, FMODEvents.WoodenDoorCreak, doorAudioVolume * 0.7f, pitch: 0.9f);
            }
            onLockedAttempt?.Invoke();

            if (!string.IsNullOrWhiteSpace(lockedDialogueKnot))
            {
                MedinaLevelManager manager = FindAnyObjectByType<MedinaLevelManager>();
                if (manager != null)
                {
                    manager.TriggerDialogue(lockedDialogueKnot, "Bab Oudaia Gate");
                }
            }
            else
            {
                Debug.Log("[MedinaExitDoor] Bab Oudaia gate is locked until the end of the game.");
            }

            return;
        }

        onUnlockedInteract?.Invoke();
    }

    public void EnsureDoorHierarchy()
    {
        float halfW = openingWidth * 0.5f;
        float leafW = halfW + 0.06f;

        if (leftDoorPivot == null)
        {
            Transform found = transform.Find("LeftDoorPivot");
            if (found != null)
            {
                leftDoorPivot = found;
            }
            else
            {
                GameObject go = new GameObject("LeftDoorPivot");
                go.transform.SetParent(transform, false);
                leftDoorPivot = go.transform;
            }
        }

        if (rightDoorPivot == null)
        {
            Transform found = transform.Find("RightDoorPivot");
            if (found != null)
            {
                rightDoorPivot = found;
            }
            else
            {
                GameObject go = new GameObject("RightDoorPivot");
                go.transform.SetParent(transform, false);
                rightDoorPivot = go.transform;
            }
        }

        leftDoorPivot.localPosition = new Vector3(-halfW, 0f, 0f);
        rightDoorPivot.localPosition = new Vector3(halfW, 0f, 0f);

        if (_leftLeafMesh == null)
        {
            _leftLeafMesh = new Mesh();
            _leftLeafMesh.name = "MedinaLeftDoorLeafMesh";
            _leftLeafMesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        if (_rightLeafMesh == null)
        {
            _rightLeafMesh = new Mesh();
            _rightLeafMesh.name = "MedinaRightDoorLeafMesh";
            _rightLeafMesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        BuildLeafMesh(_leftLeafMesh, 0f, leafW, openingHeight, doorThickness, new Rect(0.01f, 0.01f, 0.48f, 0.98f));
        BuildLeafMesh(_rightLeafMesh, -leafW, 0f, openingHeight, doorThickness, new Rect(0.51f, 0.01f, 0.48f, 0.98f));

        SetupLeafVisual(leftDoorPivot, _leftLeafMesh);
        SetupLeafVisual(rightDoorPivot, _rightLeafMesh);

        if (blockingCollider == null)
        {
            blockingCollider = GetComponent<BoxCollider>();
            if (blockingCollider == null)
            {
                blockingCollider = gameObject.AddComponent<BoxCollider>();
            }
        }

        blockingCollider.center = new Vector3(0f, openingHeight * 0.5f, 0f);
        blockingCollider.size = new Vector3(openingWidth + 0.8f, openingHeight + 0.4f, Mathf.Max(2.8f, doorThickness * 5f));
        blockingCollider.enabled = !_isUnlocked;
    }

    private void SetupLeafVisual(Transform pivot, Mesh mesh)
    {
        MeshFilter mf = pivot.GetComponent<MeshFilter>();
        if (mf == null) mf = pivot.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = pivot.GetComponent<MeshRenderer>();
        if (mr == null) mr = pivot.gameObject.AddComponent<MeshRenderer>();
        if (doorMaterial != null)
        {
            mr.sharedMaterial = doorMaterial;
        }

        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    private void ApplyDoorStateImmediate()
    {
        float angle = _currentOpenT * openSwingAngle;
        if (leftDoorPivot != null)
        {
            leftDoorPivot.localRotation = Quaternion.Euler(0f, angle, 0f);
        }

        if (rightDoorPivot != null)
        {
            rightDoorPivot.localRotation = Quaternion.Euler(0f, -angle, 0f);
        }

        if (blockingCollider != null)
        {
            blockingCollider.enabled = !_isUnlocked;
        }

        UpdateSortingOrders();
    }

    private void UpdateSortingOrders()
    {
        int order = -Mathf.RoundToInt(transform.position.z * 100f);
        if (leftDoorPivot != null)
        {
            MeshRenderer lmr = leftDoorPivot.GetComponent<MeshRenderer>();
            if (lmr != null) lmr.sortingOrder = order;
        }

        if (rightDoorPivot != null)
        {
            MeshRenderer rmr = rightDoorPivot.GetComponent<MeshRenderer>();
            if (rmr != null) rmr.sortingOrder = order;
        }
    }

    private static void BuildLeafMesh(Mesh mesh, float xMin, float xMax, float h, float thick, Rect frontUV)
    {
        float hz = thick * 0.5f;
        Vector3[] verts = new Vector3[20];
        Vector3[] norms = new Vector3[20];
        Vector2[] uvs = new Vector2[20];
        Color[] cols = new Color[20];
        int[] tris = new int[30];

        int vi = 0;
        int ti = 0;

        void AddFace(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Vector3 n, Rect uv, float aoBot, float aoTop)
        {
            int b = vi;
            verts[vi] = bl; norms[vi] = n; uvs[vi] = new Vector2(uv.xMin, uv.yMin); cols[vi] = new Color(aoBot, aoBot, aoBot, 1f); vi++;
            verts[vi] = br; norms[vi] = n; uvs[vi] = new Vector2(uv.xMax, uv.yMin); cols[vi] = new Color(aoBot, aoBot, aoBot, 1f); vi++;
            verts[vi] = tr; norms[vi] = n; uvs[vi] = new Vector2(uv.xMax, uv.yMax); cols[vi] = new Color(aoTop, aoTop, aoTop, 1f); vi++;
            verts[vi] = tl; norms[vi] = n; uvs[vi] = new Vector2(uv.xMin, uv.yMax); cols[vi] = new Color(aoTop, aoTop, aoTop, 1f); vi++;

            tris[ti++] = b; tris[ti++] = b + 2; tris[ti++] = b + 1;
            tris[ti++] = b; tris[ti++] = b + 3; tris[ti++] = b + 2;
        }

        Rect edgeUV = new Rect(frontUV.xMin, frontUV.yMin, frontUV.width * 0.08f, frontUV.height);

        AddFace(new Vector3(xMin, 0f, -hz), new Vector3(xMax, 0f, -hz), new Vector3(xMax, h, -hz), new Vector3(xMin, h, -hz), Vector3.back, frontUV, 0.80f, 0.96f);
        AddFace(new Vector3(xMax, 0f, hz), new Vector3(xMin, 0f, hz), new Vector3(xMin, h, hz), new Vector3(xMax, h, hz), Vector3.forward, frontUV, 0.80f, 0.96f);
        AddFace(new Vector3(xMin, 0f, hz), new Vector3(xMin, 0f, -hz), new Vector3(xMin, h, -hz), new Vector3(xMin, h, hz), Vector3.left, edgeUV, 0.78f, 0.92f);
        AddFace(new Vector3(xMax, 0f, -hz), new Vector3(xMax, 0f, hz), new Vector3(xMax, h, hz), new Vector3(xMax, h, -hz), Vector3.right, edgeUV, 0.78f, 0.92f);
        AddFace(new Vector3(xMin, h, -hz), new Vector3(xMax, h, -hz), new Vector3(xMax, h, hz), new Vector3(xMin, h, hz), Vector3.up, edgeUV, 0.94f, 0.94f);

        mesh.Clear();
        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.uv = uvs;
        mesh.colors = cols;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
    }

    private void OnDestroy()
    {
        if (_leftLeafMesh != null)
        {
            if (Application.isPlaying) Destroy(_leftLeafMesh);
            else DestroyImmediate(_leftLeafMesh);
        }

        if (_rightLeafMesh != null)
        {
            if (Application.isPlaying) Destroy(_rightLeafMesh);
            else DestroyImmediate(_rightLeafMesh);
        }
    }
}
