using CesiumForUnity;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using TerrainEngine;

public class moonplace : MonoBehaviour
{
    [SerializeField] public CesiumGeoreference Georeference;
    private CesiumEllipsoid ellipsoid;
    [Range(-90, 90)]
    public float lat = 0;
    [Range(-180, 180)]
    public float lon = 0;

    public bool ready = false;

    void Update()
    {
        if (ready)
        {
            transform.position = SceneMaterializer.singleton.activeTiles.GetComponent<SphereShellMaker>().GetSpherePosition(lon, lat);
        }
    }
}
