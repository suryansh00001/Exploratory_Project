#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class PedestrianPrefabUtility
{
    private const string PrefabRootFolder = "Assets/_Project/Prefabs";
    private const string PedestrianFolder = "Assets/_Project/Prefabs/Pedestrians";
    private const string MaterialFolder = "Assets/_Project/Prefabs/Pedestrians/Materials";

    private const string PrefabPath = "Assets/_Project/Prefabs/Pedestrians/PedestrianCylinder.prefab";
    private const string MaterialPath = "Assets/_Project/Prefabs/Pedestrians/Materials/PedestrianCylinder.mat";

    [MenuItem("Sumo2Unity/0. Create Pedestrian Prefab")]
    public static void CreateOrUpdatePedestrianPrefab()
    {
        EnsureFolders();

        GameObject tempPedestrian = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        tempPedestrian.name = "PedestrianCylinder";
        tempPedestrian.transform.position = Vector3.zero;
        tempPedestrian.transform.rotation = Quaternion.identity;
        tempPedestrian.transform.localScale = new Vector3(0.45f, 0.9f, 0.45f);

        ConfigureCollider(tempPedestrian);
        ConfigureRigidbody(tempPedestrian);
        ConfigureRenderer(tempPedestrian);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(tempPedestrian, PrefabPath);
        Object.DestroyImmediate(tempPedestrian);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (prefab != null)
        {
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"Pedestrian prefab created/updated at: {PrefabPath}");
        }
        else
        {
            Debug.LogError("Failed to create pedestrian prefab.");
        }
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(PrefabRootFolder))
        {
            AssetDatabase.CreateFolder("Assets/_Project", "Prefabs");
        }

        if (!AssetDatabase.IsValidFolder(PedestrianFolder))
        {
            AssetDatabase.CreateFolder(PrefabRootFolder, "Pedestrians");
        }

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateFolder(PedestrianFolder, "Materials");
        }
    }

    private static void ConfigureCollider(GameObject pedestrian)
    {
        CapsuleCollider capsule = pedestrian.GetComponent<CapsuleCollider>();
        if (capsule == null)
        {
            capsule = pedestrian.AddComponent<CapsuleCollider>();
        }

        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.radius = 0.35f;
        capsule.height = 1.8f;
    }

    private static void ConfigureRigidbody(GameObject pedestrian)
    {
        Rigidbody rb = pedestrian.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = pedestrian.AddComponent<Rigidbody>();
        }

        rb.useGravity = false;
        rb.isKinematic = false;
        rb.linearDamping = 1f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
    }

    private static void ConfigureRenderer(GameObject pedestrian)
    {
        Renderer renderer = pedestrian.GetComponent<Renderer>();
        if (renderer == null)
        {
            return;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader);
            material.color = new Color(0.95f, 0.72f, 0.32f, 1f);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        renderer.sharedMaterial = material;
    }
}
#endif
