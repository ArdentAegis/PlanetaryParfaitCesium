using CesiumForUnity;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

public class CesiumTriangleArea : MonoBehaviour
{
    [SerializeField]
    public CesiumGeoreference GeoRef;

    [Range(-90, 90)]
    public double lat1;
    [Range(-180, 180)]
    public double lon1;

    [Range(-90, 90)]
    public double lat2;
    [Range(-180, 180)]
    public double lon2;

    [Range(-90, 90)]
    public double lat3;
    [Range(-180, 180)]
    public double lon3;

    [SerializeField]
    public float area;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        double3 lonlath1 = new double3(lon1, lat1, 0);
        double3 lonlath2 = new double3(lon2, lat3, 0);
        double3 lonlath3 = new double3(lon3, lat3, 0);

        double3 ecef1 = GeoRef.ellipsoid.LongitudeLatitudeHeightToCenteredFixed(lonlath1);
        double3 ecef2 = GeoRef.ellipsoid.LongitudeLatitudeHeightToCenteredFixed(lonlath2);
        double3 ecef3 = GeoRef.ellipsoid.LongitudeLatitudeHeightToCenteredFixed(lonlath3);

        double3 world1 = GeoRef.TransformEarthCenteredEarthFixedPositionToUnity(ecef1);
        double3 world2 = GeoRef.TransformEarthCenteredEarthFixedPositionToUnity(ecef2);
        double3 world3 = GeoRef.TransformEarthCenteredEarthFixedPositionToUnity(ecef3);

        Vector3 pos1 = new Vector3((float)world1.x, (float)world1.y, (float)world1.z) * (1 / (float)GeoRef.scale);
        Vector3 pos2 = new Vector3((float)world2.x, (float)world2.y, (float)world2.z) * (1 / (float)GeoRef.scale);
        Vector3 pos3 = new Vector3((float)world3.x, (float)world3.y, (float)world3.z) * (1 / (float)GeoRef.scale);

        area = 0.5f * Vector3.Cross((pos2 - pos1), (pos3 - pos1)).magnitude;

    }
}
