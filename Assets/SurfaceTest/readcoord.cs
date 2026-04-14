using System;
using System.Collections;
using System.Collections.Generic;
using TerrainEngine;
using Unity.Mathematics;
using UnityEngine;

public class readcoord : MonoBehaviour
{
    [Range(-90, 90)]
    public float lat;
    [Range(-180, 180)]
    public float lon;

    [SerializeField]
    public Material material;

    //[SerializeField]
    //public float numericValue;
    [SerializeField]
    public Color texColor;

    public heightcheck check;
    // Start is called before the first frame update
    void Start()
    {
        
    }
    public float2 LatLontoUV(float latitude, float longitude)
    {
        JMARSScene scene = SceneMaterializer.singleton.selectedScene;

        string[] bllonlat = scene.bottom_left.Split(", ");
        float startlon = Convert.ToSingle(bllonlat[0]);
        // Convert from 0 -> 360 to -180 -> 180
        if (startlon > 180)
        {
            startlon -= 360;
        }
        float startlat = Convert.ToSingle(bllonlat[1]);

        string[] trlonlat = scene.top_right.Split(", ");
        float endlon = Convert.ToSingle(trlonlat[0]);
        // Convert from 0 -> 360 to -180 -> 180
        if (endlon > 180)
        {
            endlon -= 360;
        }
        float endlat = Convert.ToSingle(trlonlat[1]);

        float u = ((float)longitude - startlon) / (endlon - startlon);
        float v = ((float)latitude - startlat) / (endlat - startlat);
        return new float2(u, v);
    }

    // Update is called once per frame
    void Update()
    {
        float2 uv = LatLontoUV(lat, lon);
        //Texture tex = material.mainTexture as Texture2D;
        //Debug.Log(material.GetTexturePropertyNames()[0]);
        //Debug.Log(material.GetTexture("_MainTex") as Texture2D);

        Texture badtex = material.GetTexture("_MainTex");
        Texture2D goodtex = new Texture2D(badtex.width, badtex.height);
        //Debug.Log(Graphics.ConvertTexture(badtex, goodtex));

        check.u = uv.x; check.v = uv.y;

        texColor = goodtex.GetPixelBilinear(uv.x, 1f-uv.y);
    }
}
