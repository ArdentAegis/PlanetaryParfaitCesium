using CesiumForUnity;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using TerrainEngine;

// Used for debugging: takes in a lat/lon coordinate (with Geodetic latitude directly from JMARS) and places the object 
// this script is attached to at the corresponding place on the Cesium Moon.
public class CesiumPlacementChecker : MonoBehaviour
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
            transform.position = SceneMaterializer.singleton.activeTiles.GetComponent<CesiumJMARSTerrainMaker>().GetSpherePosition(lon, lat);
        }
    }
}
