using CesiumForUnity;
using NaughtyAttributes.Test;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class DistanceCheck : MonoBehaviour
{

    [SerializeField] public float distance;
    [SerializeField] public bool print;
    [SerializeField] CesiumGeoreference GeoRef;
    
    [SerializeField] double2 latlon1;
    [SerializeField] double2 latlon2;

    [SerializeField] Transform obj1; 
    [SerializeField] Transform obj2;

    [SerializeField] double surfaceHeight;

    Vector3 getSpherePostion(double2 latlon)
    {
        double lat = latlon.x;
        double lon = latlon.y;
        if (lon < -180) { lon = 360 + lon; }
        double3 lonlath = new double3(lon, lat, surfaceHeight);
        double3 ecef = GeoRef.ellipsoid.LongitudeLatitudeHeightToCenteredFixed(lonlath);
        double3 d3pos = GeoRef.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
        Vector3 v3pos = new Vector3((float)d3pos.x, (float)d3pos.y, (float)d3pos.z);
        return v3pos;
    }
    // Start is called before the first frame update
    void Start()
    {

    }

    void FixedUpdate()
    {
        obj1.position = getSpherePostion(latlon1);
        obj2.position = getSpherePostion(latlon2);
        distance = (obj2.position - obj1.position).magnitude;
        if (print)
        {
            Debug.Log(distance);
        }
    }
}
