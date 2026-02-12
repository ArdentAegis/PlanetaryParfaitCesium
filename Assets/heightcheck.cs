using CesiumForUnity;
using System;
using System.Collections;
using System.Collections.Generic;
using TerrainEngine;
using Unity.Mathematics;
using UnityEngine;

public class heightcheck : MonoBehaviour
{
    [SerializeField]
    public Material material;

    [Range(0, 1)]
    public float u = 0;
    [Range(0, 1)]
    public float v = 0;

    [SerializeField]
    public Transform check;

    [SerializeField]
    public CesiumGeoreference GeoRef;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        JMARSScene scene = SceneMaterializer.singleton.selectedScene;
        string[] bllonlat = scene.bottom_left.Split(", ");
        float startlon = Convert.ToSingle(bllonlat[0]);
        float startlat = Convert.ToSingle(bllonlat[1]);

        string[] trlonlat = scene.top_right.Split(", ");
        float endlon = Convert.ToSingle(trlonlat[0]);
        float endlat = Convert.ToSingle(trlonlat[1]);

        double londist = endlon - startlon;
        if (startlon > endlon)
        {
            londist += 360;
        }
        double latdist = endlat - startlat;
        if (startlat > endlat)
        {
            latdist += 180;
        }

        double lon = startlon + londist * u % 360;
        double lat = startlat + latdist * v % 180;
        
        Texture2D heightMap = material.GetTexture("_HeightMap") as Texture2D;
        float scaleFactor = material.GetFloat("_scaleFactor");
        double height = heightMap.GetPixelBilinear(u, v).r * scaleFactor * .00001 * (1/ GeoRef.scale);
        double3 lonlath = new double3(lon, lat, height);
        double3 ecef = GeoRef.ellipsoid.LongitudeLatitudeHeightToCenteredFixed(lonlath);
        double3 worldPos = GeoRef.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
        Vector3 position = new Vector3((float)worldPos.x, (float)worldPos.y, (float)worldPos.z);
        check.position = position;
    }
}
